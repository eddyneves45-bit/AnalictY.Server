using Scada.Security.Interfaces;

namespace Scada.Security.Services;

public class PermissionService : IPermissionService
{
    private readonly Dictionary<string, List<string>> _rolePermissions;
    private readonly Dictionary<string, List<string>> _userPermissions;

    public PermissionService()
    {
        // Role-based permissions (RBAC)
        _rolePermissions = new Dictionary<string, List<string>>
        {
            ["admin"] = new List<string>
            {
                "system.admin",
                "machine.read",
                "machine.write",
                "alarm.read",
                "alarm.write",
                "alarm.ack",
                "driver.manage",
                "user.manage",
                "config.manage",
                "goals.manage",
                "reports.download",
                "alert-rules.manage",
                "users.manage",
                "audit.view"
            },
            ["user"] = new List<string>
            {
                "machine.read",
                "alarm.read",
                "dashboard.view"
            },
            ["custom"] = new List<string>()
        };

        _userPermissions = new Dictionary<string, List<string>>();
    }

    public bool HasPermissionByUserId(string userId, string permission)
    {
        if (_userPermissions.TryGetValue(userId, out var userPerms))
        {
            return userPerms.Contains(permission);
        }
        return false;
    }

    public bool HasPermissionByRole(string role, string permission)
    {
        if (_rolePermissions.TryGetValue(role, out var rolePerms))
        {
            return rolePerms.Contains(permission);
        }
        return false;
    }

    public List<string> GetUserPermissions(string userId, string role)
    {
        var permissions = new List<string>();

        if (_rolePermissions.TryGetValue(role, out var rolePerms))
        {
            permissions.AddRange(rolePerms);
        }

        if (_userPermissions.TryGetValue(userId, out var userPerms))
        {
            permissions.AddRange(userPerms);
        }

        return permissions.Distinct().ToList();
    }

    public void GrantUserPermission(string userId, string permission)
    {
        if (!_userPermissions.ContainsKey(userId))
        {
            _userPermissions[userId] = new List<string>();
        }
        _userPermissions[userId].Add(permission);
    }

    public void RevokeUserPermission(string userId, string permission)
    {
        if (_userPermissions.ContainsKey(userId))
        {
            _userPermissions[userId].Remove(permission);
        }
    }
}
