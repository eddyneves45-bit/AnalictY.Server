using System.Security.Cryptography.X509Certificates;
using Opc.Ua;
using Opc.Ua.Client;
using Opc.Ua.Configuration;
using Scada.Core.Models.SQLite;

namespace Scada.Api.Services;

internal interface IOpcuaSessionFactory
{
    Task<Session> CreateSessionAsync(OpcuaConfig config, CancellationToken cancellationToken = default);
}

internal sealed class OpcuaSessionFactory : IOpcuaSessionFactory
{
    public async Task<Session> CreateSessionAsync(OpcuaConfig config, CancellationToken cancellationToken = default)
    {
        var pkiPath = Path.Combine(Path.GetTempPath(), "scada-opcua-pki");
        Directory.CreateDirectory(Path.Combine(pkiPath, "own"));
        Directory.CreateDirectory(Path.Combine(pkiPath, "trusted"));
        Directory.CreateDirectory(Path.Combine(pkiPath, "issuer"));
        Directory.CreateDirectory(Path.Combine(pkiPath, "rejected"));

        var applicationCertificate = LoadConfiguredCertificate(config);
        var appConfig = new ApplicationConfiguration
        {
            ApplicationName = "SCADA API",
            ApplicationUri = $"urn:{System.Net.Dns.GetHostName()}:ScadaApi",
            ApplicationType = ApplicationType.Client,
            SecurityConfiguration = new SecurityConfiguration
            {
                ApplicationCertificate = applicationCertificate == null
                    ? new CertificateIdentifier
                    {
                        StoreType = CertificateStoreType.Directory,
                        StorePath = Path.Combine(pkiPath, "own"),
                        SubjectName = "SCADA API"
                    }
                    : new CertificateIdentifier(applicationCertificate),
                TrustedPeerCertificates = new CertificateTrustList
                {
                    StoreType = CertificateStoreType.Directory,
                    StorePath = Path.Combine(pkiPath, "trusted")
                },
                TrustedIssuerCertificates = new CertificateTrustList
                {
                    StoreType = CertificateStoreType.Directory,
                    StorePath = Path.Combine(pkiPath, "issuer")
                },
                RejectedCertificateStore = new CertificateTrustList
                {
                    StoreType = CertificateStoreType.Directory,
                    StorePath = Path.Combine(pkiPath, "rejected")
                },
                AutoAcceptUntrustedCertificates = true
            },
            TransportConfigurations = new TransportConfigurationCollection(),
            TransportQuotas = new TransportQuotas { OperationTimeout = 15000 },
            ClientConfiguration = new ClientConfiguration { DefaultSessionTimeout = 60000 },
            DisableHiResClock = true
        };

        await appConfig.Validate(ApplicationType.Client);
        var application = new ApplicationInstance
        {
            ApplicationName = "SCADA API",
            ApplicationType = ApplicationType.Client,
            ApplicationConfiguration = appConfig
        };

        if (applicationCertificate == null)
        {
            var hasCertificate = await application.CheckApplicationInstanceCertificate(false, 0);
            if (!hasCertificate)
            {
                throw new InvalidOperationException("Nao foi possivel criar o certificado OPC UA da aplicacao.");
            }
        }

        appConfig.CertificateValidator.CertificateValidation += (_, e) => e.Accept = true;

        var useSecurity = !string.Equals(config.SecurityMode, "None", StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(config.SecurityPolicy, "None", StringComparison.OrdinalIgnoreCase);
        var endpoint = CoreClientUtils.SelectEndpoint(config.ServerUrl, useSecurity, 10000);
        var endpointConfig = EndpointConfiguration.Create(appConfig);
        var configuredEndpoint = new ConfiguredEndpoint(null, endpoint, endpointConfig);
        var identity = string.IsNullOrWhiteSpace(config.Username)
            ? new UserIdentity(new AnonymousIdentityToken())
            : new UserIdentity(config.Username, config.Password);

        return await Session.Create(
            appConfig,
            configuredEndpoint,
            false,
            "SCADA API",
            (uint)Math.Max(config.UpdateInterval, 1000),
            identity,
            null,
            cancellationToken);
    }

    private static X509Certificate2? LoadConfiguredCertificate(OpcuaConfig config)
    {
        if (string.IsNullOrWhiteSpace(config.CertificatePath))
        {
            return null;
        }

        var certificatePath = ResolveExistingPath(config.CertificatePath, "certificado OPC UA");
        var extension = Path.GetExtension(certificatePath).ToLowerInvariant();

        if (extension is ".pfx" or ".p12")
        {
            return new X509Certificate2(
                certificatePath,
                config.Password,
                X509KeyStorageFlags.Exportable | X509KeyStorageFlags.EphemeralKeySet);
        }

        if (string.IsNullOrWhiteSpace(config.PrivateKeyPath))
        {
            throw new InvalidOperationException("Informe a chave privada para usar certificado OPC UA em formato PEM/CRT.");
        }

        var privateKeyPath = ResolveExistingPath(config.PrivateKeyPath, "chave privada OPC UA");
        var pemCertificate = X509Certificate2.CreateFromPemFile(certificatePath, privateKeyPath);
        return new X509Certificate2(
            pemCertificate.Export(X509ContentType.Pkcs12),
            string.Empty,
            X509KeyStorageFlags.Exportable | X509KeyStorageFlags.EphemeralKeySet);
    }

    private static string ResolveExistingPath(string path, string label)
    {
        var resolvedPath = Path.IsPathRooted(path)
            ? path
            : Path.GetFullPath(path, Directory.GetCurrentDirectory());

        if (!File.Exists(resolvedPath))
        {
            throw new FileNotFoundException($"Arquivo de {label} nao encontrado: {resolvedPath}", resolvedPath);
        }

        return resolvedPath;
    }
}
