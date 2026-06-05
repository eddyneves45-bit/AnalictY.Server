using Scada.Data.Models;

namespace Scada.Gateway.Interfaces;

public interface ITagRuntimeService
{
    /// <summary>
    /// Registra uma tag no runtime para leitura
    /// </summary>
    Task RegisterTagAsync(int tagId, string tagName, string address, string driverType, string dataType, int pollIntervalMs);
    
    /// <summary>
    /// Remove uma tag do runtime
    /// </summary>
    Task UnregisterTagAsync(int tagId);
    
    /// <summary>
    /// Atualiza o valor de uma tag
    /// </summary>
    void UpdateTagValue(int tagId, object? value, string quality = "GOOD");
    
    /// <summary>
    /// Obtém o estado atual de uma tag
    /// </summary>
    TagRuntimeState? GetTagState(int tagId);
    
    /// <summary>
    /// Obtém o estado de todas as tags
    /// </summary>
    Dictionary<int, TagRuntimeState> GetAllTagStates();
    
    /// <summary>
    /// Atualiza o status de conexão de uma tag
    /// </summary>
    void UpdateTagConnectionStatus(int tagId, bool connected);
    
    /// <summary>
    /// Marca uma tag como STALE (valor antigo)
    /// </summary>
    void MarkTagAsStale(int tagId);
}
