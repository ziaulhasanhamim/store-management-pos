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
        var fallbackId = configuration["BusinessTimeZone"] ?? "Asia/Dhaka";
        var preferredId = httpContext?.User?.FindFirst("tz")?.Value
            ?? (httpContext?.Request.Cookies.TryGetValue("pos_tz", out var cookieTz) == true ? Uri.UnescapeDataString(cookieTz) : null);
        var zone = UserTimeZone.ResolveZone(preferredId, fallbackId);
        return new UserTimeZone(zone, zone.Id);
    }

    public static DateTimeOffset ToBusinessTime(this DateTimeOffset value, IConfiguration configuration)
    {
        var zone = CurrentUserZone.Value
            ?? UserTimeZone.ResolveZone(null, configuration["BusinessTimeZone"] ?? "Asia/Dhaka");
        return TimeZoneInfo.ConvertTime(value.ToUniversalTime(), zone);
    }
}
