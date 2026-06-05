namespace Scada.Api.Services;

internal interface ITagHistoryStore
{
    Task PersistIfChangedAsync(TagValueEnvelope envelope, CancellationToken cancellationToken = default);
    Task<object> QueryAsync(int? tagId, string? tagName, DateTime? from, DateTime? to, int limit, CancellationToken cancellationToken = default);
}
