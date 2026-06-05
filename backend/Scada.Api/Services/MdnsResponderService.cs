using System.Buffers.Binary;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Text;

namespace Scada.Api.Services;

public sealed class MdnsResponderService : BackgroundService
{
    private const int MdnsPort = 5353;
    private static readonly IPAddress MdnsAddress = IPAddress.Parse("224.0.0.251");
    private readonly ILogger<MdnsResponderService> _logger;
    private readonly string _hostName;
    private UdpClient? _client;

    public MdnsResponderService(IConfiguration configuration, ILogger<MdnsResponderService> logger)
    {
        _logger = logger;
        _hostName = NormalizeHostName(configuration["AnalictY:MdnsHostName"] ?? "analicty.local");
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!OperatingSystem.IsWindows())
        {
            _logger.LogInformation("mDNS habilitado para {HostName}.", _hostName);
        }

        try
        {
            _client = CreateClient();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Nao foi possivel iniciar mDNS em UDP {Port}. O acesso por {HostName} pode depender do Bonjour/Windows.", MdnsPort, _hostName);
            return;
        }

        _logger.LogInformation("mDNS anunciando {HostName} em UDP {Port}.", _hostName, MdnsPort);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var result = await _client.ReceiveAsync(stoppingToken);
                var question = TryReadQuestion(result.Buffer);
                if (question is null || !IsOurQuestion(question.Value.Name, question.Value.Type))
                {
                    continue;
                }

                var addresses = GetLocalIPv4Addresses();
                if (addresses.Count == 0)
                {
                    continue;
                }

                var response = BuildAResponse(addresses);
                await _client.SendAsync(response, response.Length, new IPEndPoint(MdnsAddress, MdnsPort));
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "Falha ao responder consulta mDNS.");
            }
        }
    }

    public override Task StopAsync(CancellationToken cancellationToken)
    {
        _client?.Dispose();
        return base.StopAsync(cancellationToken);
    }

    private static UdpClient CreateClient()
    {
        var client = new UdpClient(AddressFamily.InterNetwork);
        client.ExclusiveAddressUse = false;
        client.Client.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
        client.Client.Bind(new IPEndPoint(IPAddress.Any, MdnsPort));
        client.JoinMulticastGroup(MdnsAddress);
        client.MulticastLoopback = false;
        return client;
    }

    private bool IsOurQuestion(string name, ushort type)
    {
        return string.Equals(name, _hostName, StringComparison.OrdinalIgnoreCase) &&
            (type == 1 || type == 255);
    }

    private static string NormalizeHostName(string value)
    {
        var hostName = value.Trim().TrimEnd('.');
        return string.IsNullOrWhiteSpace(hostName) ? "analicty.local" : hostName;
    }

    private static (string Name, ushort Type)? TryReadQuestion(byte[] packet)
    {
        if (packet.Length < 12)
        {
            return null;
        }

        var questionCount = BinaryPrimitives.ReadUInt16BigEndian(packet.AsSpan(4, 2));
        if (questionCount == 0)
        {
            return null;
        }

        var offset = 12;
        var name = ReadName(packet, ref offset);
        if (string.IsNullOrWhiteSpace(name) || offset + 4 > packet.Length)
        {
            return null;
        }

        var type = BinaryPrimitives.ReadUInt16BigEndian(packet.AsSpan(offset, 2));
        return (name, type);
    }

    private static string ReadName(byte[] packet, ref int offset)
    {
        var labels = new List<string>();
        var jumps = 0;
        var current = offset;

        while (current < packet.Length && jumps < 8)
        {
            var length = packet[current++];
            if (length == 0)
            {
                offset = current;
                break;
            }

            if ((length & 0xC0) == 0xC0)
            {
                if (current >= packet.Length)
                {
                    return string.Empty;
                }

                var pointer = ((length & 0x3F) << 8) | packet[current++];
                offset = current;
                current = pointer;
                jumps++;
                continue;
            }

            if (current + length > packet.Length)
            {
                return string.Empty;
            }

            labels.Add(Encoding.ASCII.GetString(packet, current, length));
            current += length;
        }

        return string.Join('.', labels);
    }

    private byte[] BuildAResponse(IReadOnlyCollection<IPAddress> addresses)
    {
        using var stream = new MemoryStream();
        WriteUInt16(stream, 0);
        WriteUInt16(stream, 0x8400);
        WriteUInt16(stream, 0);
        WriteUInt16(stream, (ushort)addresses.Count);
        WriteUInt16(stream, 0);
        WriteUInt16(stream, 0);

        foreach (var address in addresses)
        {
            WriteName(stream, _hostName);
            WriteUInt16(stream, 1);
            WriteUInt16(stream, 0x8001);
            WriteUInt32(stream, 120);
            WriteUInt16(stream, 4);
            stream.Write(address.GetAddressBytes(), 0, 4);
        }

        return stream.ToArray();
    }

    private static List<IPAddress> GetLocalIPv4Addresses()
    {
        return NetworkInterface.GetAllNetworkInterfaces()
            .Where(adapter =>
                adapter.OperationalStatus == OperationalStatus.Up &&
                adapter.NetworkInterfaceType != NetworkInterfaceType.Loopback &&
                adapter.NetworkInterfaceType != NetworkInterfaceType.Tunnel)
            .SelectMany(adapter => adapter.GetIPProperties().UnicastAddresses)
            .Where(address =>
                address.Address.AddressFamily == AddressFamily.InterNetwork &&
                !IPAddress.IsLoopback(address.Address) &&
                !address.Address.ToString().StartsWith("169.254.", StringComparison.Ordinal))
            .Select(address => address.Address)
            .Distinct()
            .ToList();
    }

    private static void WriteName(Stream stream, string name)
    {
        foreach (var label in name.Split('.', StringSplitOptions.RemoveEmptyEntries))
        {
            var bytes = Encoding.ASCII.GetBytes(label);
            stream.WriteByte((byte)bytes.Length);
            stream.Write(bytes, 0, bytes.Length);
        }

        stream.WriteByte(0);
    }

    private static void WriteUInt16(Stream stream, ushort value)
    {
        Span<byte> buffer = stackalloc byte[2];
        BinaryPrimitives.WriteUInt16BigEndian(buffer, value);
        stream.Write(buffer);
    }

    private static void WriteUInt32(Stream stream, uint value)
    {
        Span<byte> buffer = stackalloc byte[4];
        BinaryPrimitives.WriteUInt32BigEndian(buffer, value);
        stream.Write(buffer);
    }
}
