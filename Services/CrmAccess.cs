using MiniCRM.Models;

namespace MiniCRM.Services;

public static class CrmAccess
{
    // Demo identities, deliberately not a password-based authentication system.
    public static AppUser ActiveUser(CrmData data, int? selectedId) =>
        data.Users.FirstOrDefault(u => u.Id == selectedId) ?? data.Users.First(u => u.Id == data.Users.Min(x => x.Id));

    public static RoleDef Role(CrmData data, AppUser user) => data.Roles.Single(r => r.Id == user.RoleId);

    public static bool CanUse(CrmData data, AppUser user, string module) =>
        user.Status == "Active" && Role(data, user).Modules.Contains(module);

    public static void Require(CrmData data, AppUser user, string module)
    {
        if (!CanUse(data, user, module)) throw new CrmException(403, "This demo account cannot use this module.");
    }
}

public sealed class CrmException(int statusCode, string message) : Exception(message)
{
    public int StatusCode { get; } = statusCode;
}
