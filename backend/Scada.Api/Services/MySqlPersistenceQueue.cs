using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using MySqlConnector;
using Scada.Core.Models.SQLite;
using Scada.Data.Models;

namespace Scada.Api.Services;

internal sealed class MySqlPersistenceQueue : IMySqlPersistenceQueue
{
    private readonly IServiceScopeFactory _scopeFactory;
    private DateTime? _lastSuccessAt;
    private DateTime? _lastFailureAt;
    private string? _lastError;

    public MySqlPersistenceQueue(IServiceScopeFactory scopeFactory)
    {
        _scopeFactory = scopeFactory;
    }

    public async Task EnqueueAsync(TagValueEnvelope envelope, CancellationToken cancellationToken = default)
    {
        using var scope = _scopeFactory.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<ScadaDbContext>();
        dbContext.PendingMySqlEnvelopes.Add(new PendingMySqlEnvelope
        {
            PayloadJson = JsonSerializer.Serialize(envelope),
            Attempts = 0,
            NextAttemptAt = DateTime.UtcNow,
            CreatedAt = DateTime.UtcNow
        });
        await dbContext.SaveChangesAsync(cancellationToken);
    }

    public async Task<PendingMySqlQueueItem?> GetNextAsync(CancellationToken cancellationToken = default)
    {
        var batch = await GetBatchAsync(1, cancellationToken);
        return batch.FirstOrDefault();
    }

    public async Task<IReadOnlyList<PendingMySqlQueueItem>> GetBatchAsync(int batchSize, CancellationToken cancellationToken = default)
    {
        using var scope = _scopeFactory.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<ScadaDbContext>();
        var now = DateTime.UtcNow;
        var items = await dbContext.PendingMySqlEnvelopes
            .AsNoTracking()
            .Where(entry => entry.ProcessedAt == null && entry.NextAttemptAt <= now)
            .OrderBy(entry => entry.Id)
            .Take(Math.Clamp(batchSize, 1, 500))
            .ToListAsync(cancellationToken);

        var queueItems = new List<PendingMySqlQueueItem>(items.Count);
        foreach (var item in items)
        {
            var envelope = JsonSerializer.Deserialize<TagValueEnvelope>(item.PayloadJson);
            if (envelope is not null)
            {
                queueItems.Add(new PendingMySqlQueueItem(item.Id, envelope, item.Attempts));
            }
        }

        return queueItems;
    }

    public async Task MarkProcessedAsync(long id, CancellationToken cancellationToken = default)
    {
        using var scope = _scopeFactory.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<ScadaDbContext>();
        var item = await dbContext.PendingMySqlEnvelopes.FindAsync([id], cancellationToken);
        if (item is null)
        {
            return;
        }

        item.ProcessedAt = DateTime.UtcNow;
        await dbContext.SaveChangesAsync(cancellationToken);
    }

    public async Task MarkFailedAsync(long id, string error, CancellationToken cancellationToken = default)
    {
        using var scope = _scopeFactory.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<ScadaDbContext>();
        var item = await dbContext.PendingMySqlEnvelopes.FindAsync([id], cancellationToken);
        if (item is null)
        {
            return;
        }

        item.Attempts++;
        item.LastError = error;
        item.NextAttemptAt = DateTime.UtcNow + GetBackoff(item.Attempts);
        await dbContext.SaveChangesAsync(cancellationToken);
    }

    public async Task<int> CleanupProcessedAsync(TimeSpan retention, int batchSize, CancellationToken cancellationToken = default)
    {
        using var scope = _scopeFactory.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<ScadaDbContext>();
        var cutoff = DateTime.UtcNow - retention;
        var ids = await dbContext.PendingMySqlEnvelopes
            .AsNoTracking()
            .Where(item => item.ProcessedAt != null && item.ProcessedAt < cutoff)
            .OrderBy(item => item.Id)
            .Take(Math.Clamp(batchSize, 100, 20_000))
            .Select(item => item.Id)
            .ToListAsync(cancellationToken);

        if (ids.Count == 0)
        {
            return 0;
        }

        return await dbContext.PendingMySqlEnvelopes
            .Where(item => ids.Contains(item.Id))
            .ExecuteDeleteAsync(cancellationToken);
    }

    public async Task<MySqlPersistenceHealthSnapshot> GetHealthAsync(CancellationToken cancellationToken = default)
    {
        using var scope = _scopeFactory.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<ScadaDbContext>();
        var pendingCount = await dbContext.PendingMySqlEnvelopes.CountAsync(item => item.ProcessedAt == null, cancellationToken);
        var failedCount = await dbContext.PendingMySqlEnvelopes.CountAsync(item => item.ProcessedAt == null && item.Attempts > 0, cancellationToken);
        var config = await dbContext.MySqlConfigs
            .AsNoTracking()
            .Where(item => item.IsActive && item.Provider != "SQLServer")
            .OrderByDescending(item => item.IsPrimary)
            .ThenByDescending(item => item.IsLocal)
            .ThenBy(item => item.Id)
            .FirstOrDefaultAsync(cancellationToken);
        var databaseReachable = await CanReachDatabaseAsync(config, cancellationToken);
        var status = !databaseReachable
            ? "offline"
            : pendingCount > 0 && _lastFailureAt.HasValue && (!_lastSuccessAt.HasValue || _lastFailureAt > _lastSuccessAt)
            ? "degraded"
            : "online";

        return new MySqlPersistenceHealthSnapshot(status, databaseReachable, pendingCount, failedCount, _lastSuccessAt, _lastFailureAt, _lastError);
    }

    public void RecordWriteSuccess()
    {
        _lastSuccessAt = DateTime.UtcNow;
    }

    public void RecordWriteFailure(string error)
    {
        _lastFailureAt = DateTime.UtcNow;
        _lastError = error;
    }

    private static TimeSpan GetBackoff(int attempts)
    {
        var seconds = Math.Min(300, Math.Pow(2, Math.Min(attempts, 8)));
        return TimeSpan.FromSeconds(seconds);
    }

    private static async Task<bool> CanReachDatabaseAsync(Scada.Core.Models.SQLite.MySqlConfig? config, CancellationToken cancellationToken)
    {
        if (config is null)
        {
            return false;
        }

        try
        {
            var builder = new MySqlConnectionStringBuilder
            {
                Server = config.Host,
                Port = (uint)config.Port,
                UserID = config.User,
                Password = config.Password,
                Database = config.Database,
                Pooling = true,
                MinimumPoolSize = 0,
                MaximumPoolSize = (uint)Math.Max(config.PoolSize, 1),
                SslMode = MySqlSslMode.None,
                AllowPublicKeyRetrieval = true,
                ConnectionTimeout = 2
            };
            await using var connection = new MySqlConnection(builder.ConnectionString);
            await connection.OpenAsync(cancellationToken);
            await using var command = connection.CreateCommand();
            command.CommandText = "SELECT 1";
            await command.ExecuteScalarAsync(cancellationToken);
            return true;
        }
        catch
        {
            return false;
        }
    }
}
