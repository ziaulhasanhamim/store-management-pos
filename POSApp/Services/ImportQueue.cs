using System.Threading.Channels;
using Microsoft.Extensions.Configuration;

namespace POSApp.Services;

public interface IImportQueue
{
    void Notify();
    Task WaitAsync(CancellationToken ct);
}

// Durable jobs live in the database. The bounded channel only carries wake-up signals.
public sealed class ImportQueue(IConfiguration? configuration = null) : IImportQueue
{
    private readonly Channel<bool> channel = Channel.CreateBounded<bool>(new BoundedChannelOptions(1)
    {
        FullMode = BoundedChannelFullMode.DropWrite,
        SingleReader = true
    });

    private readonly TimeSpan pollInterval = configuration?.GetValue<TimeSpan?>("Import:RecoveryInterval")
        ?? (configuration?.GetValue<int?>("Import:RecoveryIntervalMinutes") is { } minutes
            ? TimeSpan.FromMinutes(minutes)
            : TimeSpan.FromMinutes(15));

    public void Notify() => channel.Writer.TryWrite(true);

    public async Task WaitAsync(CancellationToken ct)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(pollInterval);
        try
        {
            await channel.Reader.ReadAsync(timeout.Token);
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            // A periodic database scan recovers missed signals and expired worker leases.
        }
    }
}

