namespace Scada.Security.Interfaces;

public interface IPermissionService
{
    bool HasPermissionByUserId(string userId, string permission);
    bool HasPermissionByRole(string role, string permission);
    List<string> GetUserPermissions(string userId, string role);
    void GrantUserPermission(string userId, string permission);
    void RevokeUserPermission(string userId, string permission);
}
