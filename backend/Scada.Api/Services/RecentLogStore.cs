using System.Collections.Concurrent;

namespace Scada.Api.Services;

internal sealed class RecentLogStore : IRecentLogStore
{
    private const int MaxEntries = 500;
    private readonly ConcurrentQueue<RecentLogEntry> _entries = new();

    public void Add(RecentLogEntry entry)
    {
        _entries.Enqueue(entry);
        while (_entries.Count > MaxEntries && _entries.TryDequeue(out _))
        {
        }
    }

    public IReadOnlyList<RecentLogEntry> GetRecent(int take, string? level = null, string? search = null)
    {
        var normalizedTake = Math.Clamp(take, 1, 500);
        IEnumerable<RecentLogEntry> query = _entries.ToArray().Reverse();

        if (!string.IsNullOrWhiteSpace(level))
        {
            query = query.Where(entry => string.Equals(entry.Level, level, StringComparison.OrdinalIgnoreCase));
        }

        if (!string.IsNullOrWhiteSpace(search))
        {
            query = query.Where(entry =>
                entry.Message.Contains(search, StringComparison.OrdinalIgnoreCase)
                || entry.Category.Contains(search, StringComparison.OrdinalIgnoreCase)
                || (entry.Exception?.Contains(search, StringComparison.OrdinalIgnoreCase) ?? false));
        }

        return query.Take(normalizedTake).ToList();
    }
}
