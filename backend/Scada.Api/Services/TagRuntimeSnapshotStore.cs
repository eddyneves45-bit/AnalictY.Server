using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Scada.Core.Models.SQLite;
using Scada.Data.Models;

namespace Scada.Api.Services;

internal sealed class TagRuntimeSnapshotStore : ITagRuntimeSnapshotStore
{
    private readonly IServiceScopeFactory _scopeFactory;

    public TagRuntimeSnapshotStore(IServiceScopeFactory scopeFactory)
    {
        _scopeFactory = scopeFactory;
    }

    public async Task PersistAsync(TagValueEnvelope envelope, CancellationToken cancellationToken = default)
    {
        using var scope = _scopeFactory.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<ScadaDbContext>();
        var snapshot = await dbContext.TagRuntimeSnapshots.FindAsync(new object[] { envelope.TagId }, cancellationToken);

        if (snapshot == null)
        {
            snapshot = new TagRuntimeSnapshot { TagId = envelope.TagId };
            dbContext.TagRuntimeSnapshots.Add(snapshot);
        }

        snapshot.ValueJson = JsonSerializer.Serialize(envelope.Value);
        snapshot.Quality = envelope.Quality;
        snapshot.SourceTimestamp = envelope.SourceTimestamp;
        snapshot.LastPersistedAt = DateTime.UtcNow;
        await dbContext.SaveChangesAsync(cancellationToken);
    }

    public async Task<IReadOnlyDictionary<int, RestoredTagRuntimeSnapshot>> LoadAsync(CancellationToken cancellationToken = default)
    {
        using var scope = _scopeFactory.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<ScadaDbContext>();
        var snapshots = await dbContext.TagRuntimeSnapshots
            .AsNoTracking()
            .ToListAsync(cancellationToken);

        return snapshots.ToDictionary(
            item => item.TagId,
            item => new RestoredTagRuntimeSnapshot(
                item.TagId,
                DeserializeValue(item.ValueJson),
                item.SourceTimestamp));
    }

    private static object? DeserializeValue(string valueJson)
    {
        using var document = JsonDocument.Parse(valueJson);
        var root = document.RootElement;
        return root.ValueKind switch
        {
            JsonValueKind.Null => null,
            JsonValueKind.True or JsonValueKind.False => root.GetBoolean(),
            JsonValueKind.Number when root.TryGetInt64(out var longValue) => longValue,
            JsonValueKind.Number => root.GetDouble(),
            JsonValueKind.String => root.GetString(),
            _ => root.Clone()
        };
    }
}
