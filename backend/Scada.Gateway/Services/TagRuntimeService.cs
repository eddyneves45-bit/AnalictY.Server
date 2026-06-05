using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;
using Scada.Data.Models;
using Scada.Gateway.Interfaces;

namespace Scada.Gateway.Services;

public class TagRuntimeService : ITagRuntimeService
{
    private readonly ConcurrentDictionary<int, TagRuntimeState> _tagStates = new();
    private readonly ILogger<TagRuntimeService> _logger;

    public TagRuntimeService(ILogger<TagRuntimeService> logger)
    {
        _logger = logger;
    }

    public Task RegisterTagAsync(int tagId, string tagName, string address, string driverType, string dataType, int pollIntervalMs)
    {
        var state = new TagRuntimeState
        {
            TagId = tagId,
            TagName = tagName,
            Quality = "UNKNOWN",
            Timestamp = DateTime.UtcNow,
            Connected = false,
            Value = null
        };

        _tagStates.AddOrUpdate(tagId, state, (_, existing) =>
        {
            existing.TagName = tagName;
            existing.Timestamp = DateTime.UtcNow;
            return existing;
        });
        _logger.LogInformation("Tag registrada no runtime: {TagName} (ID: {TagId}, Driver: {DriverType})", tagName, tagId, driverType);

        return Task.CompletedTask;
    }

    public Task UnregisterTagAsync(int tagId)
    {
        if (_tagStates.TryRemove(tagId, out var state))
        {
            _logger.LogInformation("Tag removida do runtime: {TagName} (ID: {TagId})", state.TagName, tagId);
        }

        return Task.CompletedTask;
    }

    public void UpdateTagValue(int tagId, object? value, string quality = "GOOD")
    {
        if (_tagStates.TryGetValue(tagId, out var state))
        {
            state.Value = value;
            state.Quality = quality;
            state.Timestamp = DateTime.UtcNow;
            
            _logger.LogDebug("Tag atualizada: {TagName} = {Value} (Quality: {Quality})", state.TagName, value, quality);
        }
    }

    public TagRuntimeState? GetTagState(int tagId)
    {
        return _tagStates.TryGetValue(tagId, out var state) ? state : null;
    }

    public Dictionary<int, TagRuntimeState> GetAllTagStates()
    {
        return _tagStates.ToDictionary(kvp => kvp.Key, kvp => kvp.Value);
    }

    public void UpdateTagConnectionStatus(int tagId, bool connected)
    {
        if (_tagStates.TryGetValue(tagId, out var state))
        {
            state.Connected = connected;
            state.Quality = connected ? "GOOD" : "DISCONNECTED";
            state.Timestamp = DateTime.UtcNow;
            
            _logger.LogDebug("Tag connection status atualizado: {TagName} - {Connected}", state.TagName, connected);
        }
    }

    public void MarkTagAsStale(int tagId)
    {
        if (_tagStates.TryGetValue(tagId, out var state))
        {
            state.Quality = "STALE";
            state.Timestamp = DateTime.UtcNow;
            
            _logger.LogDebug("Tag marcada como STALE: {TagName}", state.TagName);
        }
    }
}
