using System.Net;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Scada.Api.Services;
using Scada.Core.Models.SQLite;
using Scada.Data.Models;

public static class FtpExportEndpoints
{
    private const string ConfigKey = "FtpExport:Config";

    public static WebApplication MapFtpExportEndpoints(this WebApplication app)
    {
        app.MapGet("/api/config/ftp-export", async (ScadaDbContext dbContext, CancellationToken cancellationToken) =>
        {
            var config = await GetConfigAsync(dbContext, cancellationToken);
            return Results.Ok(ToPublicConfig(config));
        })
        .WithName("GetFtpExportConfig");

        app.MapPut("/api/config/ftp-export", async (
            HttpContext context,
            ScadaDbContext dbContext,
            CancellationToken cancellationToken) =>
        {
            var request = await context.Request.ReadFromJsonAsync<FtpExportConfigRequest>(cancellationToken);
            if (request == null) return Results.BadRequest(new { message = "Payload inválido." });

            var current = await GetConfigAsync(dbContext, cancellationToken);
            var config = NormalizeRequest(request, current);
            var validation = ValidateConfig(config);
            if (validation != null) return validation;

            await SaveConfigAsync(dbContext, config, cancellationToken);
            return Results.Ok(ToPublicConfig(config));
        })
        .WithName("SaveFtpExportConfig");

        app.MapPost("/api/config/ftp-export/test", async (
            ScadaDbContext dbContext,
            CancellationToken cancellationToken) =>
        {
            var config = await GetConfigAsync(dbContext, cancellationToken);
            var validation = ValidateConfig(config);
            if (validation != null) return validation;

            return await TestConnectionAsync(config, cancellationToken);
        })
        .WithName("TestFtpExportConfig");

        app.MapPost("/api/config/ftp-export/test-request", async (
            HttpContext context,
            ScadaDbContext dbContext,
            CancellationToken cancellationToken) =>
        {
            var request = await context.Request.ReadFromJsonAsync<FtpExportConfigRequest>(cancellationToken);
            if (request == null) return Results.BadRequest(new { message = "Payload inválido." });

            var current = await GetConfigAsync(dbContext, cancellationToken);
            var config = NormalizeRequest(request, current);
            var validation = ValidateConfig(config);
            if (validation != null) return validation;

            return await TestConnectionAsync(config, cancellationToken);
        })
        .WithName("TestFtpExportRequest");

        app.MapPost("/api/config/ftp-export/send-now", async (
            ScadaDbContext dbContext,
            CancellationToken cancellationToken) =>
        {
            var config = await GetConfigAsync(dbContext, cancellationToken);
            var validation = ValidateConfig(config);
            if (validation != null) return validation;

            var fileName = $"analicty-export-{DateTime.UtcNow:yyyyMMdd-HHmmss}.{config.file_format.ToLowerInvariant()}";
            var content = BuildSampleExport(config);
            var result = await UploadFileAsync(config, fileName, content, cancellationToken);

            if (result.success)
            {
                config.last_sent_at = DateTime.UtcNow;
                config.last_error = "";
                await SaveConfigAsync(dbContext, config, cancellationToken);
                return Results.Ok(new
                {
                    success = true,
                    message = "Arquivo enviado com sucesso.",
                    file_name = fileName,
                    config.last_sent_at
                });
            }

            config.last_error = result.message;
            await SaveConfigAsync(dbContext, config, cancellationToken);
            return Results.BadRequest(new { success = false, message = result.message });
        })
        .WithName("SendFtpExportNow");

        app.MapPost("/api/config/ftp-export/send-report", async (
            FtpReportExportRequest request,
            ScadaDbContext dbContext,
            IReportService reportService,
            CancellationToken cancellationToken) =>
        {
            var config = await GetConfigAsync(dbContext, cancellationToken);
            var validation = ValidateConfig(config);
            if (validation != null) return validation;

            if (!config.enabled)
            {
                return Results.BadRequest(new { success = false, message = "A conexão FTP/SFTP está inativa." });
            }

            var format = NormalizeReportFormat(request.formato);
            if (format == null) return Results.BadRequest(new { success = false, message = "Formato inválido. Use CSV, XML ou PDF." });

            var reportRequest = new ReportGenerateRequest(
                request.report_type,
                request.machine_id,
                request.inicio_em,
                request.fim_em,
                "csv",
                request.incluir_motivos_parada);

            var csv = string.Equals(request.report_type, "production", StringComparison.OrdinalIgnoreCase)
                ? await reportService.ExportProductionCsvAsync(reportRequest, cancellationToken)
                : await reportService.ExportCsvAsync(reportRequest, cancellationToken);

            var machineFolder = SanitizeFilePart(request.machine_code ?? request.machine_id ?? "todas");
            var dateFolder = DateTime.UtcNow.ToString("yyyyMMdd");
            var destinationPath = string.IsNullOrWhiteSpace(request.destination_path)
                ? $"{NormalizePath(config.destination_path).TrimEnd('/')}/{machineFolder}/{dateFolder}"
                : NormalizePath(request.destination_path);
            var fileName = $"relatorio-{SanitizeFilePart(request.report_type)}-{machineFolder}-{DateTime.UtcNow:yyyyMMdd-HHmmss}.{format}";
            var content = BuildReportFile(csv, format, request.report_type, request.machine_code ?? request.machine_id ?? "todas");

            var result = await UploadFileAsync(config, fileName, content, cancellationToken, destinationPath);
            if (result.success)
            {
                config.last_sent_at = DateTime.UtcNow;
                config.last_error = "";
                await SaveConfigAsync(dbContext, config, cancellationToken);
                return Results.Ok(new
                {
                    success = true,
                    message = "Relatório enviado por FTP com sucesso.",
                    file_name = fileName,
                    destination_path = destinationPath,
                    config.last_sent_at
                });
            }

            config.last_error = result.message;
            await SaveConfigAsync(dbContext, config, cancellationToken);
            return Results.BadRequest(new { success = false, message = result.message });
        })
        .WithName("SendReportToFtp")
        .RequireAuthorization("CanDownloadReports");

        return app;
    }

    private static async Task<IResult> TestConnectionAsync(FtpExportConfig config, CancellationToken cancellationToken)
    {
        if (IsSftp(config.protocol))
        {
            return Results.BadRequest(new
            {
                success = false,
                message = "Configuração SFTP salva. O teste SFTP real precisa do driver SSH.NET no backend; use FTP/FTPS agora ou habilitamos SFTP na próxima etapa."
            });
        }

        try
        {
            var request = CreateFtpRequest(config, null, WebRequestMethods.Ftp.ListDirectory);
            using var registration = cancellationToken.Register(() => request.Abort());
            using var response = (FtpWebResponse)await request.GetResponseAsync();
            return Results.Ok(new
            {
                success = true,
                message = "Conexão FTP/FTPS testada com sucesso.",
                status = response.StatusDescription,
                config.host,
                config.port,
                config.protocol
            });
        }
        catch (Exception ex)
        {
            return Results.BadRequest(new
            {
                success = false,
                message = $"Falha ao testar FTP/FTPS: {ex.Message}",
                config.host,
                config.port,
                config.protocol
            });
        }
    }

    private static async Task<(bool success, string message)> UploadFileAsync(
        FtpExportConfig config,
        string fileName,
        string content,
        CancellationToken cancellationToken,
        string? destinationPath = null)
    {
        return await UploadFileAsync(config, fileName, Encoding.UTF8.GetBytes(content), cancellationToken, destinationPath);
    }

    private static async Task<(bool success, string message)> UploadFileAsync(
        FtpExportConfig config,
        string fileName,
        byte[] bytes,
        CancellationToken cancellationToken,
        string? destinationPath = null)
    {
        if (IsSftp(config.protocol))
        {
            return (false, "Envio SFTP real ainda não habilitado no backend. Use FTP/FTPS ou habilite SSH.NET na próxima etapa.");
        }

        try
        {
            await EnsureFtpDirectoryAsync(config, destinationPath, cancellationToken);
            var request = CreateFtpRequest(config, fileName, WebRequestMethods.Ftp.UploadFile, destinationPath);
            request.ContentLength = bytes.Length;

            using var registration = cancellationToken.Register(() => request.Abort());
            await using (var stream = await request.GetRequestStreamAsync())
            {
                await stream.WriteAsync(bytes, cancellationToken);
            }

            using var response = (FtpWebResponse)await request.GetResponseAsync();
            return (true, response.StatusDescription ?? "Arquivo enviado.");
        }
        catch (Exception ex)
        {
            return (false, $"Falha ao enviar arquivo FTP/FTPS: {ex.Message}");
        }
    }

    private static async Task EnsureFtpDirectoryAsync(FtpExportConfig config, string? destinationPath, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(destinationPath)) return;

        var accumulated = "";
        foreach (var segment in NormalizePath(destinationPath).Split('/', StringSplitOptions.RemoveEmptyEntries))
        {
            accumulated += "/" + segment;
            try
            {
                var request = CreateFtpRequest(config, null, WebRequestMethods.Ftp.MakeDirectory, accumulated);
                using var registration = cancellationToken.Register(() => request.Abort());
                using var response = (FtpWebResponse)await request.GetResponseAsync();
            }
            catch (WebException ex) when (ex.Response is FtpWebResponse response &&
                                          (response.StatusCode == FtpStatusCode.ActionNotTakenFileUnavailable ||
                                           response.StatusCode == FtpStatusCode.ActionNotTakenFilenameNotAllowed))
            {
                response.Close();
            }
        }
    }

    private static FtpWebRequest CreateFtpRequest(FtpExportConfig config, string? fileName, string method, string? destinationPath = null)
    {
        var uri = BuildFtpUri(config, fileName, destinationPath);
        var request = (FtpWebRequest)WebRequest.Create(uri);
        request.Method = method;
        request.Credentials = new NetworkCredential(config.username, config.password);
        request.EnableSsl = string.Equals(config.protocol, "FTPS", StringComparison.OrdinalIgnoreCase);
        request.UseBinary = true;
        request.KeepAlive = false;
        request.Timeout = 10000;
        request.ReadWriteTimeout = 10000;
        return request;
    }

    private static Uri BuildFtpUri(FtpExportConfig config, string? fileName, string? destinationPath = null)
    {
        var path = NormalizePath(destinationPath ?? config.destination_path);
        if (!string.IsNullOrWhiteSpace(fileName))
        {
            path = $"{path.TrimEnd('/')}/{Uri.EscapeDataString(fileName)}";
        }

        return new Uri($"ftp://{config.host}:{config.port}{path}");
    }

    private static string NormalizePath(string value)
    {
        var path = string.IsNullOrWhiteSpace(value) ? "/" : value.Trim();
        return path.StartsWith('/') ? path : $"/{path}";
    }

    private static string SanitizeFilePart(string value)
    {
        var builder = new StringBuilder();
        foreach (var c in value.Trim())
        {
            builder.Append(char.IsLetterOrDigit(c) || c is '-' or '_' ? c : '_');
        }

        return builder.Length == 0 ? "sem_nome" : builder.ToString();
    }

    private static string? NormalizeReportFormat(string? format)
    {
        var normalized = (format ?? "csv").Trim().ToLowerInvariant();
        return normalized is "csv" or "xml" or "pdf" ? normalized : null;
    }

    private static byte[] BuildReportFile(string csv, string format, string reportType, string machine)
    {
        if (format == "xml")
        {
            return Encoding.UTF8.GetBytes(ConvertCsvToXml(csv, reportType, machine));
        }

        if (format == "pdf")
        {
            return BuildSimplePdf($"Relatorio {reportType} - {machine}", csv);
        }

        return Encoding.UTF8.GetBytes(csv);
    }

    private static string ConvertCsvToXml(string csv, string reportType, string machine)
    {
        var lines = csv.Replace("\r\n", "\n").Split('\n', StringSplitOptions.RemoveEmptyEntries);
        var headers = lines.Length > 0 ? SplitDelimitedLine(lines[0]) : new List<string>();
        var builder = new StringBuilder();
        builder.AppendLine("<?xml version=\"1.0\" encoding=\"UTF-8\"?>");
        builder.AppendLine("<analicty-report>");
        builder.AppendLine($"  <report-type>{WebUtility.HtmlEncode(reportType)}</report-type>");
        builder.AppendLine($"  <machine>{WebUtility.HtmlEncode(machine)}</machine>");
        builder.AppendLine($"  <generated-at>{WebUtility.HtmlEncode(DateTime.UtcNow.ToString("O"))}</generated-at>");
        builder.AppendLine("  <rows>");

        for (var i = 1; i < lines.Length; i++)
        {
            var values = SplitDelimitedLine(lines[i]);
            builder.AppendLine("    <row>");
            for (var columnIndex = 0; columnIndex < values.Count; columnIndex++)
            {
                var name = columnIndex < headers.Count ? headers[columnIndex] : $"coluna_{columnIndex + 1}";
                builder.AppendLine($"      <column name=\"{WebUtility.HtmlEncode(name)}\">{WebUtility.HtmlEncode(values[columnIndex])}</column>");
            }
            builder.AppendLine("    </row>");
        }

        builder.AppendLine("  </rows>");
        builder.AppendLine("</analicty-report>");
        return builder.ToString();
    }

    private static List<string> SplitDelimitedLine(string line)
    {
        var values = new List<string>();
        var current = new StringBuilder();
        var inQuotes = false;

        for (var i = 0; i < line.Length; i++)
        {
            var c = line[i];
            if (c == '"')
            {
                if (inQuotes && i + 1 < line.Length && line[i + 1] == '"')
                {
                    current.Append('"');
                    i++;
                }
                else
                {
                    inQuotes = !inQuotes;
                }
                continue;
            }

            if (c == ';' && !inQuotes)
            {
                values.Add(current.ToString());
                current.Clear();
                continue;
            }

            current.Append(c);
        }

        values.Add(current.ToString());
        return values;
    }

    private static byte[] BuildSimplePdf(string title, string csv)
    {
        var rows = csv.Replace("\r\n", "\n")
            .Split('\n', StringSplitOptions.RemoveEmptyEntries)
            .Take(48)
            .Select(line => line.Replace(";", " | "))
            .ToList();

        var content = new StringBuilder();
        content.AppendLine("BT");
        content.AppendLine("/F1 10 Tf");
        content.AppendLine("40 800 Td");
        content.AppendLine($"({EscapePdfText(title)}) Tj");
        content.AppendLine("0 -18 Td");
        foreach (var row in rows)
        {
            var text = row.Length > 110 ? row[..110] : row;
            content.AppendLine($"({EscapePdfText(text)}) Tj");
            content.AppendLine("0 -14 Td");
        }
        content.AppendLine("ET");

        var streamBytes = Encoding.ASCII.GetBytes(content.ToString());
        var objects = new List<string>
        {
            "1 0 obj\n<< /Type /Catalog /Pages 2 0 R >>\nendobj\n",
            "2 0 obj\n<< /Type /Pages /Kids [3 0 R] /Count 1 >>\nendobj\n",
            "3 0 obj\n<< /Type /Page /Parent 2 0 R /MediaBox [0 0 595 842] /Resources << /Font << /F1 4 0 R >> >> /Contents 5 0 R >>\nendobj\n",
            "4 0 obj\n<< /Type /Font /Subtype /Type1 /BaseFont /Helvetica >>\nendobj\n",
            $"5 0 obj\n<< /Length {streamBytes.Length} >>\nstream\n{content}endstream\nendobj\n"
        };

        var pdf = new StringBuilder();
        var offsets = new List<int> { 0 };
        pdf.Append("%PDF-1.4\n");
        foreach (var item in objects)
        {
            offsets.Add(Encoding.ASCII.GetByteCount(pdf.ToString()));
            pdf.Append(item);
        }

        var xrefOffset = Encoding.ASCII.GetByteCount(pdf.ToString());
        pdf.AppendLine("xref");
        pdf.AppendLine($"0 {objects.Count + 1}");
        pdf.AppendLine("0000000000 65535 f ");
        foreach (var offset in offsets.Skip(1))
        {
            pdf.AppendLine($"{offset:0000000000} 00000 n ");
        }
        pdf.AppendLine("trailer");
        pdf.AppendLine($"<< /Size {objects.Count + 1} /Root 1 0 R >>");
        pdf.AppendLine("startxref");
        pdf.AppendLine(xrefOffset.ToString());
        pdf.AppendLine("%%EOF");
        return Encoding.ASCII.GetBytes(pdf.ToString());
    }

    private static string EscapePdfText(string value)
    {
        return value
            .Replace("\\", "\\\\")
            .Replace("(", "\\(")
            .Replace(")", "\\)");
    }

    private static string BuildSampleExport(FtpExportConfig config)
    {
        var now = DateTime.UtcNow.ToString("O");
        if (string.Equals(config.file_format, "JSON", StringComparison.OrdinalIgnoreCase))
        {
            return JsonSerializer.Serialize(new
            {
                generated_at = now,
                source = "AnalictY",
                export_type = config.data_type,
                message = "Arquivo de teste enviado pelo conector FTP/SFTP."
            }, new JsonSerializerOptions { WriteIndented = true });
        }

        if (string.Equals(config.file_format, "XML", StringComparison.OrdinalIgnoreCase))
        {
            return $"<?xml version=\"1.0\" encoding=\"UTF-8\"?><analicty-export><generated_at>{WebUtility.HtmlEncode(now)}</generated_at><source>AnalictY</source><export_type>{WebUtility.HtmlEncode(config.data_type)}</export_type><message>Arquivo de teste enviado pelo conector FTP/SFTP.</message></analicty-export>";
        }

        return "generated_at;source;export_type;message\r\n" +
               $"{now};AnalictY;{config.data_type};Arquivo de teste enviado pelo conector FTP/SFTP.\r\n";
    }

    private static IResult? ValidateConfig(FtpExportConfig config)
    {
        if (string.IsNullOrWhiteSpace(config.name)) return Results.BadRequest(new { message = "Informe o nome da conexão." });
        if (string.IsNullOrWhiteSpace(config.host)) return Results.BadRequest(new { message = "Informe o host/IP do servidor." });
        if (config.port <= 0) return Results.BadRequest(new { message = "Informe uma porta válida." });
        if (string.IsNullOrWhiteSpace(config.username)) return Results.BadRequest(new { message = "Informe o usuário." });
        if (string.IsNullOrWhiteSpace(config.password) && string.IsNullOrWhiteSpace(config.private_key_path))
        {
            return Results.BadRequest(new { message = "Informe senha ou chave privada." });
        }

        if (!new[] { "FTP", "FTPS", "SFTP" }.Contains(config.protocol, StringComparer.OrdinalIgnoreCase))
        {
            return Results.BadRequest(new { message = "Protocolo inválido. Use FTP, FTPS ou SFTP." });
        }

        return null;
    }

    private static bool IsSftp(string protocol) =>
        string.Equals(protocol, "SFTP", StringComparison.OrdinalIgnoreCase);

    private static FtpExportConfig NormalizeRequest(FtpExportConfigRequest request, FtpExportConfig current)
    {
        var protocol = (request.protocol ?? current.protocol ?? "SFTP").Trim().ToUpperInvariant();
        var password = string.IsNullOrEmpty(request.password) ? current.password : request.password;
        return new FtpExportConfig
        {
            name = string.IsNullOrWhiteSpace(request.name) ? current.name : request.name.Trim(),
            enabled = request.enabled ?? current.enabled,
            protocol = protocol,
            host = (request.host ?? current.host).Trim(),
            port = request.port ?? (protocol == "SFTP" ? 22 : 21),
            username = (request.username ?? current.username).Trim(),
            password = password ?? "",
            private_key_path = (request.private_key_path ?? current.private_key_path).Trim(),
            destination_path = string.IsNullOrWhiteSpace(request.destination_path) ? "/" : request.destination_path.Trim(),
            frequency = string.IsNullOrWhiteSpace(request.frequency) ? current.frequency : request.frequency.Trim(),
            data_type = string.IsNullOrWhiteSpace(request.data_type) ? current.data_type : request.data_type.Trim(),
            file_format = string.IsNullOrWhiteSpace(request.file_format) ? current.file_format : request.file_format.Trim().ToUpperInvariant(),
            last_sent_at = current.last_sent_at,
            last_error = current.last_error
        };
    }

    private static async Task<FtpExportConfig> GetConfigAsync(ScadaDbContext dbContext, CancellationToken cancellationToken)
    {
        var value = await dbContext.SystemSettings
            .Where(item => item.Key == ConfigKey)
            .Select(item => item.Value)
            .FirstOrDefaultAsync(cancellationToken);

        if (string.IsNullOrWhiteSpace(value)) return FtpExportConfig.Default();
        return JsonSerializer.Deserialize<FtpExportConfig>(value) ?? FtpExportConfig.Default();
    }

    private static async Task SaveConfigAsync(ScadaDbContext dbContext, FtpExportConfig config, CancellationToken cancellationToken)
    {
        var setting = await dbContext.SystemSettings.FirstOrDefaultAsync(item => item.Key == ConfigKey, cancellationToken);
        if (setting == null)
        {
            dbContext.SystemSettings.Add(new SystemSetting
            {
                Key = ConfigKey,
                Value = JsonSerializer.Serialize(config),
                UpdatedAt = DateTime.UtcNow
            });
        }
        else
        {
            setting.Value = JsonSerializer.Serialize(config);
            setting.UpdatedAt = DateTime.UtcNow;
        }

        await dbContext.SaveChangesAsync(cancellationToken);
    }

    private static object ToPublicConfig(FtpExportConfig config) => new
    {
        config.name,
        config.enabled,
        config.protocol,
        config.host,
        config.port,
        config.username,
        password_configured = !string.IsNullOrWhiteSpace(config.password),
        config.private_key_path,
        config.destination_path,
        config.frequency,
        config.data_type,
        config.file_format,
        config.last_sent_at,
        config.last_error
    };

    private sealed record FtpExportConfigRequest(
        string? name,
        bool? enabled,
        string? protocol,
        string? host,
        int? port,
        string? username,
        string? password,
        string? private_key_path,
        string? destination_path,
        string? frequency,
        string? data_type,
        string? file_format);

    public sealed record FtpReportExportRequest(
        string report_type,
        string? machine_id,
        string? machine_code,
        DateTime inicio_em,
        DateTime fim_em,
        string formato,
        string? destination_path,
        bool incluir_motivos_parada = false);

    private sealed class FtpExportConfig
    {
        public string name { get; set; } = "EDDY";
        public bool enabled { get; set; } = false;
        public string protocol { get; set; } = "SFTP";
        public string host { get; set; } = "";
        public int port { get; set; } = 22;
        public string username { get; set; } = "";
        public string password { get; set; } = "";
        public string private_key_path { get; set; } = "";
        public string destination_path { get; set; } = "/entrada/mes";
        public string frequency { get; set; } = "manual";
        public string data_type { get; set; } = "producao";
        public string file_format { get; set; } = "CSV";
        public DateTime? last_sent_at { get; set; }
        public string last_error { get; set; } = "";

        public static FtpExportConfig Default() => new();
    }
}
