using System.Diagnostics;
using System.IO.Compression;
using System.Reflection;
using System.Text.Json;

public static class SystemEndpoints
{
    public static WebApplication MapSystemEndpoints(this WebApplication app)
    {
        app.MapGet("/api/system/version", (IConfiguration configuration, IWebHostEnvironment environment) =>
        {
            var contentRoot = environment.ContentRootPath;
            var installRoot = Path.GetFullPath(Path.Combine(contentRoot, "..", ".."));
            var versionFile = Path.Combine(installRoot, "installer", "version.json");
            var assemblyVersion = Assembly.GetExecutingAssembly().GetName().Version?.ToString() ?? "0.0.0";

            if (File.Exists(versionFile))
            {
                try
                {
                    using var document = JsonDocument.Parse(File.ReadAllText(versionFile));
                    var root = document.RootElement;
                    return Results.Ok(new
                    {
                        product = root.TryGetProperty("product", out var product) ? product.GetString() : "AnalictY",
                        version = root.TryGetProperty("version", out var version) ? version.GetString() : assemblyVersion,
                        channel = root.TryGetProperty("channel", out var channel) ? channel.GetString() : "stable",
                        built_at = root.TryGetProperty("built_at", out var builtAt) ? builtAt.GetString() : null,
                        data_directory = configuration["AnalictY:DataDirectory"],
                        source = "installer"
                    });
                }
                catch (JsonException)
                {
                    // Fall through to assembly metadata when the local version file is corrupt.
                }
            }

            return Results.Ok(new
            {
                product = "AnalictY",
                version = assemblyVersion,
                channel = "dev",
                built_at = (string?)null,
                data_directory = configuration["AnalictY:DataDirectory"],
                source = "assembly"
            });
        })
        .AllowAnonymous();

        app.MapGet("/api/system/health", (IConfiguration configuration, IWebHostEnvironment environment) =>
        {
            var dataDirectory = ResolveDataDirectory(configuration, environment);
            var databasePath = Path.Combine(dataDirectory, "scada.db");

            return Results.Ok(new
            {
                product = "AnalictY",
                status = "healthy",
                timestamp = DateTimeOffset.UtcNow,
                data_directory = dataDirectory,
                database_exists = File.Exists(databasePath)
            });
        })
        .AllowAnonymous();

        app.MapGet("/api/system/updates/check", async (
            IConfiguration configuration,
            IWebHostEnvironment environment,
            IHttpClientFactory httpClientFactory,
            CancellationToken cancellationToken) =>
        {
            var current = ResolveCurrentVersion(configuration, environment);
            var manifestUrl = ResolveUpdateManifestUrl(configuration);
            if (string.IsNullOrWhiteSpace(manifestUrl))
            {
                return Results.Ok(new
                {
                    configured = false,
                    update_available = false,
                    current,
                    latest = (object?)null,
                    message = "Manifesto remoto de atualizacao ainda nao configurado."
                });
            }

            try
            {
                var latest = await FetchUpdateManifestAsync(httpClientFactory, manifestUrl, cancellationToken);

                return Results.Ok(new
                {
                    configured = true,
                    update_available = IsNewerVersion(latest.version, current.version),
                    current,
                    latest,
                    message = "Manifesto remoto consultado."
                });
            }
            catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException)
            {
                return Results.Ok(new
                {
                    configured = true,
                    update_available = false,
                    current,
                    latest = (object?)null,
                    message = $"Falha ao consultar manifesto remoto: {ex.Message}"
                });
            }
        })
        .AllowAnonymous();

        app.MapPost("/api/system/updates/download", async (
            IConfiguration configuration,
            IWebHostEnvironment environment,
            IHttpClientFactory httpClientFactory,
            CancellationToken cancellationToken) =>
        {
            var current = ResolveCurrentVersion(configuration, environment);
            var manifestUrl = ResolveUpdateManifestUrl(configuration);
            if (string.IsNullOrWhiteSpace(manifestUrl))
            {
                return Results.BadRequest(new { success = false, message = "Manifesto remoto de atualizacao ainda nao configurado." });
            }

            UpdateManifest latest;
            try
            {
                latest = await FetchUpdateManifestAsync(httpClientFactory, manifestUrl, cancellationToken);
            }
            catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException)
            {
                return Results.BadRequest(new { success = false, message = $"Falha ao consultar manifesto remoto: {ex.Message}" });
            }

            var validationError = ValidateManifestForDownload(latest, current);
            if (validationError != null)
            {
                return Results.BadRequest(new { success = false, message = validationError });
            }

            var dataDirectory = ResolveDataDirectory(configuration, environment);
            var packagesRoot = Path.Combine(dataDirectory, "updates", "packages");
            Directory.CreateDirectory(packagesRoot);

            var packageFile = Path.Combine(packagesRoot, $"AnalictY-{SafeFileName(latest.version)}.zip");
            var manifestFile = Path.Combine(packagesRoot, $"AnalictY-{SafeFileName(latest.version)}.json");

            try
            {
                var client = httpClientFactory.CreateClient();
                client.Timeout = TimeSpan.FromMinutes(10);
                await using var packageStream = await client.GetStreamAsync(latest.url!, cancellationToken);
                await using (var fileStream = File.Create(packageFile))
                {
                    await packageStream.CopyToAsync(fileStream, cancellationToken);
                }

                var actualHash = await ComputeSha256Async(packageFile, cancellationToken);
                if (!string.Equals(actualHash, latest.sha256, StringComparison.OrdinalIgnoreCase))
                {
                    File.Delete(packageFile);
                    return Results.BadRequest(new
                    {
                        success = false,
                        message = "SHA256 do pacote baixado nao confere.",
                        expected = latest.sha256,
                        actual = actualHash
                    });
                }

                var packageValidationError = ValidateUpdatePackage(packageFile);
                if (packageValidationError != null)
                {
                    File.Delete(packageFile);
                    return Results.BadRequest(new { success = false, message = packageValidationError });
                }

                await File.WriteAllTextAsync(manifestFile, JsonSerializer.Serialize(latest, new JsonSerializerOptions { WriteIndented = true }), cancellationToken);

                return Results.Ok(new
                {
                    success = true,
                    message = "Pacote de atualizacao baixado e validado.",
                    version = latest.version,
                    package_file = packageFile,
                    sha256 = actualHash
                });
            }
            catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or IOException)
            {
                if (File.Exists(packageFile))
                {
                    File.Delete(packageFile);
                }

                return Results.BadRequest(new { success = false, message = $"Falha ao baixar pacote: {ex.Message}" });
            }
        })
        .RequireAuthorization(policy => policy.RequireRole("admin"));

        app.MapPost("/api/system/updates/apply", async (
            UpdateApplyRequest? request,
            IConfiguration configuration,
            IWebHostEnvironment environment,
            CancellationToken cancellationToken) =>
        {
            var dataDirectory = ResolveDataDirectory(configuration, environment);
            var packagesRoot = Path.Combine(dataDirectory, "updates", "packages");
            if (!Directory.Exists(packagesRoot))
            {
                return Results.BadRequest(new { success = false, message = "Nenhum pacote de atualizacao foi baixado." });
            }

            var manifestFile = ResolveLocalManifestFile(packagesRoot, request?.version);
            if (manifestFile == null)
            {
                return Results.BadRequest(new { success = false, message = "Manifesto local da atualizacao nao encontrado." });
            }

            var manifest = JsonSerializer.Deserialize<UpdateManifest>(await File.ReadAllTextAsync(manifestFile, cancellationToken), new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            });

            if (manifest == null || string.IsNullOrWhiteSpace(manifest.version) || string.IsNullOrWhiteSpace(manifest.sha256))
            {
                return Results.BadRequest(new { success = false, message = "Manifesto local da atualizacao esta incompleto." });
            }

            var installRoot = ResolveInstallRoot(environment);
            var updaterScript = Path.Combine(installRoot, "updater", "apply-update.ps1");
            if (!File.Exists(updaterScript))
            {
                return Results.BadRequest(new { success = false, message = $"Updater nao encontrado em {updaterScript}." });
            }

            var packageFile = Path.Combine(packagesRoot, $"AnalictY-{SafeFileName(manifest.version)}.zip");
            if (!File.Exists(packageFile))
            {
                return Results.BadRequest(new { success = false, message = "Pacote ZIP da atualizacao nao encontrado." });
            }

            var actualHash = await ComputeSha256Async(packageFile, cancellationToken);
            if (!string.Equals(actualHash, manifest.sha256, StringComparison.OrdinalIgnoreCase))
            {
                return Results.BadRequest(new
                {
                    success = false,
                    message = "SHA256 do pacote local nao confere.",
                    expected = manifest.sha256,
                    actual = actualHash
                });
            }

            var startInfo = new ProcessStartInfo
            {
                FileName = "powershell.exe",
                UseShellExecute = false,
                CreateNoWindow = true,
                WorkingDirectory = installRoot
            };
            startInfo.ArgumentList.Add("-ExecutionPolicy");
            startInfo.ArgumentList.Add("Bypass");
            startInfo.ArgumentList.Add("-File");
            startInfo.ArgumentList.Add(updaterScript);
            startInfo.ArgumentList.Add("-InstallRoot");
            startInfo.ArgumentList.Add(installRoot);
            startInfo.ArgumentList.Add("-PackagePath");
            startInfo.ArgumentList.Add(packageFile);
            startInfo.ArgumentList.Add("-TargetVersion");
            startInfo.ArgumentList.Add(manifest.version);
            startInfo.ArgumentList.Add("-ExpectedSha256");
            startInfo.ArgumentList.Add(manifest.sha256);
            startInfo.ArgumentList.Add("-BackendHealthUrl");
            startInfo.ArgumentList.Add(configuration["AnalictY:BackendHealthUrl"] ?? "http://127.0.0.1:5000/api/system/health");
            startInfo.ArgumentList.Add("-FrontendUrl");
            startInfo.ArgumentList.Add(configuration["AnalictY:FrontendHealthUrl"] ?? "http://127.0.0.1:3000");

            Process.Start(startInfo);

            return Results.Accepted("/api/system/updates/check", new
            {
                success = true,
                message = "Atualizacao iniciada. O AnalictY pode ficar indisponivel por alguns instantes.",
                version = manifest.version
            });
        })
        .RequireAuthorization(policy => policy.RequireRole("admin"));

        return app;
    }

    private static async Task<UpdateManifest> FetchUpdateManifestAsync(
        IHttpClientFactory httpClientFactory,
        string manifestUrl,
        CancellationToken cancellationToken)
    {
        var client = httpClientFactory.CreateClient();
        client.Timeout = TimeSpan.FromSeconds(10);
        using var response = await client.GetAsync(manifestUrl, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            throw new HttpRequestException($"Manifesto remoto retornou HTTP {(int)response.StatusCode}.");
        }

        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
        var root = document.RootElement;

        return new UpdateManifest(
            root.TryGetProperty("product", out var product) ? product.GetString() ?? "AnalictY" : "AnalictY",
            root.TryGetProperty("version", out var version) ? version.GetString() : null,
            root.TryGetProperty("channel", out var channel) ? channel.GetString() ?? "stable" : "stable",
            root.TryGetProperty("url", out var url) ? url.GetString() : null,
            root.TryGetProperty("sha256", out var sha256) ? sha256.GetString() : null,
            root.TryGetProperty("released_at", out var releasedAt) ? releasedAt.GetString() : null,
            root.TryGetProperty("changelog", out var changelog) && changelog.ValueKind == JsonValueKind.Array
                ? changelog.EnumerateArray().Select(item => item.GetString()).Where(item => !string.IsNullOrWhiteSpace(item)).Select(item => item!).ToArray()
                : []);
    }

    private static string? ValidateManifestForDownload(UpdateManifest latest, SystemVersionInfo current)
    {
        if (!string.Equals(latest.product, "AnalictY", StringComparison.OrdinalIgnoreCase))
        {
            return "Manifesto remoto nao pertence ao produto AnalictY.";
        }
        if (string.IsNullOrWhiteSpace(latest.version))
        {
            return "Manifesto remoto nao informou versao.";
        }
        if (!IsNewerVersion(latest.version, current.version))
        {
            return "Nao existe versao mais nova para baixar.";
        }
        if (string.IsNullOrWhiteSpace(latest.url) || !Uri.TryCreate(latest.url, UriKind.Absolute, out var uri) || (uri.Scheme != Uri.UriSchemeHttps && uri.Scheme != Uri.UriSchemeHttp))
        {
            return "Manifesto remoto nao informou uma URL HTTP/HTTPS valida.";
        }
        if (string.IsNullOrWhiteSpace(latest.sha256))
        {
            return "Manifesto remoto nao informou SHA256 do pacote.";
        }

        return null;
    }

    private static string? ValidateUpdatePackage(string packageFile)
    {
        try
        {
            using var archive = ZipFile.OpenRead(packageFile);
            var entries = archive.Entries
                .Select(entry => entry.FullName.Replace('\\', '/'))
                .ToHashSet(StringComparer.OrdinalIgnoreCase);

            if (!entries.Contains("app/backend/Scada.Api.exe"))
            {
                return "Pacote de atualizacao invalido: backend Scada.Api.exe nao encontrado.";
            }

            if (!entries.Contains("app/frontend/server.js"))
            {
                return "Pacote de atualizacao invalido: frontend server.js nao encontrado.";
            }

            if (!entries.Contains("installer/version.json"))
            {
                return "Pacote de atualizacao invalido: installer/version.json nao encontrado.";
            }

            return null;
        }
        catch (InvalidDataException ex)
        {
            return $"Pacote de atualizacao invalido: {ex.Message}";
        }
        catch (IOException ex)
        {
            return $"Nao foi possivel validar o pacote de atualizacao: {ex.Message}";
        }
    }

    private static string ResolveInstallRoot(IWebHostEnvironment environment)
    {
        return Path.GetFullPath(Path.Combine(environment.ContentRootPath, "..", ".."));
    }

    private static string ResolveUpdateManifestUrl(IConfiguration configuration)
    {
        var configured = configuration["AnalictY:UpdateManifestUrl"];
        return string.IsNullOrWhiteSpace(configured)
            ? "https://analicty-downloads.s3.sa-east-1.amazonaws.com/updates/stable/latest.json"
            : configured;
    }

    private static string ResolveDataDirectory(IConfiguration configuration, IWebHostEnvironment environment)
    {
        var configured = configuration["AnalictY:DataDirectory"];
        if (!string.IsNullOrWhiteSpace(configured))
        {
            return configured;
        }

        return Path.Combine(ResolveInstallRoot(environment), "data");
    }

    private static string? ResolveLocalManifestFile(string packagesRoot, string? requestedVersion)
    {
        if (!string.IsNullOrWhiteSpace(requestedVersion))
        {
            var exactFile = Path.Combine(packagesRoot, $"AnalictY-{SafeFileName(requestedVersion)}.json");
            return File.Exists(exactFile) ? exactFile : null;
        }

        return Directory
            .GetFiles(packagesRoot, "AnalictY-*.json")
            .Select(path => new FileInfo(path))
            .OrderByDescending(file => file.LastWriteTimeUtc)
            .FirstOrDefault()
            ?.FullName;
    }

    private static async Task<string> ComputeSha256Async(string path, CancellationToken cancellationToken)
    {
        await using var stream = File.OpenRead(path);
        var hash = await System.Security.Cryptography.SHA256.HashDataAsync(stream, cancellationToken);
        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    private static string SafeFileName(string? value)
    {
        var clean = string.IsNullOrWhiteSpace(value) ? "unknown" : value.Trim();
        foreach (var invalid in Path.GetInvalidFileNameChars())
        {
            clean = clean.Replace(invalid, '_');
        }

        return clean;
    }

    private static SystemVersionInfo ResolveCurrentVersion(IConfiguration configuration, IWebHostEnvironment environment)
    {
        var contentRoot = environment.ContentRootPath;
        var installRoot = Path.GetFullPath(Path.Combine(contentRoot, "..", ".."));
        var versionFile = Path.Combine(installRoot, "installer", "version.json");
        var assemblyVersion = Assembly.GetExecutingAssembly().GetName().Version?.ToString() ?? "0.0.0";

        if (File.Exists(versionFile))
        {
            try
            {
                using var document = JsonDocument.Parse(File.ReadAllText(versionFile));
                var root = document.RootElement;
                return new SystemVersionInfo(
                    root.TryGetProperty("product", out var product) ? product.GetString() ?? "AnalictY" : "AnalictY",
                    root.TryGetProperty("version", out var version) ? version.GetString() ?? assemblyVersion : assemblyVersion,
                    root.TryGetProperty("channel", out var channel) ? channel.GetString() ?? "stable" : "stable",
                    root.TryGetProperty("built_at", out var builtAt) ? builtAt.GetString() : null,
                    configuration["AnalictY:DataDirectory"],
                    "installer");
            }
            catch (JsonException)
            {
                // Fall through.
            }
        }

        return new SystemVersionInfo(
            "AnalictY",
            assemblyVersion,
            "dev",
            null,
            configuration["AnalictY:DataDirectory"],
            "assembly");
    }

    private static bool IsNewerVersion(string? candidate, string? current)
    {
        if (!Version.TryParse(NormalizeVersion(candidate), out var candidateVersion)) return false;
        if (!Version.TryParse(NormalizeVersion(current), out var currentVersion)) return true;
        return candidateVersion > currentVersion;
    }

    private static string NormalizeVersion(string? value)
    {
        var clean = string.IsNullOrWhiteSpace(value) ? "0.0.0" : value.Trim();
        var dash = clean.IndexOf('-');
        return dash >= 0 ? clean[..dash] : clean;
    }

    private sealed record SystemVersionInfo(
        string product,
        string version,
        string? channel,
        string? built_at,
        string? data_directory,
        string source);

    private sealed record UpdateManifest(
        string product,
        string? version,
        string? channel,
        string? url,
        string? sha256,
        string? released_at,
        string[] changelog);

    private sealed record UpdateApplyRequest(string? version);
}
