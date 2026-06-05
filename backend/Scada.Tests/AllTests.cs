namespace Scada.Tests;

public class AllTests
{
    public static async Task Main(string[] args)
    {
        Console.WriteLine("=== Plataforma SCADA - Testes de Conexão ===\n");

        Console.WriteLine("Selecione o protocolo para testar:");
        Console.WriteLine("1 - MQTT (básico)");
        Console.WriteLine("2 - MQTT com TLS");
        Console.WriteLine("3 - OPC-UA (básico)");
        Console.WriteLine("4 - OPC-UA com segurança");
        Console.WriteLine("5 - OPC-UA subscrição");
        Console.WriteLine("6 - Modbus TCP");
        Console.WriteLine("7 - Modbus RTU");
        Console.WriteLine("8 - Modbus polling");
        Console.WriteLine("9 - Ethernet/IP básico");
        Console.WriteLine("10 - Ethernet/IP polling");
        Console.WriteLine("11 - Executar todos os testes básicos");
        Console.WriteLine("12 - Regras MES");
        Console.Write("\nOpção: ");

        var option = Console.ReadLine();

        try
        {
            switch (option)
            {
                case "1":
                    await new MqttConnectionTest().TestMqttBasicConnection();
                    break;
                case "2":
                    await new MqttConnectionTest().TestMqttTlsConnection();
                    break;
                case "3":
                    await new OpcUaConnectionTest().TestOpcUaBasicConnection();
                    break;
                case "4":
                    await new OpcUaConnectionTest().TestOpcUaSecureConnection();
                    break;
                case "5":
                    await new OpcUaConnectionTest().TestOpcUaSubscription();
                    break;
                case "6":
                    await new ModbusConnectionTest().TestModbusTcpConnection();
                    break;
                case "7":
                    await new ModbusConnectionTest().TestModbusRtuConnection();
                    break;
                case "8":
                    await new ModbusConnectionTest().TestModbusPolling();
                    break;
                case "9":
                    await new EthernetIpConnectionTest().TestEthernetIpBasicConnection();
                    break;
                case "10":
                    await new EthernetIpConnectionTest().TestEthernetIpPolling();
                    break;
                case "11":
                    await RunAllBasicTests();
                    break;
                case "12":
                    MesEventRulesTests.Run();
                    break;
                default:
                    Console.WriteLine("Opção inválida");
                    break;
            }

            Console.WriteLine("\n=== Teste concluído com sucesso ===");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"\n=== ERRO: {ex.Message} ===");
            Console.WriteLine(ex.StackTrace);
        }

        Console.WriteLine("\nPressione qualquer tecla para sair...");
        Console.ReadKey();
    }

    private static async Task RunAllBasicTests()
    {
        Console.WriteLine("\n=== Executando todos os testes básicos ===\n");

        var tests = new[]
        {
            ("MQTT Básico", new MqttConnectionTest().TestMqttBasicConnection()),
            ("OPC-UA Básico", new OpcUaConnectionTest().TestOpcUaBasicConnection()),
            ("Modbus TCP", new ModbusConnectionTest().TestModbusTcpConnection()),
            ("Ethernet/IP Básico", new EthernetIpConnectionTest().TestEthernetIpBasicConnection())
        };

        foreach (var (name, task) in tests)
        {
            Console.WriteLine($"\n--- Testando {name} ---");
            try
            {
                await task;
                Console.WriteLine($"✓ {name} - PASSOU");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"✗ {name} - FALHOU: {ex.Message}");
            }
        }

        Console.WriteLine("\n=== Todos os testes concluídos ===");
    }
}
