using Scada.Gateway.Interfaces;

namespace Scada.Api.Services;

internal class RuntimeService : IRuntimeService
{
    private readonly ITagRuntimeService _tagRuntimeService;

    public RuntimeService(ITagRuntimeService tagRuntimeService)
    {
        _tagRuntimeService = tagRuntimeService;
    }

    public object GetRuntimeState()
    {
        var states = _tagRuntimeService.GetAllTagStates();
        return states.Values.Select(NormalizeState).ToList();
    }

    public static object NormalizeState(Scada.Data.Models.TagRuntimeState state)
    {
        return new
        {
            tagId = state.TagId,
            tag_id = state.TagId,
            tagName = state.TagName,
            tag_name = state.TagName,
            value = state.Value,
            quality = state.Quality,
            timestamp = state.Timestamp,
            connected = state.Connected
        };
    }
}
