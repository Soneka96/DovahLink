using System.Diagnostics;

namespace DovahLink.Host.Tests.PairingCeremony;

/// <summary>
/// Repeatedly schedules short asynchronous delays on the thread pool and records how late each one
/// resumes, standing for ordinary asynchronous Host work that must keep running while the dormant
/// integration drives on its own thread.
/// </summary>
internal sealed class ResponsivenessProbe : IDisposable
{
    /// <summary>The delay each probe iteration requests.</summary>
    private static readonly TimeSpan Delay = TimeSpan.FromMilliseconds(5);

    /// <summary>Stops the probe loop.</summary>
    private readonly CancellationTokenSource stop = new();

    /// <summary>The probe loop, completing with the worst lateness seen.</summary>
    private readonly Task<TimeSpan> loop;

    /// <summary>Starts probing.</summary>
    public ResponsivenessProbe()
    {
        loop = Task.Run(async () =>
        {
            TimeSpan worst = TimeSpan.Zero;
            while (!stop.IsCancellationRequested)
            {
                var stopwatch = Stopwatch.StartNew();
                await Task.Delay(Delay);
                TimeSpan lateness = stopwatch.Elapsed - Delay;
                worst = lateness > worst ? lateness : worst;
            }

            return worst;
        });
    }

    /// <summary>Stops probing and returns the worst lateness of any iteration.</summary>
    /// <returns>The worst lateness.</returns>
    public TimeSpan Stop()
    {
        stop.Cancel();
        return loop.GetAwaiter().GetResult();
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        stop.Cancel();
        stop.Dispose();
    }
}
