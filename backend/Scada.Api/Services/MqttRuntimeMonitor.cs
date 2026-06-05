using System.Collections.Concurrent;
using System.Globalization;
using System.Text.Json;

namespace Scada.Api.Services;

internal sealed class MqttRuntimeMonitor : IMqttRuntimeMonitor
{
    private const int MaxMessages = 200;
    private readonly ConcurrentQueue<MqttRuntimeMessage> _messages = new();
    private readonly ConcurrentDictionary<int, ConcurrentDictionary<string, byte>> _subscriptions = new();
    private readonly ConcurrentDictionary<int, MqttConnectionStats> _stats = new();

    public void RegisterSubscription(int connectionId, string topic)
    {
        if (string.IsNullOrWhiteSpace(topic))
        {
            return;
        }

        var topics = _subscriptions.GetOrAdd(connectionId, _ => new ConcurrentDictionary<string, byte>(StringComparer.OrdinalIgnoreCase));
        topics.TryAdd(topic, 0);
    }

    public void UnregisterSubscription(int connectionId, string topic)
    {
        if (_subscriptions.TryGetValue(connectionId, out var topics))
        {
            topics.TryRemove(topic, out _);
        }
    }

    public void RecordConnected(int connectionId)
    {
        var stats = _stats.GetOrAdd(connectionId, _ => new MqttConnectionStats());
        stats.RecordConnected();
    }

    public void RecordConnectionFailure(int connectionId, string message)
    {
        var stats = _stats.GetOrAdd(connectionId, _ => new MqttConnectionStats());
        stats.RecordFailure(message);
    }

    public MqttRuntimeMessage AddMessage(int connectionId, string topic, string payload, int qos, bool retain)
    {
        var message = new MqttRuntimeMessage(connectionId, topic, payload, qos, retain, DateTime.UtcNow);
        _messages.Enqueue(message);
        _stats.GetOrAdd(connectionId, _ => new MqttConnectionStats()).RecordMessage(message.Timestamp);

        while (_messages.Count > MaxMessages && _messages.TryDequeue(out _))
        {
        }

        return message;
    }

    public IReadOnlyList<MqttRuntimeMessage> GetMessages(int? connectionId = null)
    {
        return _messages
            .Where(message => !connectionId.HasValue || message.ConnectionId == connectionId.Value)
            .OrderBy(message => message.Timestamp)
            .ToList();
    }

    public IReadOnlyList<string> GetSubscribedTopics(int? connectionId = null)
    {
        var topics = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var pair in _subscriptions)
        {
            if (connectionId.HasValue && pair.Key != connectionId.Value)
            {
                continue;
            }

            foreach (var topic in pair.Value.Keys)
            {
                topics.Add(topic);
            }
        }

        return topics.OrderBy(topic => topic).ToList();
    }

    public IReadOnlyList<string> GetDiscoveredTopics(int? connectionId = null)
    {
        return GetMessages(connectionId)
            .Select(message => message.Topic)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(topic => topic)
            .ToList();
    }

    public object? GetLatestValue(string tagAddress, string dataType)
    {
        var topic = GetTagTopic(tagAddress);
        var field = GetJsonField(tagAddress);
        var message = _messages
            .Where(item => string.Equals(item.Topic, topic, StringComparison.Ordinal))
            .OrderByDescending(item => item.Timestamp)
            .FirstOrDefault();

        if (message == null)
        {
            return null;
        }

        return NormalizePayload(message.Payload, dataType, field);
    }

    public object GetDiagnostics(int? connectionId, string broker, int port, string clientId, bool connected)
    {
        var messages = GetMessages(connectionId);
        var topics = GetSubscribedTopics(connectionId);
        var stats = connectionId.HasValue && _stats.TryGetValue(connectionId.Value, out var foundStats)
            ? foundStats.BuildSnapshot()
            : MqttConnectionStatsSnapshot.Empty;
        var values = messages
            .GroupBy(message => message.Topic)
            .ToDictionary(group => group.Key, group => (object?)group.Last().Payload);

        return new
        {
            connection_id = connectionId,
            status = connected ? "CONNECTED" : "DISCONNECTED",
            broker,
            port,
            client_id = clientId,
            uptime = stats.ConnectedSince.HasValue ? Math.Max(0, (DateTime.UtcNow - stats.ConnectedSince.Value).TotalSeconds) : 0,
            message_count = stats.TotalMessages,
            messages_per_second = stats.MessagesPerSecond,
            retry_count = stats.ConnectionFailures,
            subscribed_topics = topics,
            values_cache = values,
            connection_log = stats.ConnectionLog
        };
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

    private sealed class MqttConnectionStats
    {
        private readonly object _sync = new();
        private readonly Queue<DateTime> _recentMessages = new();
        private readonly Queue<MqttConnectionLogEntry> _connectionLog = new();
        private DateTime? _connectedSince;
        private long _totalMessages;
        private int _connectionFailures;

        public void RecordConnected()
        {
            lock (_sync)
            {
                _connectedSince = DateTime.UtcNow;
            }
        }

        public void RecordFailure(string message)
        {
            lock (_sync)
            {
                _connectionFailures++;
                _connectionLog.Enqueue(new MqttConnectionLogEntry(DateTime.UtcNow, "connection_failure", false, message));
                while (_connectionLog.Count > 50)
                {
                    _connectionLog.Dequeue();
                }
            }
        }

        public void RecordMessage(DateTime timestamp)
        {
            lock (_sync)
            {
                _totalMessages++;
                _recentMessages.Enqueue(timestamp);
                TrimRecentMessages(timestamp);
            }
        }

        public MqttConnectionStatsSnapshot BuildSnapshot()
        {
            lock (_sync)
            {
                TrimRecentMessages(DateTime.UtcNow);
                return new MqttConnectionStatsSnapshot(
                    _connectedSince,
                    _totalMessages,
                    Math.Round(_recentMessages.Count / 60d, 2),
                    _connectionFailures,
                    _connectionLog.ToArray());
            }
        }

        private void TrimRecentMessages(DateTime now)
        {
            while (_recentMessages.Count > 0 && now - _recentMessages.Peek() > TimeSpan.FromSeconds(60))
            {
                _recentMessages.Dequeue();
            }
        }
    }

    private sealed record MqttConnectionStatsSnapshot(
        DateTime? ConnectedSince,
        long TotalMessages,
        double MessagesPerSecond,
        int ConnectionFailures,
        IReadOnlyList<MqttConnectionLogEntry> ConnectionLog)
    {
        public static readonly MqttConnectionStatsSnapshot Empty = new(null, 0, 0, 0, Array.Empty<MqttConnectionLogEntry>());
    }

    private sealed record MqttConnectionLogEntry(
        DateTime timestamp,
        string @event,
        bool success,
        string message);
}
