using Microsoft.Extensions.Logging;
using Opc.Ua;
using Opc.Ua.Client;

namespace OpcUaSimpleTest;

class Program
{
    static async Task Main(string[] args)
    {
        var loggerFactory = LoggerFactory.Create(builder =>
        {
            builder.AddConsole();
            builder.SetMinimumLevel(LogLevel.Information);
        });

        var logger = loggerFactory.CreateLogger<Program>();
        
        logger.LogInformation("=== Teste OPC-UA Simples ===");
        logger.LogInformation("Endpoint: opc.tcp://DESKTOP-EDDY:4840/G01");

        var endpointUrl = "opc.tcp://DESKTOP-EDDY:4840/G01";
        
        try
        {
            // Configurar aplicação
            var config = new ApplicationConfiguration()
            {
                ApplicationName = "SCADA OPC-UA Test",
                ApplicationType = ApplicationType.Client,
                SecurityConfiguration = new SecurityConfiguration
                {
                    ApplicationCertificate = new CertificateIdentifier
                    {
                        StoreType = CertificateStoreType.Directory,
                        StorePath = Path.Combine(Environment.CurrentDirectory, "pki", "own"),
                        SubjectName = "SCADA OPC-UA Test"
                    },
                    TrustedPeerCertificates = new CertificateTrustList(),
                    TrustedIssuerCertificates = new CertificateTrustList()
                },
                ClientConfiguration = new ClientConfiguration
                {
                    DefaultSessionTimeout = 60000
                }
            };
            
            // Criar sessão usando método simplificado
            logger.LogInformation("Criando sessão...");
            var endpoint = CoreClientUtils.SelectEndpoint(endpointUrl, false, 10000);
            
            var session = await Session.Create(config, new ConfiguredEndpoint(null, endpoint, EndpointConfiguration.Create()), 
                false, "SCADA OPC-UA Test", 60000, new UserIdentity(new AnonymousIdentityToken()), null);
            
            logger.LogInformation("Conectado com sucesso! SessionId: {SessionId}", session.SessionId);
            
            // Ler alguns nós básicos do servidor
            logger.LogInformation("\nLendo nós básicos do servidor OPC-UA...");
            
            var nodesToTest = new[]
            {
                ObjectIds.ObjectsFolder,
                ObjectTypes.BaseObjectType,
                VariableTypeIds.BaseVariableType
            };
            
            foreach (var nodeId in nodesToTest)
            {
                try
                {
                    var node = session.ReadNode(nodeId);
                    logger.LogInformation("Nó {NodeId}: {DisplayName} - {NodeClass}", 
                        nodeId, node.DisplayName.Text, node.NodeClass);
                }
                catch (Exception ex)
                {
                    logger.LogWarning("Erro ao ler nó {NodeId}: {Message}", nodeId, ex.Message);
                }
            }
            
            // Tentar ler CurrentTime do servidor
            try
            {
                var currentTimeNodeId = new NodeId(2258); // ServerStatus.CurrentTime
                var currentTime = session.ReadValue(currentTimeNodeId);
                logger.LogInformation("\nServer CurrentTime: {Value}", currentTime.WrappedValue.Value);
            }
            catch (Exception ex)
            {
                logger.LogWarning("Não foi possível ler CurrentTime: {Message}", ex.Message);
            }
            
            // Desconectar
            logger.LogInformation("Desconectando...");
            session.Close();
            logger.LogInformation("Teste concluído com sucesso!");
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Erro no teste OPC-UA");
            logger.LogError("Mensagem: {Message}", ex.Message);
            logger.LogError("StackTrace: {StackTrace}", ex.StackTrace);
        }
        
        Console.WriteLine("\nPressione qualquer tecla para sair...");
        Console.ReadKey();
    }
}
