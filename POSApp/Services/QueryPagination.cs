using Microsoft.EntityFrameworkCore;

namespace POSApp.Services;

public static class QueryPagination
{
    public static int Size(int requested) => requested is 10 or 30 or 50 or 100 or 250 ? requested : 30;

    public static async Task<PagedResult<T>> ToPageAsync<T>(this IQueryable<T> query,
        int page = 1, int pageSize = 30, CancellationToken ct = default)
    {
        var count = await query.CountAsync(ct);
        var size = Size(pageSize);
        var number = Math.Clamp(page, 1, Math.Max(1, (int)Math.Ceiling(count / (double)size)));
        var items = await query.Skip((number - 1) * size).Take(size).ToArrayAsync(ct);
        return new(items, new(number, size, count));
    }
}
