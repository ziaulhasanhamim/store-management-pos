using System.Data;
using System.Security.Claims;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using POSApp.Client.DTOs;
using POSApp.Persistence;
using POSApp.Persistence.Entities;
using POSApp.Security;

namespace POSApp.Services;

public sealed class UserAdministration(AppDbContext db, UserManager<User> users)
{
    public async Task<IReadOnlyList<UserListItem>> ListAsync(ClaimsPrincipal actor, CancellationToken cancellationToken = default)
    {
        await RequireAdminAsync(actor, cancellationToken);
        return await ReadUsers(db.Users.OrderBy(user => user.UserName)).TagWith("UserAdministration.List")
            .ToListAsync(cancellationToken);
    }

    public async Task<PagedResult<UserListItem>> PageAsync(ClaimsPrincipal actor, int page = 1,
        int pageSize = 30, CancellationToken ct = default)
    {
        await RequireAdminAsync(actor, ct);
        return await ReadUsers(db.Users.OrderBy(user => user.UserName).ThenBy(user => user.Id))
            .TagWith("UserAdministration.List").ToPageAsync(page, pageSize, ct);
    }

    public async Task<UserListItem?> FindAsync(ClaimsPrincipal actor, int id, CancellationToken cancellationToken = default)
    {
        await RequireAdminAsync(actor, cancellationToken);
        return await ReadUsers(db.Users.Where(user => user.Id == id)).TagWith("UserAdministration.Find")
            .SingleOrDefaultAsync(cancellationToken);
    }

    private IQueryable<UserListItem> ReadUsers(IQueryable<User> query) => query.AsNoTracking().Select(user => new UserListItem(
        user.Id, user.UserName!,
        (from membership in db.UserRoles
         join role in db.Roles on membership.RoleId equals role.Id
         where membership.UserId == user.Id
         select role.Name!).ToArray(),
        user.MustChangePassword)
    {
        CashLocationId = user.CashLocationId,
        CashLocationName = user.CashLocation != null && !user.CashLocation.IsDeleted ? user.CashLocation.Name : null
    });

    public async Task<IReadOnlyList<Lookup>> SellingLocationsAsync(ClaimsPrincipal actor, CancellationToken ct = default)
    {
        await RequireAdminAsync(actor, ct);
        return await db.StockLocations.AsNoTracking()
            .Where(l => !l.IsDeleted && l.CanSell)
            .OrderBy(l => l.Name).ThenBy(l => l.Id)
            .Select(l => new Lookup(l.Id, l.Name))
            .ToListAsync(ct);
    }

    public async Task<IReadOnlyList<RolePermissionMatrixRow>> GetRolePermissionsAsync(ClaimsPrincipal actor, CancellationToken ct = default)
    {
        await RequireAdminAsync(actor, ct);
        return await RolePermissionService.GetMatrixAsync(db, ct);
    }

    public async Task SaveRolePermissionsAsync(
        ClaimsPrincipal actor,
        IReadOnlySet<(string RoleName, string PolicyName)> grantedSet,
        CancellationToken ct = default)
    {
        var admin = await RequireAdminAsync(actor, ct);
        await RolePermissionService.SaveMatrixAsync(db, admin.Id, grantedSet, ct);
    }

    public async Task<IdentityResult> ChangeOwnPasswordAsync(
        ClaimsPrincipal actor,
        string currentPassword,
        string newPassword,
        string confirmPassword)
    {
        if (actor.Identity?.IsAuthenticated != true || !int.TryParse(users.GetUserId(actor), out var id))
        {
            return Error("Sign in to change your password.");
        }
        if (string.IsNullOrWhiteSpace(currentPassword) || string.IsNullOrWhiteSpace(newPassword))
        {
            return Error("Enter both your current password and a new password.");
        }
        if (newPassword != confirmPassword)
        {
            return Error("The new password and confirmation do not match.");
        }
        if (currentPassword == newPassword)
        {
            return Error("Choose a new password that is different from your current password.");
        }

        var user = await users.FindByIdAsync(id.ToString());
        if (user is null)
        {
            return Error("Account not found.");
        }

        var result = await users.ChangePasswordAsync(user, currentPassword, newPassword);
        if (!result.Succeeded)
        {
            return result;
        }

        user.MustChangePassword = false;
        result = await users.UpdateAsync(user);
        if (!result.Succeeded)
        {
            return result;
        }

        return await users.UpdateSecurityStampAsync(user);
    }

    public async Task<IdentityResult> SaveAsync(ClaimsPrincipal actor, int? id, string username, string? password, string role, bool mustChangePassword = false, int? cashLocationId = null)
    {
        var administrator = await RequireAdminAsync(actor);
        if (!AppRoles.All.Contains(role)) return Error("Choose a valid role.");
        username = username.Trim();
        if (username.Length is 0 or > 256) return Error("Enter a username of up to 256 characters.");
        if (id is null && string.IsNullOrWhiteSpace(password)) return Error("A password is required for a new user.");
        if (cashLocationId is not null && !await db.StockLocations.AnyAsync(l => l.Id == cashLocationId.Value && !l.IsDeleted && l.CanSell))
        {
            return Error("Choose an active selling location.");
        }
        // User details, password, and role assignment must be committed as one operation.
        await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable);
        User? user;
        IdentityResult result;
        if (id is null)
        {
            user = new User { UserName = username, MustChangePassword = mustChangePassword, CashLocationId = cashLocationId };
            result = await users.CreateAsync(user, password!);
            if (!result.Succeeded) return result;
        }
        else
        {
            user = await users.FindByIdAsync(id.Value.ToString());
            if (user is null) return Error("This user no longer exists.");
            var currentRoles = await users.GetRolesAsync(user);
            if (currentRoles.Contains(AppRoles.Admin) && role != AppRoles.Admin)
            {
                if (administrator.Id == user.Id) return Error("You cannot remove your own admin role.");
                if ((await users.GetUsersInRoleAsync(AppRoles.Admin)).Count <= 1)
                    return Error("At least one admin must remain.");
            }
            user.UserName = username;
            user.MustChangePassword = mustChangePassword;
            user.CashLocationId = cashLocationId;
            result = await users.UpdateAsync(user);
            if (!result.Succeeded) return result;
            if (!string.IsNullOrEmpty(password))
            {
                var token = await users.GeneratePasswordResetTokenAsync(user);
                result = await users.ResetPasswordAsync(user, token, password);
                if (!result.Succeeded) return result;
                result = await users.ResetAccessFailedCountAsync(user);
                if (!result.Succeeded) return result;
                result = await users.SetLockoutEndDateAsync(user, null);
                if (!result.Succeeded) return result;
            }
        }

        var oldRoles = await users.GetRolesAsync(user);
        var rolesToRemove = oldRoles.Where(existing => existing != role).ToArray();
        if (rolesToRemove.Length > 0)
        {
            result = await users.RemoveFromRolesAsync(user, rolesToRemove);
            if (!result.Succeeded) return result;
        }
        if (!oldRoles.Contains(role))
        {
            result = await users.AddToRoleAsync(user, role);
            if (!result.Succeeded) return result;
        }
        // Invalidate existing sessions after changes to credentials or permissions.
        result = await users.UpdateSecurityStampAsync(user);
        if (!result.Succeeded) return result;
        await transaction.CommitAsync();
        return IdentityResult.Success;

    }

    private async Task<User> RequireAdminAsync(ClaimsPrincipal actor, CancellationToken cancellationToken = default)
    {
        if (actor.Identity?.IsAuthenticated != true || !int.TryParse(users.GetUserId(actor), out var id))
            throw new UnauthorizedAccessException("Only an admin can manage users.");
        return await db.Users.Where(user => user.Id == id && db.UserRoles.Any(membership =>
                membership.UserId == user.Id && db.Roles.Any(role => role.Id == membership.RoleId && role.Name == AppRoles.Admin)))
            .SingleOrDefaultAsync(cancellationToken)
            ?? throw new UnauthorizedAccessException("Only an admin can manage users.");
    }

    private static IdentityResult Error(string description) => IdentityResult.Failed(new IdentityError { Description = description });
}
