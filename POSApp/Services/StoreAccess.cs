using System.ComponentModel.DataAnnotations;
using System.Security.Claims;
using Microsoft.EntityFrameworkCore;
using POSApp.Persistence;

namespace POSApp.Services;

public static class StoreAccess
{
    public static async Task<int> RequireAsync(AppDbContext db, ClaimsPrincipal actor, string role, CancellationToken ct = default)
    {
        if (actor.Identity?.IsAuthenticated != true || !int.TryParse(actor.FindFirstValue(ClaimTypes.NameIdentifier), out var id)
            || !await db.UserRoles.AnyAsync(membership => membership.UserId == id && db.Roles.Any(item => item.Id == membership.RoleId && (item.Name == AppRoles.Admin || item.Name == role || (role == AppRoles.SalesAssociate && item.Name == AppRoles.InventoryManager))), ct))
        {
            throw new UnauthorizedAccessException("Your account no longer has access to this operation.");
        }
        return id;
    }

    public static void Money(decimal amount, string label, bool negative = false)
    {
        if ((!negative && amount < 0) || amount < -9999999999999999.99m || amount > 9999999999999999.99m || decimal.Round(amount, 2) != amount)
        {
            throw new ValidationException($"{label} must be within the supported range with at most two decimal places.");
        }
    }
}
