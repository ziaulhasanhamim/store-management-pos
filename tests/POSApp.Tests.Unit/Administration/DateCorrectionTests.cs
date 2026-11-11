using System.ComponentModel.DataAnnotations;
using System.Security.Claims;
using Microsoft.Extensions.DependencyInjection;
using POSApp.Services;

namespace POSApp.Tests.Unit.Administration;

public class DateCorrectionTests
{
    [Test]
    public async Task ChangeDateAsync_ThrowsWhenReasonIsBlank()
    {
        var services = new ServiceCollection().BuildServiceProvider();
        var scopeFactory = services.GetRequiredService<IServiceScopeFactory>();
        var service = new DateCorrectionService(scopeFactory);

        var user = new ClaimsPrincipal(new ClaimsIdentity([new Claim(ClaimTypes.Role, "Admin")]));

        await Assert.ThrowsAsync<ArgumentException>(async () =>
        {
            await service.ChangeDateAsync(user, "sale", 1, DateTimeOffset.UtcNow, "   ");
        });
    }

    [Test]
    public async Task ChangeDateAsync_ThrowsWhenRecordTypeIsBlank()
    {
        var services = new ServiceCollection().BuildServiceProvider();
        var scopeFactory = services.GetRequiredService<IServiceScopeFactory>();
        var service = new DateCorrectionService(scopeFactory);

        var user = new ClaimsPrincipal(new ClaimsIdentity([new Claim(ClaimTypes.Role, "Admin")]));

        await Assert.ThrowsAsync<ArgumentException>(async () =>
        {
            await service.ChangeDateAsync(user, "", 1, DateTimeOffset.UtcNow, "Fix mistake");
        });
    }
}
