namespace Scada.Security.Models;

public class TenantContext
{
    public string TenantId { get; set; } = string.Empty;
    public string? SiteId { get; set; }
    public string TenantName { get; set; } = string.Empty;
    public Dictionary<string, string> TenantMetadata { get; set; } = new();
}
