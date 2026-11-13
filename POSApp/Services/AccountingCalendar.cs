using System.ComponentModel.DataAnnotations;
using Microsoft.EntityFrameworkCore;
using POSApp.Client.Services;
using POSApp.Persistence;

namespace POSApp.Services;

public static class AccountingCalendar
{
    public static TimeZoneInfo Zone(IConfiguration? configuration) =>
        UserTimeZone.ResolveZone(configuration?["BusinessTimeZone"], "Asia/Dhaka");

    public static DateOnly Today(IConfiguration? configuration) =>
        DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(DateTimeOffset.UtcNow, Zone(configuration)).DateTime);

    public static DateTimeOffset Now(IConfiguration? configuration) =>
        TimeZoneInfo.ConvertTime(DateTimeOffset.UtcNow, Zone(configuration));

    public static DateTimeOffset At(DateOnly date, IConfiguration? configuration) =>
        At(date, null, configuration);

    public static DateTimeOffset At(DateOnly date, TimeOnly? time, IConfiguration? configuration)
    {
        var zone = Zone(configuration);
        var effectiveTime = time ?? TimeOnly.FromDateTime(TimeZoneInfo.ConvertTime(DateTimeOffset.UtcNow, zone).DateTime);
        var local = date.ToDateTime(effectiveTime, DateTimeKind.Unspecified);
        return new DateTimeOffset(TimeZoneInfo.ConvertTimeToUtc(local, zone), TimeSpan.Zero);
    }

    public static void ValidateExpected(DateOnly current, DateOnly? expected)
    {
        // Continuous timeline: no working day change conflicts.
    }

    public static Task<DateOnly> SelectedAsync(AppDbContext db, int actorId, IConfiguration? configuration, CancellationToken ct)
    {
        // Continuous timeline: today is always the active date.
        return Task.FromResult(Today(configuration));
    }

    public static async Task EnsureOpenAsync(AppDbContext db, DateOnly date, CancellationToken ct, bool requireDayOpened = true, int? locationId = null)
    {
        var isClosed = await db.NonTradingDays.AsNoTracking().AnyAsync(d =>
            d.Date == date && (d.StockLocationId == null || locationId == null || d.StockLocationId == locationId.Value), ct);
        if (isClosed)
        {
            throw new ValidationException($"{date:dd MMM yyyy} is marked as a closed (non-trading) day. Reopen it in Administration > Trading Calendar before recording transactions.");
        }
    }
}
