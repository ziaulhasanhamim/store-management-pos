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

    [Test]
    public async Task ToBusinessTime_ConvertsUtcToDhakaTime()
    {
        // 04:00 UTC is 10:00 AM in Asia/Dhaka (+6)
        var utc = new DateTimeOffset(2026, 11, 11, 4, 0, 0, TimeSpan.Zero);
        var businessTime = utc.ToBusinessTime(null);

        await Assert.That(businessTime.Offset).IsEqualTo(TimeSpan.FromHours(6));
        await Assert.That(businessTime.Hour).IsEqualTo(10);
        await Assert.That(businessTime.Day).IsEqualTo(11);
    }

    [Test]
    public async Task DateCorrectionCandidate_StoresExpectedValues()
    {
        var now = DateTimeOffset.UtcNow;
        var today = DateOnly.FromDateTime(now.DateTime);
        var candidate = new DateCorrectionCandidate(
            104,
            "debt",
            "Debt (Borrowing) · Prime Lender",
            now,
            today,
            50000m,
            "Debt #104",
            "Cash borrowing"
        );

        await Assert.That(candidate.Id).IsEqualTo(104);
        await Assert.That(candidate.RecordType).IsEqualTo("debt");
        await Assert.That(candidate.Amount).IsEqualTo(50000m);
        await Assert.That(candidate.Reference).IsEqualTo("Debt #104");
    }
}
