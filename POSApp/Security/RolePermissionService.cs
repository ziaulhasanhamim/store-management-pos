using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;
using POSApp.Client.DTOs;
using POSApp.Persistence;
using POSApp.Persistence.Entities;

namespace POSApp.Security;

public sealed record DynamicPolicyRequirement(string PolicyName) : IAuthorizationRequirement;

public sealed class DynamicPolicyAuthorizationHandler(IServiceScopeFactory scopes)
    : AuthorizationHandler<DynamicPolicyRequirement>
{
    protected override async Task HandleRequirementAsync(
        AuthorizationHandlerContext context,
        DynamicPolicyRequirement requirement)
    {
        if (context.User.Identity?.IsAuthenticated != true)
        {
            return;
        }

        if (context.User.IsInRole(AppRoles.Admin))
        {
            context.Succeed(requirement);
            return;
        }

        if (requirement.PolicyName == AppPolicies.ManageUsers)
        {
            return;
        }

        // 1. Fast path: check user roles from Claims without opening a database scope
        var claimRoles = context.User.FindAll(ClaimTypes.Role).Select(c => c.Value)
            .Concat(context.User.FindAll("role").Select(c => c.Value))
            .Distinct(StringComparer.Ordinal)
            .ToArray();

        if (claimRoles.Length > 0)
        {
            if (RolePermissionService.TryCheckCached(claimRoles, requirement.PolicyName, out var cachedGranted))
            {
                if (cachedGranted)
                {
                    context.Succeed(requirement);
                }
                return;
            }

            await using var scope = scopes.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            if (await RolePermissionService.HasPolicyForRolesAsync(db, claimRoles, requirement.PolicyName, CancellationToken.None))
            {
                context.Succeed(requirement);
            }
            return;
        }

        // 2. Fallback only if user has no role claims
        if (int.TryParse(context.User.FindFirstValue(ClaimTypes.NameIdentifier), out var userId))
        {
            await using var scope = scopes.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            if (await RolePermissionService.HasPolicyAsync(db, userId, requirement.PolicyName, CancellationToken.None))
            {
                context.Succeed(requirement);
            }
        }
    }
}

public static class RolePermissionService
{
    public static readonly (string PolicyName, string Title, string Description)[] ConfigurablePolicies =
    [
        (AppPolicies.ViewCostPrice, "View cost prices & margins", "Allow seeing item cost prices, stock valuation, and profit margins."),
        (AppPolicies.MakeSales, "Make sales & customer checkout", "Allow using the Point of Sale and billing customers."),
        (AppPolicies.ManageDrawer, "Open & count cash drawer", "Allow opening, counting, and closing location cash drawer shifts."),
        (AppPolicies.ManageInventory, "Manage items, stock & receiving", "Allow adding/editing items, receiving shipments, and managing inventory."),
        (AppPolicies.ManageStockAdjustment, "Stock additions & write-offs", "Allow recording direct stock additions and damaged/expired stock write-offs."),
        (AppPolicies.RecordExpense, "Record store expenses", "Allow recording operational store expenses and payouts."),
        (AppPolicies.ManagePayroll, "Manage payroll & staff accounts", "Allow viewing staff balances and recording salary/bonus advances."),
        (AppPolicies.ManageParties, "Manage customers & suppliers", "Allow creating, updating, and managing customer and supplier directories."),
        (AppPolicies.ApplyDiscount, "Apply sales discounts", "Allow applying line-item or invoice discounts during checkout."),
        (AppPolicies.VoidSale, "Void or edit completed sales", "Allow modifying or voiding completed sales transactions."),
        (AppPolicies.ViewFinancialReports, "View financial reports", "Allow viewing profit & loss statements and period closing summaries.")
    ];

    public static readonly string[] ConfigurableRoles =
    [
        AppRoles.InventoryManager,
        AppRoles.SalesAssociate,
        AppRoles.InventoryViewer
    ];

    public static bool GetDefaultPermission(string roleName, string policyName) => (roleName, policyName) switch
    {
        (AppRoles.Admin, _) => true,
        (_, AppPolicies.ManageUsers) => false,
        (AppRoles.InventoryManager, AppPolicies.ManageInventory or AppPolicies.ManageDrawer or AppPolicies.MakeSales or AppPolicies.ViewCostPrice or AppPolicies.RecordExpense or AppPolicies.ManageParties or AppPolicies.ApplyDiscount or AppPolicies.ViewFinancialReports) => true,
        (AppRoles.SalesAssociate, AppPolicies.MakeSales or AppPolicies.ViewCostPrice or AppPolicies.ApplyDiscount) => true,
        (AppRoles.InventoryViewer, AppPolicies.ViewCostPrice or AppPolicies.ViewFinancialReports) => true,
        _ => false
    };

    public static string MapRoleOrPolicyToPolicy(string roleOrPolicy) => roleOrPolicy switch
    {
        AppRoles.Admin or AppPolicies.ManageUsers => AppPolicies.ManageUsers,
        AppPolicies.ManageStockAdjustment => AppPolicies.ManageStockAdjustment,
        AppRoles.InventoryManager or AppPolicies.ManageInventory => AppPolicies.ManageInventory,
        AppPolicies.ManageDrawer => AppPolicies.ManageDrawer,
        AppRoles.SalesAssociate or AppPolicies.MakeSales => AppPolicies.MakeSales,
        AppRoles.InventoryViewer or AppPolicies.ViewCostPrice => AppPolicies.ViewCostPrice,
        _ => roleOrPolicy
    };

    public static async Task<bool> HasPolicyAsync(AppDbContext db, int userId, string roleOrPolicy, CancellationToken ct = default)
    {
        var userRoles = await db.UserRoles
            .Where(ur => ur.UserId == userId)
            .Join(db.Roles, ur => ur.RoleId, r => r.Id, (ur, r) => r.Name!)
            .Where(name => name != null)
            .ToArrayAsync(ct);

        if (userRoles.Length == 0)
        {
            return false;
        }

        return await HasPolicyForRolesAsync(db, userRoles, roleOrPolicy, ct);
    }

    private static volatile IReadOnlyDictionary<(string RoleName, string PolicyName), bool>? _cachedOverrides;
    private static DateTimeOffset _cacheExpiresAt = DateTimeOffset.MinValue;
    private static readonly SemaphoreSlim _cacheLock = new(1, 1);

    public static void InvalidateCache()
    {
        _cachedOverrides = null;
        _cacheExpiresAt = DateTimeOffset.MinValue;
    }

    public static void SetCache(IReadOnlyDictionary<(string RoleName, string PolicyName), bool> overrides, TimeSpan? ttl = null)
    {
        _cachedOverrides = overrides;
        _cacheExpiresAt = DateTimeOffset.UtcNow.Add(ttl ?? TimeSpan.FromMinutes(5));
    }

    public static bool TryCheckCached(IReadOnlyCollection<string> roles, string roleOrPolicy, out bool granted)
    {
        if (roles.Contains(AppRoles.Admin))
        {
            granted = true;
            return true;
        }

        var policyName = MapRoleOrPolicyToPolicy(roleOrPolicy);
        if (policyName == AppPolicies.ManageUsers)
        {
            granted = false;
            return true;
        }

        var local = _cachedOverrides;
        if (local is not null && DateTimeOffset.UtcNow < _cacheExpiresAt)
        {
            foreach (var role in roles)
            {
                if (local.TryGetValue((role, policyName), out var isGranted))
                {
                    if (isGranted)
                    {
                        granted = true;
                        return true;
                    }
                }
                else if (GetDefaultPermission(role, policyName))
                {
                    granted = true;
                    return true;
                }
            }
            granted = false;
            return true;
        }

        granted = false;
        return false;
    }

    public static async Task<bool> HasPolicyForRolesAsync(
        AppDbContext db,
        IReadOnlyCollection<string> roles,
        string roleOrPolicy,
        CancellationToken ct = default)
    {
        if (roles.Contains(AppRoles.Admin))
        {
            return true;
        }

        var policyName = MapRoleOrPolicyToPolicy(roleOrPolicy);
        if (policyName == AppPolicies.ManageUsers)
        {
            return false;
        }

        var local = _cachedOverrides;
        if (local is null || DateTimeOffset.UtcNow >= _cacheExpiresAt)
        {
            await _cacheLock.WaitAsync(ct);
            try
            {
                if (_cachedOverrides is null || DateTimeOffset.UtcNow >= _cacheExpiresAt)
                {
                    _cachedOverrides = await db.RolePermissions.AsNoTracking()
                        .ToDictionaryAsync(rp => (rp.RoleName, rp.PolicyName), rp => rp.IsGranted, ct);
                    _cacheExpiresAt = DateTimeOffset.UtcNow.AddMinutes(5);
                }
                local = _cachedOverrides;
            }
            finally
            {
                _cacheLock.Release();
            }
        }

        foreach (var role in roles)
        {
            if (local.TryGetValue((role, policyName), out var granted))
            {
                if (granted)
                {
                    return true;
                }
            }
            else if (GetDefaultPermission(role, policyName))
            {
                return true;
            }
        }

        return false;
    }

    public static async Task<IReadOnlyList<RolePermissionMatrixRow>> GetMatrixAsync(AppDbContext db, CancellationToken ct = default)
    {
        var saved = await db.RolePermissions.AsNoTracking()
            .ToDictionaryAsync(rp => (rp.RoleName, rp.PolicyName), rp => rp.IsGranted, ct);

        bool IsGranted(string role, string policy) =>
            saved.TryGetValue((role, policy), out var val) ? val : GetDefaultPermission(role, policy);

        return ConfigurablePolicies
            .Select(p => new RolePermissionMatrixRow(
                p.PolicyName,
                p.Title,
                p.Description,
                IsGranted(AppRoles.InventoryManager, p.PolicyName),
                IsGranted(AppRoles.SalesAssociate, p.PolicyName),
                IsGranted(AppRoles.InventoryViewer, p.PolicyName)))
            .ToArray();
    }

    public static async Task SaveMatrixAsync(
        AppDbContext db,
        int actorUserId,
        IReadOnlySet<(string RoleName, string PolicyName)> grantedSet,
        CancellationToken ct = default)
    {
        var existing = await db.RolePermissions.ToListAsync(ct);
        var byKey = existing.ToDictionary(rp => (rp.RoleName, rp.PolicyName));
        var now = DateTimeOffset.UtcNow;

        foreach (var role in ConfigurableRoles)
        {
            foreach (var (policy, _, _) in ConfigurablePolicies)
            {
                var isGranted = grantedSet.Contains((role, policy));
                if (byKey.TryGetValue((role, policy), out var row))
                {
                    if (row.IsGranted != isGranted)
                    {
                        row.IsGranted = isGranted;
                        row.UpdatedAt = now;
                        row.UpdatedByUserId = actorUserId;
                    }
                }
                else
                {
                    db.RolePermissions.Add(new RolePermission
                    {
                        RoleName = role,
                        PolicyName = policy,
                        IsGranted = isGranted,
                        CreatedAt = now,
                        CreatedByUserId = actorUserId
                    });
                }
            }
        }

        await db.SaveChangesAsync(ct);
        InvalidateCache();
    }
}
