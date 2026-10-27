using Microsoft.AspNetCore.WebUtilities;

namespace POSApp.Services;

public static class ReportLinks
{
    public static string Create(string path, string? period, DateOnly? start, DateOnly? end,
        string? search = null, string? sort = null, int page = 1, int? locationId = null, int pageSize = 30)
    {
        return QueryHelpers.AddQueryString(path, new Dictionary<string, string?>
        {
            ["period"] = period ?? "today",
            ["start"] = start?.ToString("yyyy-MM-dd"),
            ["end"] = end?.ToString("yyyy-MM-dd"),
            ["search"] = search,
            ["sort"] = sort,
            ["page"] = page.ToString(),
            ["locationId"] = locationId?.ToString(),
            ["pageSize"] = pageSize.ToString()
        });
    }
}
