using Microsoft.Extensions.Logging;
using Microsoft.Data.Sqlite;
using System.Text.Json;

namespace Scada.Core.ConnectionContexts;

public class MqttConnectionContext
{
    private readonly string _connectionId;
    private readonly string _connectionString;
    private readonly ILogger<MqttConnectionContext> _logger;
    private readonly Queue<BufferedMessage> _offlineBuffer;
    private readonly Timer _heartbeatTimer;
    private readonly Timer _syncTimer;
    private DateTime _lastHeartbeat;
    private int _consecutiveFailures;
    private ConnectionStatus _status;

    public string ConnectionId => _connectionId;
    public ConnectionStatus Status => _status;

    public MqttConnectionContext(string connectionId, string connectionString, ILogger<MqttConnectionContext> logger)
    {
        _connectionId = connectionId;
        _connectionString = connectionString;
        _logger = logger;
        _offlineBuffer = new Queue<BufferedMessage>();
        _lastHeartbeat = DateTime.UtcNow;
        _consecutiveFailures = 0;
        _status = ConnectionStatus.Offline;

        _heartbeatTimer = new Timer(CheckHeartbeat, null, TimeSpan.FromSeconds(30), TimeSpan.FromSeconds(30));
        _syncTimer = new Timer(SyncBufferedMessages, null, TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(5));

        InitializeDatabase();
    }

    private void InitializeDatabase()
    {
        using var connection = new SqliteConnection(_connectionString);
        connection.Open();

        var command = connection.CreateCommand();
        command.CommandText = @"
            CREATE TABLE IF NOT EXISTS offline_buffer (
                Id INTEGER PRIMARY KEY AUTOINCREMENT,
                ConnectionId TEXT NOT NULL,
                Topic TEXT NOT NULL,
                Payload TEXT NOT NULL,
                Timestamp TEXT NOT NULL,
                IsProcessed INTEGER DEFAULT 0
            );
        ";
        command.ExecuteNonQuery();
    }

    public void SetOnline()
    {
        if (_status == ConnectionStatus.Offline)
        {
            _logger.LogInformation("Conexão {ConnectionId} voltou online", _connectionId);
            LogConnectionResilience(online: true);
        }

        _status = ConnectionStatus.Online;
        _lastHeartbeat = DateTime.UtcNow;
        _consecutiveFailures = 0;
    }

    public void SetOffline()
    {
        if (_status == ConnectionStatus.Online)
        {
            _logger.LogWarning("Conexão {ConnectionId} ficou offline", _connectionId);
            LogConnectionResilience(online: false);
        }

        _status = ConnectionStatus.Offline;
    }

    public void SetDegraded()
    {
        if (_status != ConnectionStatus.Degraded)
        {
            _logger.LogWarning("Conexão {ConnectionId} entrou em modo degradado", _connectionId);
        }

        _status = ConnectionStatus.Degraded;
    }

    public void UpdateHeartbeat()
    {
        _lastHeartbeat = DateTime.UtcNow;
        _consecutiveFailures = 0;

        if (_status == ConnectionStatus.Offline || _status == ConnectionStatus.Degraded)
        {
            SetOnline();
        }
    }

    public void IncrementFailures()
    {
        _consecutiveFailures++;

        if (_consecutiveFailures >= 10)
        {
            SetDegraded();
        }

        if (_consecutiveFailures >= 20)
        {
            SetOffline();
        }
    }

    public async Task BufferMessageAsync(string topic, string payload)
    {
        var message = new BufferedMessage
        {
            Topic = topic,
            Payload = payload,
            Timestamp = DateTime.UtcNow
        };

        _offlineBuffer.Enqueue(message);

        using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync();

        var command = connection.CreateCommand();
        command.CommandText = @"
            INSERT INTO offline_buffer (ConnectionId, Topic, Payload, Timestamp, IsProcessed)
            VALUES (@ConnectionId, @Topic, @Payload, @Timestamp, 0)
        ";
        command.Parameters.AddWithValue("@ConnectionId", _connectionId);
        command.Parameters.AddWithValue("@Topic", topic);
        command.Parameters.AddWithValue("@Payload", payload);
        command.Parameters.AddWithValue("@Timestamp", message.Timestamp.ToString("O"));

        await command.ExecuteNonQueryAsync();
        _logger.LogInformation("Mensagem bufferizada para conexão {ConnectionId}: {Topic}", _connectionId, topic);
    }

    private void CheckHeartbeat(object? state)
    {
        var elapsed = DateTime.UtcNow - _lastHeartbeat;

        if (elapsed > TimeSpan.FromSeconds(60))
        {
            _logger.LogWarning("Heartbeat timeout para conexão {ConnectionId}. Último heartbeat: {Elapsed}s atrás", 
                _connectionId, elapsed.TotalSeconds);
            SetOffline();
        }
    }

    private async void SyncBufferedMessages(object? state)
    {
        if (_status != ConnectionStatus.Online)
        {
            return;
        }

        try
        {
            using var connection = new SqliteConnection(_connectionString);
            await connection.OpenAsync();

            var command = connection.CreateCommand();
            command.CommandText = @"
                SELECT * FROM offline_buffer
                WHERE ConnectionId = @ConnectionId AND IsProcessed = 0
                ORDER BY Timestamp ASC
                LIMIT 100
            ";
            command.Parameters.AddWithValue("@ConnectionId", _connectionId);

            using var reader = await command.ExecuteReaderAsync();
            var messages = new List<BufferedMessage>();

            while (await reader.ReadAsync())
            {
                messages.Add(new BufferedMessage
                {
                    Id = reader.GetInt32(0),
                    Topic = reader.GetString(2),
                    Payload = reader.GetString(3),
                    Timestamp = DateTime.Parse(reader.GetString(4))
                });
            }

            if (messages.Count > 0)
            {
                _logger.LogInformation("Sincronizando {Count} mensagens bufferizadas para conexão {ConnectionId}", 
                    messages.Count, _connectionId);

                // Aqui você enviaria as mensagens para o broker MQTT
                // Por enquanto, apenas marcamos como processadas

                foreach (var message in messages)
                {
                    var updateCommand = connection.CreateCommand();
                    updateCommand.CommandText = @"
                        UPDATE offline_buffer SET IsProcessed = 1 WHERE Id = @Id
                    ";
                    updateCommand.Parameters.AddWithValue("@Id", message.Id);
                    await updateCommand.ExecuteNonQueryAsync();
                }

                _logger.LogInformation("Sincronização concluída para conexão {ConnectionId}", _connectionId);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Erro ao sincronizar mensagens bufferizadas para conexão {ConnectionId}", _connectionId);
        }
    }

    private void LogConnectionResilience(bool online)
    {
        // Implementar log de resiliência no SQLite
    }

    public void Dispose()
    {
        _heartbeatTimer?.Dispose();
        _syncTimer?.Dispose();
    }
}

public enum ConnectionStatus
{
    Online,
    Offline,
    Degraded
}

public class BufferedMessage
{
    public int Id { get; set; }
    public string Topic { get; set; } = string.Empty;
    public string Payload { get; set; } = string.Empty;
    public DateTime Timestamp { get; set; }
}
