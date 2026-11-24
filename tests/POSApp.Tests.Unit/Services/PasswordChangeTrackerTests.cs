using Microsoft.EntityFrameworkCore;
using POSApp.Persistence;
using POSApp.Persistence.Entities;
using POSApp.Services;

namespace POSApp.Tests.Unit.Services;

public class PasswordChangeTrackerTests
{
    private static AppDbContext CreateDb(string name)
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(name)
            .Options;
        return new AppDbContext(options);
    }

    [Test]
    public async Task MustChangePasswordAsync_QueriesDbAndCachesResult()
    {
        var dbName = Guid.NewGuid().ToString();
        using (var db = CreateDb(dbName))
        {
            db.Users.Add(new User { Id = 101, UserName = "testuser", MustChangePassword = true });
            await db.SaveChangesAsync();
        }

        var tracker = new PasswordChangeTracker();
        using (var db = CreateDb(dbName))
        {
            var mustChange = await tracker.MustChangePasswordAsync(101, db);
            await mustChange.Should().BeTrue();
        }

        // Change DB directly without updating tracker
        using (var db = CreateDb(dbName))
        {
            var user = await db.Users.FindAsync(101);
            user!.MustChangePassword = false;
            await db.SaveChangesAsync();
        }

        // Tracker still serves cached 'true'
        using (var db = CreateDb(dbName))
        {
            var mustChange = await tracker.MustChangePasswordAsync(101, db);
            await mustChange.Should().BeTrue();
        }

        // When Set(101, false) is called, returns false immediately
        tracker.Set(101, false);
        using (var db = CreateDb(dbName))
        {
            var mustChange = await tracker.MustChangePasswordAsync(101, db);
            await mustChange.Should().BeFalse();
        }
    }

    [Test]
    public async Task Invalidate_ClearsCacheAndForcesDbRefresh()
    {
        var dbName = Guid.NewGuid().ToString();
        using (var db = CreateDb(dbName))
        {
            db.Users.Add(new User { Id = 102, UserName = "user2", MustChangePassword = true });
            await db.SaveChangesAsync();
        }

        var tracker = new PasswordChangeTracker();
        using (var db = CreateDb(dbName))
        {
            var mustChange = await tracker.MustChangePasswordAsync(102, db);
            await mustChange.Should().BeTrue();
        }

        // Update in DB
        using (var db = CreateDb(dbName))
        {
            var user = await db.Users.FindAsync(102);
            user!.MustChangePassword = false;
            await db.SaveChangesAsync();
        }

        tracker.Invalidate(102);

        using (var db = CreateDb(dbName))
        {
            var mustChange = await tracker.MustChangePasswordAsync(102, db);
            await mustChange.Should().BeFalse();
        }
    }
}
