using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using Scada.Api.Realtime;
using Scada.Data.Models;
using System.Text.Json;

namespace Scada.Api.Services;

internal sealed class MqttDiagnosticsRealtimeService : IMqttDiagnosticsRealtimeService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IMqttRuntimeMonitor _mqttRuntimeMonitor;
    private readonly IHubContext<MesHub> _hubContext;

    public MqttDiagnosticsRealtimeService(
        IServiceScopeFactory scopeFactory,
        IMqttRuntimeMonitor mqttRuntimeMonitor,
        IHubContext<MesHub> hubContext)
    {
        _scopeFactory = scopeFactory;
        _mqttRuntimeMonitor = mqttRuntimeMonitor;
        _hubContext = hubContext;
    }

    public async Task<object> BuildSnapshotAsync(int connectionId, CancellationToken cancellationToken = default)
    {
        using var scope = _scopeFactory.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<ScadaDbContext>();
        var config = await dbContext.MqttConfigs
            .AsNoTracking()
            .FirstOrDefaultAsync(item => item.Id == connectionId, cancellationToken);

        return _mqttRuntimeMonitor.GetDiagnostics(
            connectionId,
            config?.BrokerHost ?? "-",
            config?.BrokerPort ?? 1883,
            config?.ClientId ?? "-",
            config?.IsActive == true);
    }

    public async Task PublishAsync(int connectionId, CancellationToken cancellationToken = default)
    {
        await _hubContext.Clients
            .Group(MesHub.MqttGroup(connectionId))
            .SendAsync("mqtt:diagnostics", await BuildSnapshotAsync(connectionId, cancellationToken), cancellationToken);
    }

    public IReadOnlyList<object> BuildMessageSnapshot(int connectionId)
    {
        return _mqttRuntimeMonitor
            .GetMessages(connectionId)
            .Select(BuildMessageDto)
            .ToList();
    }

    public Task PublishMessageAsync(MqttRuntimeMessage message, CancellationToken cancellationToken = default)
    {
        return _hubContext.Clients
            .Group(MesHub.MqttGroup(message.ConnectionId))
            .SendAsync("mqtt:message", BuildMessageDto(message), cancellationToken);
    }

    private static object BuildMessageDto(MqttRuntimeMessage message) => new
    {
        connection_id = message.ConnectionId,
        type = "mqtt_message",
        topic = message.Topic,
        value = TryParseJsonValue(message.Payload),
        payload = message.Payload,
        qos = message.Qos,
        retain = message.Retain,
        timestamp = message.Timestamp,
        parsed = ParseTopic(message.Topic)
    };

    private static object ParseTopic(string topic)
    {
        var parts = (topic ?? string.Empty).Split('/', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        return new
        {
            level_0 = parts.ElementAtOrDefault(0),
            level_1 = parts.ElementAtOrDefault(1),
            level_2 = parts.ElementAtOrDefault(2),
            level_3 = parts.ElementAtOrDefault(3),
            raw = topic
        };
    }

    private static object? TryParseJsonValue(string payload)
    {
        try
        {
            using var document = JsonDocument.Parse(payload);
            if (document.RootElement.ValueKind == JsonValueKind.Object && document.RootElement.TryGetProperty("value", out var value))
            {
                return value.ToString();
            }

            return document.RootElement.ToString();
        }
        catch
        {
            return payload;
        }
    }
}
