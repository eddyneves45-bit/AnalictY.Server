using Microsoft.EntityFrameworkCore;
using Opc.Ua;
using Opc.Ua.Client;
using Scada.Core.Models.SQLite;
using Scada.Data.Models;
using Scada.Gateway.Interfaces;

namespace Scada.Api.Services;

internal sealed class OpcuaTagPollingService : BackgroundService
{
    private static readonly TimeSpan SubscriptionRefreshInterval = TimeSpan.FromSeconds(15);
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ITagRuntimeService _tagRuntimeService;
    private readonly ITagValueQueue _tagValueQueue;
    private readonly IIndustrialHeartbeatService _heartbeatService;
    private readonly IOpcuaSessionFactory _sessionFactory;
    private readonly ILogger<OpcuaTagPollingService> _logger;
    private DateTime _lastWarningAt = DateTime.MinValue;

    public OpcuaTagPollingService(
        IServiceScopeFactory scopeFactory,
        ITagRuntimeService tagRuntimeService,
        ITagValueQueue tagValueQueue,
        IIndustrialHeartbeatService heartbeatService,
        IOpcuaSessionFactory sessionFactory,
        ILogger<OpcuaTagPollingService> logger)
    {
        _scopeFactory = scopeFactory;
        _tagRuntimeService = tagRuntimeService;
        _tagValueQueue = tagValueQueue;
        _heartbeatService = heartbeatService;
        _sessionFactory = sessionFactory;
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

                var configs = await dbContext.OpcuaConfigs
                    .AsNoTracking()
                    .Where(c => c.IsActive)
                    .OrderBy(c => c.Id)
                    .ToListAsync(stoppingToken);

                foreach (var config in configs)
                {
                    _heartbeatService.RegisterConnection("OPCUA", config.Id);
                }

                tags = await dbContext.TagConfigs
                    .AsNoTracking()
                    .Where(t => t.IsActive && t.DriverType.ToUpper() == "OPCUA")
                    .OrderBy(t => t.Id)
                    .ToListAsync(stoppingToken);

                if (configs.Count == 0 || tags.Count == 0)
                {
                    await Task.Delay(TimeSpan.FromSeconds(2), stoppingToken);
                    continue;
                }

                var tasks = configs.Select((config, index) =>
                {
                    var tagsForConfig = tags
                        .Where(tag => tag.OpcuaConnectionId == config.Id || (tag.OpcuaConnectionId == null && index == 0))
                        .ToList();

                    return RunSubscriptionAsync(config, tagsForConfig, stoppingToken);
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

                _heartbeatService.RecordError(nameof(OpcuaTagPollingService), ex.Message);
                LogWarningThrottled(ex, "Falha na assinatura OPC UA");
                await Task.Delay(TimeSpan.FromSeconds(3), stoppingToken);
            }
        }
    }

    private async Task RunSubscriptionAsync(OpcuaConfig config, IReadOnlyList<TagConfig> tags, CancellationToken stoppingToken)
    {
        if (tags.Count == 0)
        {
            return;
        }

        using var session = await _sessionFactory.CreateSessionAsync(config, stoppingToken);
        var publishingInterval = Math.Clamp(tags.Min(t => t.PollIntervalMs), 250, 5000);
        var subscription = new Subscription(session.DefaultSubscription)
        {
            DisplayName = $"SCADA OPC UA {config.Id}",
            PublishingInterval = publishingInterval,
            KeepAliveCount = 10,
            LifetimeCount = 30,
            PublishingEnabled = true
        };

        session.AddSubscription(subscription);

        try
        {
            foreach (var tag in tags)
            {
                try
                {
                    var monitoredItem = new MonitoredItem(subscription.DefaultItem)
                    {
                        DisplayName = tag.TagName,
                        StartNodeId = NodeId.Parse(GetNodeAddress(tag.Address)),
                        AttributeId = Attributes.Value,
                        SamplingInterval = Math.Clamp(tag.PollIntervalMs, 100, 5000),
                        QueueSize = 1,
                        DiscardOldest = true,
                        Handle = tag
                    };

                    monitoredItem.Notification += OnMonitoredItemNotification;
                    subscription.AddItem(monitoredItem);
                }
                catch (Exception ex)
                {
                    _tagRuntimeService.UpdateTagConnectionStatus(tag.Id, false);
                    _heartbeatService.RecordError(nameof(OpcuaTagPollingService), ex.Message);
                    LogWarningThrottled(ex, $"Endereco OPC UA invalido na tag {tag.TagName}: {tag.Address}");
                }
            }

            if (subscription.MonitoredItemCount == 0)
            {
                return;
            }

            subscription.Create();

            foreach (var tag in tags)
            {
                _tagRuntimeService.UpdateTagConnectionStatus(tag.Id, true);
            }

            _heartbeatService.RecordConnection("OPCUA", config.Id, DateTime.UtcNow);

            var refreshAt = DateTime.UtcNow + SubscriptionRefreshInterval;
            while (!stoppingToken.IsCancellationRequested && session.Connected && DateTime.UtcNow < refreshAt)
            {
                await Task.Delay(TimeSpan.FromSeconds(1), stoppingToken);
            }
        }
        finally
        {
            try
            {
                subscription.Delete(true);
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "Falha ao remover assinatura OPC UA da conexao {ConnectionId}", config.Id);
            }

            session.RemoveSubscription(subscription);
        }
    }

    private void OnMonitoredItemNotification(MonitoredItem monitoredItem, MonitoredItemNotificationEventArgs eventArgs)
    {
        if (monitoredItem.Handle is not TagConfig tag)
        {
            return;
        }

        foreach (var dataValue in monitoredItem.DequeueValues())
        {
            var quality = StatusCode.IsGood(dataValue.StatusCode) ? "GOOD" : "BAD";
            var sourceTimestamp = dataValue.SourceTimestamp == DateTime.MinValue
                ? DateTime.UtcNow
                : dataValue.SourceTimestamp.ToUniversalTime();
            var receivedAt = DateTime.UtcNow;

            if (!StatusCode.IsGood(dataValue.StatusCode))
            {
                _logger.LogWarning(
                    "Assinatura OPC UA ruim para a tag {TagName} ({Address}): {StatusCode}",
                    tag.TagName,
                    tag.Address,
                    dataValue.StatusCode);
            }

            _ = _tagValueQueue.EnqueueAsync(new TagValueEnvelope(
                tag.Id,
                tag.TagName,
                "OPCUA",
                tag.PersistenceMode,
                tag.OpcuaConnectionId,
                NormalizeValue(tag, dataValue.Value),
                quality,
                sourceTimestamp,
                receivedAt,
                tag.Address));
        }
    }

    private static string GetNodeAddress(string address)
    {
        var separatorIndex = address.LastIndexOf("::", StringComparison.Ordinal);
        return separatorIndex >= 0 ? address[..separatorIndex] : address;
    }

    private static object? NormalizeValue(TagConfig tag, object? value)
    {
        var address = tag.Address;
        var separatorIndex = address.LastIndexOf("::", StringComparison.Ordinal);
        if (separatorIndex >= 0 && separatorIndex + 2 < address.Length &&
            int.TryParse(address[(separatorIndex + 2)..], out var arrayIndex) &&
            arrayIndex >= 0)
        {
            return value switch
            {
                Array array when arrayIndex < array.Length => array.GetValue(arrayIndex),
                _ => value
            };
        }

        if (value is Array fallbackArray && fallbackArray.Length > 0 && IsScalarDataType(tag.DataType))
        {
            return fallbackArray.GetValue(0);
        }

        return value;
    }

    private static bool IsScalarDataType(string? dataType)
    {
        return dataType?.Trim().ToUpperInvariant() switch
        {
            "BOOL" or
            "BOOLEAN" or
            "BYTE" or
            "SBYTE" or
            "INT16" or
            "UINT16" or
            "INT32" or
            "UINT32" or
            "INT64" or
            "UINT64" or
            "FLOAT" or
            "SINGLE" or
            "DOUBLE" or
            "DECIMAL" or
            "STRING" => true,
            _ => false
        };
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
}


