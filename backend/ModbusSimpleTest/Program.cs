using Microsoft.Extensions.Logging;
using System.Net.Sockets;

namespace ModbusSimpleTest;

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
        
        logger.LogInformation("=== Teste de Conectividade Modbus TCP ===");
        logger.LogInformation("Endpoint: 127.0.0.1:502");

        var host = "127.0.0.1";
        var port = 502;

        try
        {
            logger.LogInformation("Testando conexão TCP com {Host}:{Port}...", host, port);
            
            using var tcpClient = new TcpClient();
            
            // Tentar conectar com timeout
            var connectTask = tcpClient.ConnectAsync(host, port);
            var timeoutTask = Task.Delay(5000);
            
            await connectTask; // Aguardar a conexão completa
            
            if (!tcpClient.Connected)
            {
                logger.LogError("❌ Falha ao conectar. Nenhum servidor Modbus detectado.");
                logger.LogInformation("");
                logger.LogInformation("=== PARA TESTAR MODBUS, VOCÊ PRECISA ===");
                logger.LogInformation("1. Instalar um simulador Modbus:");
                logger.LogInformation("   - ModbusSim (Windows, gratuito)");
                logger.LogInformation("   - QModMaster (Windows, gratuito)");
                logger.LogInformation("   - ModbusPal (Windows, gratuito)");
                logger.LogInformation("");
                logger.LogInformation("2. Configurar o simulador:");
                logger.LogInformation("   - Host/IP: {Host}", host);
                logger.LogInformation("   - Porta: {Port}", port);
                logger.LogInformation("   - Slave ID: 1");
                logger.LogInformation("   - Adicionar alguns Holding Registers para teste");
                logger.LogInformation("");
                logger.LogInformation("3. Execute o simulador antes deste teste");
                return;
            }
            
            logger.LogInformation("✅ Conexão TCP estabelecida com sucesso!");
            logger.LogInformation("Um servidor está rodando em {Host}:{Port}", host, port);
            logger.LogInformation("");
            logger.LogInformation("=== TESTE DE CONECTIVIDADE MODBUS CONCLUÍDO ===");
            logger.LogInformation("Para testes avançados (leitura/escrita de registros), use o driver Modbus:");
            logger.LogInformation("backend/Scada.Drivers/Modbus/ModbusDriver.cs");
            
            tcpClient.Close();
        }
        catch (SocketException ex) when (ex.SocketErrorCode == SocketError.ConnectionRefused)
        {
            logger.LogError("❌ Conexão recusada. Nenhum servidor Modbus rodando em {Host}:{Port}", host, port);
            logger.LogInformation("");
            logger.LogInformation("=== COMO RESOLVER ===");
            logger.LogInformation("1. Instale um simulador Modbus (ModbusSim, QModMaster, etc)");
            logger.LogInformation("2. Configure para escutar em {Host}:{Port}", host, port);
            logger.LogInformation("3. Execute o simulador antes deste teste");
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Erro no teste de conectividade");
            logger.LogError("Mensagem: {Message}", ex.Message);
        }
        
        Console.WriteLine("\nPressione qualquer tecla para sair...");
        Console.ReadKey();
    }
    
    static byte[] CalculateCRC(byte[] data)
    {
        ushort crc = 0xFFFF;
        for (int i = 0; i < data.Length; i++)
        {
            crc ^= data[i];
            for (int j = 0; j < 8; j++)
            {
                bool lsb = (crc & 0x0001) != 0;
                crc >>= 1;
                if (lsb)
                    crc ^= 0xA001;
            }
        }
        return BitConverter.GetBytes(crc);
    }
}
