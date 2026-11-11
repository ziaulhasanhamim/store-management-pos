using System.ComponentModel.DataAnnotations;
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

    public static Task EnsureOpenAsync(AppDbContext db, DateOnly date, CancellationToken ct, bool requireDayOpened = true)
    {
        // Continuous timeline: no day/month locking.
        return Task.CompletedTask;
    }
}
