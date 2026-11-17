using Microsoft.AspNetCore.Identity;

namespace POSApp.Security;

public static class IdentityValidation
{
    public static IServiceCollection AddPosIdentityValidation(this IServiceCollection services)
    {
        // Periodic cookie validation refreshes identity claims. Authorization handlers
        // and services separately verify current database role membership.
        services.Configure<SecurityStampValidatorOptions>(options =>
        {
            options.ValidationInterval = TimeSpan.FromMinutes(5);
        });

        return services;
    }
}
