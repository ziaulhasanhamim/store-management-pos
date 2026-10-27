namespace POSApp.Services;

public static class BusinessTime
{
    public static DateTimeOffset ToBusinessTime(this DateTimeOffset value, IConfiguration configuration)
    {
        var zone = TimeZoneInfo.FindSystemTimeZoneById(configuration["BusinessTimeZone"] ?? "Asia/Dhaka");
        return TimeZoneInfo.ConvertTime(value, zone);
    }
}
