using System.Diagnostics;
using Microsoft.Extensions.Configuration;
using POSApp.Services;

namespace POSApp.Tests.Unit.Services;

public class ImportQueueTests
{
    [Test]
    public async Task WaitAsync_WhenNotified_CompletesImmediately()
    {
        var queue = new ImportQueue();
        queue.Notify();

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(2));
        var sw = Stopwatch.StartNew();
        await queue.WaitAsync(cts.Token);
        sw.Stop();

        await sw.ElapsedMilliseconds.Should().BeLessThan(1000);
    }

    [Test]
    public async Task WaitAsync_WhenNotifiedConcurrently_CompletesPromptly()
    {
        var queue = new ImportQueue();
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(2));

        var waitTask = queue.WaitAsync(cts.Token);
        await Task.Delay(50);
        queue.Notify();

        await waitTask;
        await waitTask.IsCompletedSuccessfully.Should().BeTrue();
    }

    [Test]
    public async Task WaitAsync_WhenCanceled_ThrowsOperationCanceledException()
    {
        var queue = new ImportQueue();
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        await Assert.ThrowsAsync<OperationCanceledException>(async () =>
        {
            await queue.WaitAsync(cts.Token);
        });
    }

    [Test]
    public async Task WaitAsync_WithConfiguredFallbackInterval_TimesOutCleanly()
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Import:RecoveryInterval"] = "00:00:00.100"
            })
            .Build();

        var queue = new ImportQueue(config);
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(2));

        var sw = Stopwatch.StartNew();
        await queue.WaitAsync(cts.Token);
        sw.Stop();

        // Should time out cleanly in around 100ms without throwing
        await sw.ElapsedMilliseconds.Should().BeGreaterThanOrEqualTo(80);
        await sw.ElapsedMilliseconds.Should().BeLessThan(1000);
    }
}
