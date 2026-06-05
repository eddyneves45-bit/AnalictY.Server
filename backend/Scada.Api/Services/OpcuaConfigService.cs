using Microsoft.EntityFrameworkCore;
using Scada.Core.Models.SQLite;
using Scada.Data.Models;

namespace Scada.Api.Services;

internal class OpcuaConfigService : IOpcuaConfigService
{
    private readonly ScadaDbContext _dbContext;
    private readonly IOpcuaSessionFactory _sessionFactory;

    public OpcuaConfigService(ScadaDbContext dbContext, IOpcuaSessionFactory sessionFactory)
    {
        _dbContext = dbContext;
        _sessionFactory = sessionFactory;
    }

    public async Task<object> GetConfigsAsync(CancellationToken cancellationToken = default)
    {
        return await _dbContext.OpcuaConfigs.OrderByDescending(c => c.Id).ToListAsync(cancellationToken);
    }

    public async Task<ApplicationServiceResult> UpsertConfigAsync(OpcuaConfigRequest request, CancellationToken cancellationToken = default)
    {
        if (!request.server_url.StartsWith("opc.tcp://", StringComparison.OrdinalIgnoreCase))
        {
            return ApplicationServiceResult.BadRequest(new { error = "A URL do OPC UA deve comecar com opc.tcp://" });
        }

        var usesSecurity = !string.Equals(request.security_policy, "None", StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(request.security_mode, "None", StringComparison.OrdinalIgnoreCase);
        var hasCertificate = !string.IsNullOrWhiteSpace(request.certificate_path);
        var hasPrivateKey = !string.IsNullOrWhiteSpace(request.private_key_path);
        if (hasPrivateKey && !hasCertificate)
        {
            return ApplicationServiceResult.BadRequest(new { error = "Informe o certificado OPC UA quando houver chave privada." });
        }

        if (hasCertificate && !Path.GetExtension(request.certificate_path).Equals(".pfx", StringComparison.OrdinalIgnoreCase) && !hasPrivateKey)
        {
            return ApplicationServiceResult.BadRequest(new { error = "Certificados PEM/CRT exigem chave privada." });
        }

        if (!usesSecurity && (hasCertificate || hasPrivateKey))
        {
            return ApplicationServiceResult.BadRequest(new { error = "Use Security Policy/Mode diferente de None para informar certificado OPC UA." });
        }

        var config = request.id.HasValue
            ? await _dbContext.OpcuaConfigs.FindAsync(new object[] { request.id.Value }, cancellationToken)
            : null;

        if (config == null)
        {
            config = new OpcuaConfig { CreatedAt = DateTime.UtcNow };
            _dbContext.OpcuaConfigs.Add(config);
        }

        config.Name = request.name;
        config.ServerUrl = request.server_url;
        config.SecurityPolicy = request.security_policy;
        config.SecurityMode = request.security_mode;
        config.Username = request.username;
        config.Password = request.password;
        config.CertificatePath = request.certificate_path ?? "";
        config.PrivateKeyPath = request.private_key_path ?? "";
        config.UpdateInterval = request.update_interval;
        config.IsActive = request.is_active;
        config.UpdatedAt = DateTime.UtcNow;

        await _dbContext.SaveChangesAsync(cancellationToken);
        return ApplicationServiceResult.Ok(config);
    }

    public async Task<ApplicationServiceResult> DeleteConfigAsync(int id, CancellationToken cancellationToken = default)
    {
        var config = await _dbContext.OpcuaConfigs.FindAsync(new object[] { id }, cancellationToken);
        if (config == null)
        {
            return ApplicationServiceResult.NotFound();
        }

        _dbContext.OpcuaConfigs.Remove(config);
        await _dbContext.SaveChangesAsync(cancellationToken);
        return ApplicationServiceResult.Ok(new { message = "Configuracao OPC UA excluida" });
    }

    public async Task<ApplicationServiceResult> BrowseAsync(string? nodeId, int? connectionId, CancellationToken cancellationToken = default)
    {
        var config = connectionId.HasValue
            ? await _dbContext.OpcuaConfigs.FirstOrDefaultAsync(c => c.Id == connectionId.Value, cancellationToken)
            : await _dbContext.OpcuaConfigs.FirstOrDefaultAsync(c => c.IsActive, cancellationToken);
        if (config == null)
        {
            return ApplicationServiceResult.NotFound(new { message = "Nenhuma configuracao OPC UA encontrada" });
        }

        try
        {
            using var session = await _sessionFactory.CreateSessionAsync(config, cancellationToken);
            var nodeIdObj = string.IsNullOrEmpty(nodeId)
                ? Opc.Ua.ObjectIds.ObjectsFolder
                : Opc.Ua.NodeId.Parse(nodeId);

            var browseResult = new List<object>();
            var browseDescription = new Opc.Ua.BrowseDescription
            {
                NodeId = nodeIdObj,
                BrowseDirection = Opc.Ua.BrowseDirection.Forward,
                ReferenceTypeId = Opc.Ua.ReferenceTypeIds.HierarchicalReferences,
                IncludeSubtypes = true,
                NodeClassMask = (uint)Opc.Ua.NodeClass.Object | (uint)Opc.Ua.NodeClass.Variable | (uint)Opc.Ua.NodeClass.Method | (uint)Opc.Ua.NodeClass.ObjectType | (uint)Opc.Ua.NodeClass.VariableType | (uint)Opc.Ua.NodeClass.ReferenceType | (uint)Opc.Ua.NodeClass.DataType,
                ResultMask = (uint)Opc.Ua.BrowseResultMask.All
            };

            session.Browse(null, null, 0, new Opc.Ua.BrowseDescriptionCollection { browseDescription }, out var results, out _);
            foreach (var reference in results[0].References)
            {
                try
                {
                    var referenceNodeId = (Opc.Ua.NodeId)reference.NodeId;
                    var node = session.ReadNode(referenceNodeId);
                    var hasChildrenDescription = new Opc.Ua.BrowseDescription
                    {
                        NodeId = referenceNodeId,
                        BrowseDirection = Opc.Ua.BrowseDirection.Forward,
                        ReferenceTypeId = Opc.Ua.ReferenceTypeIds.HierarchicalReferences,
                        IncludeSubtypes = true,
                        NodeClassMask = (uint)Opc.Ua.NodeClass.Object | (uint)Opc.Ua.NodeClass.Variable,
                        ResultMask = (uint)Opc.Ua.BrowseResultMask.None
                    };

                    session.Browse(null, null, 0, new Opc.Ua.BrowseDescriptionCollection { hasChildrenDescription }, out var hasChildrenResults, out _);
                    browseResult.Add(new
                    {
                        node_id = referenceNodeId.ToString(),
                        browse_name = reference.DisplayName.Text,
                        display_name = reference.DisplayName.Text,
                        node_class = node.NodeClass.ToString(),
                        has_children = hasChildrenResults[0].References.Count > 0
                    });
                }
                catch
                {
                    continue;
                }
            }

            return ApplicationServiceResult.Ok(new { nodes = browseResult });
        }
        catch (Exception ex)
        {
            return ApplicationServiceResult.BadRequest(new { message = $"Erro ao conectar ao servidor OPC UA: {ex.Message}" });
        }
    }
}
