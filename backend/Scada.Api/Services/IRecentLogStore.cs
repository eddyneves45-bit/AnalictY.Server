namespace Scada.Api.Services;

internal interface IRecentLogStore
{
    void Add(RecentLogEntry entry);
    IReadOnlyList<RecentLogEntry> GetRecent(int take, string? level = null, string? search = null);
}
