using System.Globalization;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Scada.Core.Models.SQLite;
using Scada.Data.Models;
using Scada.Gateway.Interfaces;

namespace Scada.Api.Services;

internal sealed class MqttTagSubscriptionService : BackgroundService
{
    private static readonly TimeSpan SubscriptionRefreshInterval = TimeSpan.FromSeconds(15);
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ITagRuntimeService _tagRuntimeService;
    private readonly IMqttRuntimeMonitor _mqttRuntimeMonitor;
    private readonly IMqttDiagnosticsRealtimeService _mqttDiagnosticsRealtimeService;
    private readonly ITagValueQueue _tagValueQueue;
    private readonly IIndustrialHeartbeatService _heartbeatService;
    private readonly ILogger<MqttTagSubscriptionService> _logger;
    private DateTime _lastWarningAt = DateTime.MinValue;

    public MqttTagSubscriptionService(
        IServiceScopeFactory scopeFactory,
        ITagRuntimeService tagRuntimeService,
        IMqttRuntimeMonitor mqttRuntimeMonitor,
        IMqttDiagnosticsRealtimeService mqttDiagnosticsRealtimeService,
        ITagValueQueue tagValueQueue,
        IIndustrialHeartbeatService heartbeatService,
        ILogger<MqttTagSubscriptionService> logger)
    {
        _scopeFactory = scopeFactory;
        _tagRuntimeService = tagRuntimeService;
        _mqttRuntimeMonitor = mqttRuntimeMonitor;
        _mqttDiagnosticsRealtimeService = mqttDiagnosticsRealtimeService;
        _tagValueQueue = tagValueQueue;
        _heartbeatService = heartbeatService;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            IReadOnlyList<TagConfig> tags = Array.Empty<TagConfig>();

            try
            {
                using var scope = _scopeFactory.CreateScope();
                var dbContext = scope.ServiceProvider.GetRequiredService<ScadaDbContext>();

                var configs = await dbContext.MqttConfigs
                    .AsNoTracking()
                    .Where(c => c.IsActive)
                    .OrderBy(c => c.Id)
                    .ToListAsync(stoppingToken);

                foreach (var config in configs)
                {
                    _heartbeatService.RegisterConnection("MQTT", config.Id);
                }

                tags = await dbContext.TagConfigs
                    .AsNoTracking()
                    .Where(t => t.IsActive && t.DriverType.ToUpper() == "MQTT")
                    .OrderBy(t => t.Id)
                    .ToListAsync(stoppingToken);

                if (configs.Count == 0)
                {
                    await Task.Delay(TimeSpan.FromSeconds(2), stoppingToken);
                    continue;
                }

                var tasks = configs.Select((config, index) =>
                {
                    var tagsForConfig = tags
                        .Where(tag => tag.MqttConnectionId == config.Id || (tag.MqttConnectionId == null && index == 0))
                        .ToList();

                    return RunSubscriptionsAsync(config, tagsForConfig, stoppingToken);
                }).ToArray();

                await Task.WhenAll(tasks);
                await Task.Delay(TimeSpan.FromSeconds(1), stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                foreach (var tag in tags)
                {
                    _tagRuntimeService.UpdateTagConnectionStatus(tag.Id, false);
                }

                _heartbeatService.RecordError(nameof(MqttTagSubscriptionService), ex.Message);
                LogWarningThrottled(ex, "Falha na assinatura MQTT");
                await Task.Delay(TimeSpan.FromSeconds(3), stoppingToken);
            }
        }
    }

    private async Task RunSubscriptionsAsync(MqttConfig config, IReadOnlyList<TagConfig> tags, CancellationToken stoppingToken)
    {
        var recentMessages = new Dictionary<string, DateTime>(StringComparer.Ordinal);

        var driverConfig = new Scada.Drivers.DTOs.MqttDriverConfig(
            config.BrokerHost,
            string.IsNullOrWhiteSpace(config.ClientId) ? $"scada-runtime-{Guid.NewGuid():N}" : config.ClientId,
            config.Username,
            config.Password,
            config.TlsEnabled,
            config.BrokerPort,
            config.CaCertPath,
            config.ClientCertPath,
            config.ClientKeyPath,
            config.Qos);

        var driver = new Scada.Drivers.Adapters.MqttDriverAdapter(driverConfig);

        try
        {
            await driver.ConnectAsync();
            _mqttRuntimeMonitor.RecordConnected(config.Id);

            var topics = SplitTopics(config.Topics)
                .Concat(tags.Select(tag => GetTagTopic(tag.Address)))
                .Where(topic => !string.IsNullOrWhiteSpace(topic))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            if (topics.Count == 0)
            {
                await Task.Delay(TimeSpan.FromSeconds(2), stoppingToken);
                return;
            }

            foreach (var topic in topics)
            {
                await driver.SubscribeAsync(topic, (receivedTopic, payload) =>
                {
                    if (IsDuplicateMessage(recentMessages, receivedTopic, payload))
                    {
                        return;
                    }

                    var message = _mqttRuntimeMonitor.AddMessage(config.Id, receivedTopic, payload, config.Qos, false);
                    ObserveRealtimePublish(
                        _mqttDiagnosticsRealtimeService.PublishMessageAsync(message),
                        "mensagem MQTT",
                        config.Id);
                    ObserveRealtimePublish(
                        _mqttDiagnosticsRealtimeService.PublishAsync(config.Id),
                        "diagnostico MQTT",
                        config.Id);
                    _heartbeatService.RecordConnection("MQTT", config.Id, DateTime.UtcNow);

                    foreach (var tag in tags.Where(tag => TopicMatches(GetTagTopic(tag.Address), receivedTopic)))
                    {
                        var value = NormalizePayload(payload, tag.DataType, GetJsonField(tag.Address));
                        var now = DateTime.UtcNow;
                        _ = _tagValueQueue.EnqueueAsync(new TagValueEnvelope(
                            tag.Id,
                            tag.TagName,
                            "MQTT",
                            tag.PersistenceMode,
                            config.Id,
                            value,
                            "GOOD",
                            now,
                            now,
                            receivedTopic));
                    }
                });

                _mqttRuntimeMonitor.RegisterSubscription(config.Id, topic);
                ObserveRealtimePublish(
                    _mqttDiagnosticsRealtimeService.PublishAsync(config.Id),
                    "diagnostico MQTT",
                    config.Id);
            }

            foreach (var tag in tags)
            {
                _tagRuntimeService.UpdateTagConnectionStatus(tag.Id, true);
            }

            var refreshAt = DateTime.UtcNow + SubscriptionRefreshInterval;
            while (!stoppingToken.IsCancellationRequested && driver.IsConnected && DateTime.UtcNow < refreshAt)
            {
                await Task.Delay(TimeSpan.FromSeconds(1), stoppingToken);
            }
        }
        catch (Exception ex)
        {
            _mqttRuntimeMonitor.RecordConnectionFailure(config.Id, ex.Message);
            throw;
        }
        finally
        {
            await driver.DisconnectAsync();
        }
    }

    private static IEnumerable<string> SplitTopics(string topics)
    {
        return (topics ?? "")
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(topic => !string.IsNullOrWhiteSpace(topic));
    }

    private static bool TopicMatches(string filter, string topic)
    {
        var filterParts = (filter ?? "").Split('/', StringSplitOptions.None);
        var topicParts = (topic ?? "").Split('/', StringSplitOptions.None);

        for (var i = 0; i < filterParts.Length; i++)
        {
            var part = filterParts[i];
            if (part == "#")
            {
                return true;
            }

            if (i >= topicParts.Length)
            {
                return false;
            }

            if (part != "+" && !string.Equals(part, topicParts[i], StringComparison.Ordinal))
            {
                return false;
            }
        }

        return filterParts.Length == topicParts.Length;
    }

    private static object? NormalizePayload(string payload, string dataType, string? jsonField = null)
    {
        var rawValue = ExtractValue(payload, jsonField);
        var targetType = dataType.Trim().ToUpperInvariant();

        try
        {
            return targetType switch
            {
                "BOOL" or "BOOLEAN" => rawValue.ValueKind == JsonValueKind.True
                    || rawValue.ValueKind == JsonValueKind.False
                        ? rawValue.GetBoolean()
                        : bool.Parse(rawValue.ToString()),
                "INT16" or "INT" or "INT32" => rawValue.ValueKind == JsonValueKind.Number
                    ? rawValue.GetInt32()
                    : int.Parse(rawValue.ToString(), CultureInfo.InvariantCulture),
                "FLOAT" or "DOUBLE" or "REAL" => rawValue.ValueKind == JsonValueKind.Number
                    ? rawValue.GetDouble()
                    : double.Parse(rawValue.ToString(), CultureInfo.InvariantCulture),
                _ => rawValue.ToString()
            };
        }
        catch
        {
            return payload;
        }
    }

    private static JsonElement ExtractValue(string payload, string? jsonField = null)
    {
        try
        {
            using var document = JsonDocument.Parse(payload);
            var root = document.RootElement;
            var value = root.ValueKind == JsonValueKind.Object && !string.IsNullOrWhiteSpace(jsonField) && TryGetJsonPath(root, jsonField, out var fieldProperty)
                ? fieldProperty
                : root.ValueKind == JsonValueKind.Object && root.TryGetProperty("value", out var valueProperty)
                    ? valueProperty
                    : root;

            return value.Clone();
        }
        catch
        {
            using var document = JsonDocument.Parse(JsonSerializer.Serialize(payload));
            return document.RootElement.Clone();
        }
    }

    private static string GetTagTopic(string address)
    {
        var normalizedAddress = address ?? "";
        var separatorIndex = normalizedAddress.IndexOf("::", StringComparison.Ordinal);
        return separatorIndex >= 0 ? normalizedAddress[..separatorIndex] : normalizedAddress;
    }

    private static string? GetJsonField(string address)
    {
        var normalizedAddress = address ?? "";
        var separatorIndex = normalizedAddress.IndexOf("::", StringComparison.Ordinal);
        return separatorIndex >= 0 && separatorIndex + 2 < normalizedAddress.Length
            ? normalizedAddress[(separatorIndex + 2)..]
            : null;
    }

    private static bool TryGetJsonPath(JsonElement root, string path, out JsonElement value)
    {
        if (root.ValueKind == JsonValueKind.Object && root.TryGetProperty(path, out value))
        {
            return true;
        }

        value = root;
        foreach (var segment in path.Split('.', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            if (value.ValueKind == JsonValueKind.Object && value.TryGetProperty(segment, out var property))
            {
                value = property;
                continue;
            }

            if (value.ValueKind == JsonValueKind.Array
                && int.TryParse(segment, NumberStyles.Integer, CultureInfo.InvariantCulture, out var index)
                && index >= 0
                && index < value.GetArrayLength())
            {
                value = value[index];
                continue;
            }

            value = default;
            return false;
        }

        return true;
    }

    private static bool IsDuplicateMessage(Dictionary<string, DateTime> recentMessages, string topic, string payload)
    {
        var now = DateTime.UtcNow;
        var key = $"{topic}\u001f{payload}";

        lock (recentMessages)
        {
            foreach (var expiredKey in recentMessages
                .Where(item => now - item.Value > TimeSpan.FromSeconds(2))
                .Select(item => item.Key)
                .ToList())
            {
                recentMessages.Remove(expiredKey);
            }

            if (recentMessages.TryGetValue(key, out var lastSeenAt) && now - lastSeenAt <= TimeSpan.FromSeconds(2))
            {
                return true;
            }

            recentMessages[key] = now;
            return false;
        }
    }

    private void LogWarningThrottled(Exception ex, string message)
    {
        if (DateTime.UtcNow - _lastWarningAt < TimeSpan.FromSeconds(30))
        {
            return;
        }

        _lastWarningAt = DateTime.UtcNow;
        _logger.LogWarning(ex, "{Message}", message);
    }

    private void ObserveRealtimePublish(Task publishTask, string publishKind, int connectionId)
    {
        _ = publishTask.ContinueWith(
            task => _logger.LogWarning(
                task.Exception,
                "Falha ao publicar {PublishKind} em tempo real para conexao MQTT {ConnectionId}",
                publishKind,
                connectionId),
            CancellationToken.None,
            TaskContinuationOptions.OnlyOnFaulted,
            TaskScheduler.Default);
    }
}
