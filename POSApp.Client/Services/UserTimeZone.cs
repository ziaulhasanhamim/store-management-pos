using System.Globalization;

namespace POSApp.Client.Services;

public sealed class UserTimeZone
{
    public TimeZoneInfo Zone { get; }
    public string TimeZoneId { get; }

    public UserTimeZone(TimeZoneInfo? zone = null, string? timeZoneId = null)
    {
        Zone = zone ?? TimeZoneInfo.Local;
        TimeZoneId = string.IsNullOrWhiteSpace(timeZoneId) ? Zone.Id : timeZoneId;
    }

    public static TimeZoneInfo ResolveZone(string? preferredTimeZoneId, string? fallbackTimeZoneId = "Asia/Dhaka")
    {
        if (!string.IsNullOrWhiteSpace(preferredTimeZoneId) &&
            TimeZoneInfo.TryFindSystemTimeZoneById(preferredTimeZoneId.Trim(), out var preferred))
        {
            return preferred;
        }

        if (!string.IsNullOrWhiteSpace(fallbackTimeZoneId) &&
            TimeZoneInfo.TryFindSystemTimeZoneById(fallbackTimeZoneId.Trim(), out var fallback))
        {
            return fallback;
        }

        return TimeZoneInfo.Utc;
    }

    public DateTimeOffset ToUserTime(DateTimeOffset utcValue)
        => TimeZoneInfo.ConvertTime(utcValue.ToUniversalTime(), Zone);

    public DateOnly Today(DateTimeOffset? utcNow = null)
        => DateOnly.FromDateTime(ToUserTime(utcNow ?? DateTimeOffset.UtcNow).DateTime);

    public DateTimeOffset StartOfDayUtc(DateOnly date)
    {
        var unspecified = date.ToDateTime(TimeOnly.MinValue, DateTimeKind.Unspecified);
        return new DateTimeOffset(TimeZoneInfo.ConvertTimeToUtc(unspecified, Zone), TimeSpan.Zero);
    }

    public string Format(DateTimeOffset utcValue, string format = "dd MMM yyyy, hh:mm tt")
        => ToUserTime(utcValue).ToString(format, CultureInfo.InvariantCulture);

    public string FormatNullable(DateTimeOffset? utcValue, string format = "dd MMM yyyy, hh:mm tt", string empty = "-")
        => utcValue is { } v ? Format(v, format) : empty;
}
