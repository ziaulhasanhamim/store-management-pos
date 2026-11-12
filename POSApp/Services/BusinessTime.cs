using POSApp.Client.Services;

namespace POSApp.Services;

public static class BusinessTime
{
    private static readonly AsyncLocal<TimeZoneInfo?> CurrentUserZone = new();

    public static TimeZoneInfo? AmbientZone
    {
        get => CurrentUserZone.Value;
        set => CurrentUserZone.Value = value;
    }

    public static UserTimeZone ResolveUserTimeZone(HttpContext? httpContext, IConfiguration configuration)
    {
        var businessZoneId = configuration["BusinessTimeZone"] ?? "Asia/Dhaka";
        var userClaimTz = httpContext?.User?.FindFirst("tz")?.Value;
        var cookieTz = httpContext?.Request.Cookies.TryGetValue("pos_tz", out var c) == true ? Uri.UnescapeDataString(c) : null;

        // Explicit user claim takes priority. Ignore generic UTC cookie when a business zone is configured.
        string? effectiveId = userClaimTz;
        if (string.IsNullOrWhiteSpace(effectiveId) && !string.IsNullOrWhiteSpace(cookieTz) && !string.Equals(cookieTz, "UTC", StringComparison.OrdinalIgnoreCase))
        {
            effectiveId = cookieTz;
        }
        if (string.IsNullOrWhiteSpace(effectiveId))
        {
            effectiveId = businessZoneId;
        }

        var zone = UserTimeZone.ResolveZone(effectiveId, businessZoneId);
        return new UserTimeZone(zone, zone.Id);
    }

    public static DateTimeOffset ToBusinessTime(this DateTimeOffset value, IConfiguration? configuration)
    {
        var zone = AccountingCalendar.Zone(configuration);
        return TimeZoneInfo.ConvertTime(value.ToUniversalTime(), zone);
    }

    /// <summary>
    /// Interprets a wall-clock date & time entered by the user in the business timezone (e.g. from an HTML datetime-local picker)
    /// and accurately converts it to a canonical UTC DateTimeOffset.
    /// </summary>
    public static DateTimeOffset FromBusinessWallClockToUtc(DateTime wallClock, IConfiguration? configuration)
    {
        var zone = AccountingCalendar.Zone(configuration);
        var unspecified = DateTime.SpecifyKind(wallClock, DateTimeKind.Unspecified);
        var utc = TimeZoneInfo.ConvertTimeToUtc(unspecified, zone);
        return new DateTimeOffset(utc, TimeSpan.Zero);
    }

    /// <summary>
    /// Interprets a DateTimeOffset whose raw DateTime represents business wall-clock time and converts it to canonical UTC.
    /// </summary>
    public static DateTimeOffset FromBusinessWallClockToUtc(DateTimeOffset rawInput, IConfiguration? configuration)
    {
        return FromBusinessWallClockToUtc(rawInput.DateTime, configuration);
    }
}
