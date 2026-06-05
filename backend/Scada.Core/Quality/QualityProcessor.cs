using Microsoft.Extensions.Logging;

namespace Scada.Core.Quality;

public enum DataQuality
{
    Good,
    Bad,
    Stale,
    Uncertain
}

public class QualityProcessor
{
    private readonly ILogger<QualityProcessor> _logger;
    private readonly Dictionary<string, DateTime> _lastHeartbeatTimes;
    private readonly Dictionary<string, int> _uncertainCycles;
    private readonly TimeSpan _heartbeatTimeout = TimeSpan.FromSeconds(15);
    private const int MaxUncertainCycles = 2;

    public QualityProcessor(ILogger<QualityProcessor> logger)
    {
        _logger = logger;
        _lastHeartbeatTimes = new Dictionary<string, DateTime>();
        _uncertainCycles = new Dictionary<string, int>();
    }

    public DataQuality EvaluateQuality(string connectionId, DateTime timestamp)
    {
        var now = DateTime.UtcNow;

        // Atualiza heartbeat
        _lastHeartbeatTimes[connectionId] = timestamp;

        // Verifica se está stale
        if (now - timestamp > _heartbeatTimeout)
        {
            return DataQuality.Stale;
        }

        // Verifica cycles uncertain
        if (_uncertainCycles.TryGetValue(connectionId, out var cycles) && cycles >= MaxUncertainCycles)
        {
            return DataQuality.Bad;
        }

        return DataQuality.Good;
    }

    public void RecordUncertainCycle(string connectionId)
    {
        if (!_uncertainCycles.ContainsKey(connectionId))
        {
            _uncertainCycles[connectionId] = 0;
        }

        _uncertainCycles[connectionId]++;

        if (_uncertainCycles[connectionId] >= MaxUncertainCycles)
        {
            _logger.LogWarning("Conexão {ConnectionId} atingiu limite de cycles uncertain ({MaxCycles})", 
                connectionId, MaxUncertainCycles);
        }
    }

    public void ResetUncertainCycles(string connectionId)
    {
        if (_uncertainCycles.ContainsKey(connectionId))
        {
            _uncertainCycles[connectionId] = 0;
        }
    }

    public ConnectionHealth GetConnectionHealth(string connectionId)
    {
        if (!_lastHeartbeatTimes.TryGetValue(connectionId, out var lastHeartbeat))
        {
            return new ConnectionHealth
            {
                ConnectionId = connectionId,
                Status = "offline",
                LastHeartbeat = null,
                Latency = null
            };
        }

        var elapsed = DateTime.UtcNow - lastHeartbeat;
        var status = elapsed > _heartbeatTimeout ? "offline" : "online";

        return new ConnectionHealth
        {
            ConnectionId = connectionId,
            Status = status,
            LastHeartbeat = lastHeartbeat,
            Latency = elapsed.TotalMilliseconds
        };
    }

    public Dictionary<string, ConnectionHealth> GetAllConnectionHealth()
    {
        var result = new Dictionary<string, ConnectionHealth>();

        foreach (var connectionId in _lastHeartbeatTimes.Keys)
        {
            result[connectionId] = GetConnectionHealth(connectionId);
        }

        return result;
    }
}

public class ConnectionHealth
{
    public string ConnectionId { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty; // online, offline, degraded
    public DateTime? LastHeartbeat { get; set; }
    public double? Latency { get; set; } // em milissegundos
}
