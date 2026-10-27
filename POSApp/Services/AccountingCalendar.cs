using System.ComponentModel.DataAnnotations;
using Microsoft.EntityFrameworkCore;
using POSApp.Persistence;

namespace POSApp.Services;

public static class AccountingCalendar
{
    public static TimeZoneInfo Zone(IConfiguration? configuration) =>
        TimeZoneInfo.FindSystemTimeZoneById(configuration?["BusinessTimeZone"] ?? "Asia/Dhaka");

    public static DateOnly Today(IConfiguration? configuration) =>
        DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(DateTimeOffset.UtcNow, Zone(configuration)).DateTime);

    public static DateTimeOffset At(DateOnly date, IConfiguration? configuration)
    {
        var localTime = TimeOnly.FromDateTime(TimeZoneInfo.ConvertTime(DateTimeOffset.UtcNow, Zone(configuration)).DateTime);
        var local = date.ToDateTime(localTime, DateTimeKind.Unspecified);
        return new DateTimeOffset(TimeZoneInfo.ConvertTimeToUtc(local, Zone(configuration)), TimeSpan.Zero);
    }

    public static void ValidateExpected(DateOnly current, DateOnly? expected)
    {
        if (expected is { } date && date != current)
        {
            throw new ValidationException($"The working day changed from {date:dd MMM yyyy} to {current:dd MMM yyyy}. Your entries are kept. Refresh and review the working day before submitting.");
        }
    }

    public static async Task<DateOnly> SelectedAsync(AppDbContext db, int actorId, IConfiguration? configuration, CancellationToken ct)
    {
        var date = await db.WorkingDays.Where(selection => selection.Id == 1)
            .Select(selection => (DateOnly?)selection.Date).SingleOrDefaultAsync(ct) ?? Today(configuration);
        return date;
    }

    public static async Task EnsureOpenAsync(AppDbContext db, DateOnly date, CancellationToken ct)
    {
        var month = new DateOnly(date.Year, date.Month, 1);
        if (await db.FinanceClosings.AnyAsync(closing => closing.ClosedAt != null
            && ((closing.Kind == ClosingKind.Day && closing.Date == date) || (closing.Kind == ClosingKind.Month && closing.Date == month)), ct))
        {
            throw new ValidationException("This accounting day or month is closed. Select an open accounting date.");
        }
    }
}
