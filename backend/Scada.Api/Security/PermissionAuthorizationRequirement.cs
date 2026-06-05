using Microsoft.AspNetCore.Authorization;

namespace Scada.Api.Security;

internal sealed class PermissionAuthorizationRequirement : IAuthorizationRequirement
{
    public PermissionAuthorizationRequirement(string permission)
    {
        Permission = permission;
    }

    public string Permission { get; }
}
