using System.ComponentModel.DataAnnotations;
using System.Security.Claims;
using POSApp.Persistence;
using POSApp.Security;

namespace POSApp.Services;

public static class StoreAccess
{
    public static async Task<int> RequireAsync(AppDbContext db, ClaimsPrincipal actor, string roleOrPolicy, CancellationToken ct = default)
    {
        if (actor.Identity?.IsAuthenticated != true
            || !int.TryParse(actor.FindFirstValue(ClaimTypes.NameIdentifier), out var id))
        {
            throw new UnauthorizedAccessException("Your account no longer has access to this operation.");
        }

        var claimRoles = actor.FindAll(ClaimTypes.Role).Select(c => c.Value)
            .Concat(actor.FindAll("role").Select(c => c.Value))
            .Distinct(StringComparer.Ordinal)
            .ToArray();

        if (claimRoles.Length > 0 && RolePermissionService.TryCheckCached(claimRoles, roleOrPolicy, out var granted) && granted)
        {
            return id;
        }

        if (!await RolePermissionService.HasPolicyAsync(db, id, roleOrPolicy, ct))
        {
            throw new UnauthorizedAccessException("Your account no longer has access to this operation.");
        }
        return id;
    }

    public static async Task<bool> HasPolicyAsync(AppDbContext db, ClaimsPrincipal actor, string roleOrPolicy, CancellationToken ct = default)
    {
        if (actor.Identity?.IsAuthenticated != true
            || !int.TryParse(actor.FindFirstValue(ClaimTypes.NameIdentifier), out var id))
        {
            return false;
        }

        var claimRoles = actor.FindAll(ClaimTypes.Role).Select(c => c.Value)
            .Concat(actor.FindAll("role").Select(c => c.Value))
            .Distinct(StringComparer.Ordinal)
            .ToArray();

        if (claimRoles.Length > 0 && RolePermissionService.TryCheckCached(claimRoles, roleOrPolicy, out var granted))
        {
            return granted;
        }

        return await RolePermissionService.HasPolicyAsync(db, id, roleOrPolicy, ct);
    }

    public static void Money(decimal amount, string label, bool negative = false)
    {
        if ((!negative && amount < 0) || amount < -9999999999999999.99m || amount > 9999999999999999.99m || decimal.Round(amount, 2) != amount)
        {
            throw new ValidationException($"{label} must be within the supported range with at most two decimal places.");
        }
    }
}
