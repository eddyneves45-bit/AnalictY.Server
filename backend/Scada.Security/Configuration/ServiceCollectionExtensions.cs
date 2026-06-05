using Microsoft.Extensions.DependencyInjection;
using Scada.Security.Interfaces;
using Scada.Security.Services;

namespace Scada.Security.Configuration;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddSecurityModule(
        this IServiceCollection services,
        string jwtKey,
        string jwtIssuer,
        string jwtAudience)
    {
        // Password Service
        services.AddSingleton<IPasswordService, PasswordService>();

        // Token Service
        services.AddSingleton<ITokenService>(provider => new TokenService(jwtKey, jwtIssuer, jwtAudience));

        // Permission Service (RBAC)
        services.AddSingleton<IPermissionService, PermissionService>();

        // MFA / TOTP Service
        services.AddSingleton<ITotpService, TotpService>();

        // Session Service
        services.AddSingleton<ISessionService, SessionService>();

        // Auth Service
        services.AddScoped<IAuthService, AuthService>();

        return services;
    }
}
