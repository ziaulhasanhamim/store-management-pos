using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using POSApp.Persistence;
using POSApp.Persistence.Entities;

namespace POSApp.Security;

public static class AdminSeeder
{
    public static async Task SeedAsync(IServiceProvider services, IConfiguration configuration)
    {
        if (!configuration.GetValue("AdminUser:Enabled", true)) return;

        var username = configuration["AdminUser:Username"]?.Trim();
        if (string.IsNullOrWhiteSpace(username))
            throw new InvalidOperationException("Configure AdminUser:Username before starting the app.");

        await using var scope = services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<User>>();
        var roles = scope.ServiceProvider.GetRequiredService<RoleManager<IdentityRole<int>>>();
        // All seed changes succeed or roll back together. Schema provisioning remains explicit.
        await using var transaction = await db.Database.BeginTransactionAsync();
        foreach (var role in AppRoles.All)
        {
            if (!await roles.RoleExistsAsync(role))
                EnsureSuccess(await roles.CreateAsync(new IdentityRole<int>(role)));
        }

        var user = await users.FindByNameAsync(username);
        if (user is null)
        {
            // Initial provisioning only: don't recreate a bootstrap account after an admin renames it.
            if ((await users.GetUsersInRoleAsync(AppRoles.Admin)).Count > 0)
            {
                await transaction.CommitAsync();
                return;
            }
            var password = configuration["AdminUser:Password"];
            if (string.IsNullOrWhiteSpace(password))
                throw new InvalidOperationException("Configure AdminUser:Password with user secrets or AdminUser__Password for the initial admin.");
            user = new User { UserName = username };
            EnsureSuccess(await users.CreateAsync(user, password));
            EnsureSuccess(await users.AddToRoleAsync(user, AppRoles.Admin));
        }
        else if (!await users.IsInRoleAsync(user, AppRoles.Admin))
        {
            // Changing bootstrap configuration must not promote an existing non-admin account.
            throw new InvalidOperationException("The configured admin username belongs to a non-admin user. Use an administrator account or disable AdminUser:Enabled after initial setup.");
        }

        // Ensure at least one active selling location exists for day 1 retail operations
        var defaultLocation = await db.StockLocations.FirstOrDefaultAsync(l => !l.IsDeleted && l.CanSell);
        if (defaultLocation is null)
        {
            defaultLocation = new StockLocation
            {
                Code = "MAIN",
                Name = "Main Store",
                NormalizedName = NameNormalization.Normalize("Main Store"),
                CanSell = true
            };
            db.StockLocations.Add(defaultLocation);
            await db.SaveChangesAsync();
        }

        // Ensure default cash drawer is linked to the primary selling location
        var defaultDrawer = await db.CashDrawers.FirstOrDefaultAsync(d => d.StockLocationId == defaultLocation.Id);
        if (defaultDrawer is null)
        {
            defaultDrawer = new CashDrawer
            {
                StockLocationId = defaultLocation.Id,
                OpeningBalance = 0m,
                CurrentCash = 0m
            };
            db.CashDrawers.Add(defaultDrawer);
            await db.SaveChangesAsync();
        }

        // Assign user to default cash location if not set
        if (user is not null && user.CashLocationId == null)
        {
            user.CashLocationId = defaultLocation.Id;
            await users.UpdateAsync(user);
        }

        await transaction.CommitAsync();
    }

    private static void EnsureSuccess(IdentityResult result)
    {
        if (!result.Succeeded)
            throw new InvalidOperationException(string.Join(" ", result.Errors.Select(error => error.Description)));
    }
}
