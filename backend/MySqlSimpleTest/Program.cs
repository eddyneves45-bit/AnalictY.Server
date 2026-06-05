using Microsoft.Extensions.Logging;
using MySqlConnector;

namespace MySqlSimpleTest;

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
        
        var server = Environment.GetEnvironmentVariable("MYSQL_TEST_HOST") ?? "localhost";
        var database = Environment.GetEnvironmentVariable("MYSQL_TEST_DATABASE") ?? "banco_mes_mundial";
        var user = Environment.GetEnvironmentVariable("MYSQL_TEST_USER");
        var password = Environment.GetEnvironmentVariable("MYSQL_TEST_PASSWORD");
        if (string.IsNullOrWhiteSpace(user) || string.IsNullOrWhiteSpace(password))
        {
            throw new InvalidOperationException(
                "Defina MYSQL_TEST_USER e MYSQL_TEST_PASSWORD antes de executar este teste manual.");
        }

        logger.LogInformation("=== Teste de Conexão MySQL ===");
        logger.LogInformation("Server: {Server}", server);
        logger.LogInformation("Database: {Database}", database);
        logger.LogInformation("User: {User}", user);

        var connectionString = new MySqlConnectionStringBuilder
        {
            Server = server,
            Database = database,
            UserID = user,
            Password = password,
            Port = 3306,
            SslMode = MySqlSslMode.None,
            AllowPublicKeyRetrieval = true
        }.ToString();

        try
        {
            logger.LogInformation("Tentando conectar ao MySQL...");
            
            await using var connection = new MySqlConnection(connectionString);
            await connection.OpenAsync();
            
            logger.LogInformation("✅ Conexão estabelecida com sucesso!");
            logger.LogInformation("MySQL Version: {Version}", connection.ServerVersion);
            logger.LogInformation("Database: {Database}", connection.Database);
            
            // Testar uma query simples
            logger.LogInformation("Executando query de teste...");
            var command = connection.CreateCommand();
            command.CommandText = "SELECT VERSION() AS version, NOW() AS server_time";
            
            await using (var reader = await command.ExecuteReaderAsync())
            {
                if (await reader.ReadAsync())
                {
                    logger.LogInformation("MySQL Version: {Version}", reader.GetString("version"));
                    logger.LogInformation("Server Time: {Time}", reader.GetDateTime("server_time"));
                }
            }
            
            await connection.CloseAsync();
            
            logger.LogInformation("");
            logger.LogInformation("=== TESTE MYSQL CONCLUÍDO COM SUCESSO ===");
            logger.LogInformation("");
            logger.LogInformation("Arquitetura:");
            logger.LogInformation("- SQLite: configurações internas (SCADA não depende de MySQL)");
            logger.LogInformation("- MySQL: histórico de eventos de longo prazo");
        }
        catch (MySqlException ex)
        {
            logger.LogError(ex, "Erro de conexão MySQL");
            logger.LogError("Mensagem: {Message}", ex.Message);
            logger.LogError("Erro Number: {ErrorNumber}", ex.Number);
            logger.LogInformation("");
            logger.LogInformation("=== POSSÍVEIS CAUSAS ===");
            logger.LogInformation("1. Credenciais incorretas");
            logger.LogInformation("2. Firewall bloqueando acesso ao RDS");
            logger.LogInformation("3. Database não existe");
            logger.LogInformation("4. SSL/TLS configurado incorretamente");
            logger.LogInformation("5. IP não autorizado no Security Group do RDS");
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Erro no teste MySQL");
            logger.LogError("Mensagem: {Message}", ex.Message);
            logger.LogError("StackTrace: {StackTrace}", ex.StackTrace);
        }
        
        Console.WriteLine("\nPressione qualquer tecla para sair...");
        Console.ReadKey();
    }
}
