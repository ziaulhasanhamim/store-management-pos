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
}
