using Scada.Security.DTOs;

namespace Scada.Security.Interfaces;

public interface IIdentityProvider
{
    Task<AuthResponse> AuthenticateExternalAsync(string provider, string externalToken);
    Task<AuthResponse> RegisterExternalAsync(string provider, string externalToken, RegisterRequest request);
    string GetProviderName();
}
