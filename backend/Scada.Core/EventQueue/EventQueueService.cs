using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging;
using System.Text.Json;
using Scada.Core.Models.SQLite;

namespace Scada.Core.EventQueue;

public class EventQueueService
{
    private readonly string _connectionString;
    private readonly ILogger<EventQueueService> _logger;
    private readonly Dictionary<string, Func<Scada.Core.Models.SQLite.EventQueue, Task>> _eventHandlers;

    public EventQueueService(string connectionString, ILogger<EventQueueService> logger)
    {
        _connectionString = connectionString;
        _logger = logger;
        _eventHandlers = new Dictionary<string, Func<Scada.Core.Models.SQLite.EventQueue, Task>>();
        InitializeDatabase();
    }

    private void InitializeDatabase()
    {
        using var connection = new SqliteConnection(_connectionString);
        connection.Open();

        var command = connection.CreateCommand();
        command.CommandText = @"
            CREATE TABLE IF NOT EXISTS event_queue (
                Id INTEGER PRIMARY KEY AUTOINCREMENT,
                EventType TEXT NOT NULL,
                MachineId TEXT NOT NULL,
                Payload TEXT NOT NULL,
                CreatedAt TEXT NOT NULL,
                ProcessingStartedAt TEXT,
                ProcessingCompletedAt TEXT,
                RetryCount INTEGER DEFAULT 0,
                Status TEXT DEFAULT 'pending'
            );
        ";
        command.ExecuteNonQuery();
    }

    public void RegisterEventHandler(string eventType, Func<Scada.Core.Models.SQLite.EventQueue, Task> handler)
    {
        _eventHandlers[eventType] = handler;
        _logger.LogInformation("Handler registrado para evento: {EventType}", eventType);
    }

    public async Task EnqueueAsync(string eventType, string machineId, object payload)
    {
        using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync();

        var payloadJson = JsonSerializer.Serialize(payload);
        var createdAt = DateTime.UtcNow.ToString("O");

        var command = connection.CreateCommand();
        command.CommandText = @"
            INSERT INTO event_queue (EventType, MachineId, Payload, CreatedAt, Status)
            VALUES (@EventType, @MachineId, @Payload, @CreatedAt, 'pending')
        ";
        command.Parameters.AddWithValue("@EventType", eventType);
        command.Parameters.AddWithValue("@MachineId", machineId);
        command.Parameters.AddWithValue("@Payload", payloadJson);
        command.Parameters.AddWithValue("@CreatedAt", createdAt);

        await command.ExecuteNonQueryAsync();
        _logger.LogInformation("Evento enfileirado: {EventType} para máquina {MachineId}", eventType, machineId);
    }

    public async Task<Scada.Core.Models.SQLite.EventQueue?> DequeueAsync(string eventType)
    {
        using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync();

        var command = connection.CreateCommand();
        command.CommandText = @"
            SELECT * FROM event_queue
            WHERE EventType = @EventType AND Status = 'pending'
            ORDER BY CreatedAt ASC
            LIMIT 1
        ";
        command.Parameters.AddWithValue("@EventType", eventType);

        using var reader = await command.ExecuteReaderAsync();
        if (await reader.ReadAsync())
        {
            return new Scada.Core.Models.SQLite.EventQueue
            {
                Id = reader.GetInt32(0),
                EventType = reader.GetString(1),
                MachineId = reader.GetString(2),
                Payload = reader.GetString(3),
                CreatedAt = DateTime.Parse(reader.GetString(4)),
                ProcessingStartedAt = reader.IsDBNull(5) ? null : DateTime.Parse(reader.GetString(5)),
                ProcessingCompletedAt = reader.IsDBNull(6) ? null : DateTime.Parse(reader.GetString(6)),
                RetryCount = reader.GetInt32(7),
                Status = reader.GetString(8)
            };
        }

        return null;
    }

    public async Task MarkAsProcessingAsync(int eventId)
    {
        using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync();

        var command = connection.CreateCommand();
        command.CommandText = @"
            UPDATE event_queue
            SET Status = 'processing', ProcessingStartedAt = @ProcessingStartedAt
            WHERE Id = @Id
        ";
        command.Parameters.AddWithValue("@Id", eventId);
        command.Parameters.AddWithValue("@ProcessingStartedAt", DateTime.UtcNow.ToString("O"));

        await command.ExecuteNonQueryAsync();
    }

    public async Task MarkAsCompletedAsync(int eventId)
    {
        using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync();

        var command = connection.CreateCommand();
        command.CommandText = @"
            UPDATE event_queue
            SET Status = 'completed', ProcessingCompletedAt = @ProcessingCompletedAt
            WHERE Id = @Id
        ";
        command.Parameters.AddWithValue("@Id", eventId);
        command.Parameters.AddWithValue("@ProcessingCompletedAt", DateTime.UtcNow.ToString("O"));

        await command.ExecuteNonQueryAsync();
    }

    public async Task MarkAsFailedAsync(int eventId)
    {
        using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync();

        var command = connection.CreateCommand();
        command.CommandText = @"
            UPDATE event_queue
            SET Status = 'failed', RetryCount = RetryCount + 1
            WHERE Id = @Id
        ";
        command.Parameters.AddWithValue("@Id", eventId);

        await command.ExecuteNonQueryAsync();
    }

    public async Task ProcessEventsAsync(string eventType, CancellationToken cancellationToken = default)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                var eventItem = await DequeueAsync(eventType);
                if (eventItem != null)
                {
                    await MarkAsProcessingAsync(eventItem.Id);

                    if (_eventHandlers.TryGetValue(eventType, out var handler))
                    {
                        try
                        {
                            await handler(eventItem);
                            await MarkAsCompletedAsync(eventItem.Id);
                        }
                        catch (Exception ex)
                        {
                            _logger.LogError(ex, "Erro ao processar evento {EventId}", eventItem.Id);
                            await MarkAsFailedAsync(eventItem.Id);
                        }
                    }
                    else
                    {
                        _logger.LogWarning("Nenhum handler registrado para evento: {EventType}", eventType);
                    }
                }
                else
                {
                    await Task.Delay(1000, cancellationToken);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Erro no loop de processamento de eventos");
                await Task.Delay(5000, cancellationToken);
            }
        }
    }

    public async Task RecoverStuckEventsAsync(TimeSpan stuckThreshold)
    {
        using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync();

        var threshold = DateTime.UtcNow.Subtract(stuckThreshold).ToString("O");

        var command = connection.CreateCommand();
        command.CommandText = @"
            UPDATE event_queue
            SET Status = 'pending', ProcessingStartedAt = NULL, RetryCount = RetryCount + 1
            WHERE Status = 'processing' AND ProcessingStartedAt < @Threshold
        ";
        command.Parameters.AddWithValue("@Threshold", threshold);

        var affectedRows = await command.ExecuteNonQueryAsync();
        if (affectedRows > 0)
        {
            _logger.LogInformation("{Count} eventos stuck recuperados", affectedRows);
        }
    }
}
