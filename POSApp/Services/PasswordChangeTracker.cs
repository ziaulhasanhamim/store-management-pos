using System.Collections.Concurrent;
using Microsoft.EntityFrameworkCore;
using POSApp.Persistence;

namespace POSApp.Services;

public sealed class PasswordChangeTracker
{
    private readonly ConcurrentDictionary<int, (bool MustChange, DateTimeOffset CachedAt)> _cache = new();

    public async Task<bool> MustChangePasswordAsync(int userId, AppDbContext db, CancellationToken ct = default)
    {
        var now = DateTimeOffset.UtcNow;
        if (!_cache.TryGetValue(userId, out var cached) || (now - cached.CachedAt).TotalSeconds > 60)
        {
            var mustChange = await db.Users.AsNoTracking()
                .Where(u => u.Id == userId)
                .Select(u => u.MustChangePassword)
                .FirstOrDefaultAsync(ct);
            cached = (mustChange, now);
            _cache[userId] = cached;
        }

        return cached.MustChange;
    }

    public void Invalidate(int userId) => _cache.TryRemove(userId, out _);

    public void Set(int userId, bool mustChange) => _cache[userId] = (mustChange, DateTimeOffset.UtcNow);
}
