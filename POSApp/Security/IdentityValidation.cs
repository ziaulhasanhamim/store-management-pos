using Microsoft.AspNetCore.Identity;

namespace POSApp.Security;

public static class IdentityValidation
{
    public static IServiceCollection AddPosIdentityValidation(this IServiceCollection services)
    {
        // Zero forces a user/role claims rebuild on every authenticated HTTP request,
        // including SignalR polling. Services still check current roles per operation.
        services.Configure<SecurityStampValidatorOptions>(options =>
        {
            options.ValidationInterval = TimeSpan.FromMinutes(5);
        });

        return services;
    }
}
