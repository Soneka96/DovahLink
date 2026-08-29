namespace DovahLink.Host.Process;

/// <summary>Runs the headless host until its owner requests process shutdown.</summary>
public interface IHostProcessLifetime
{
    /// <summary>Waits for the host lifetime to end.</summary>
    /// <param name="cancellationToken">The token used to request host shutdown.</param>
    Task RunAsync(CancellationToken cancellationToken = default);
}

/// <inheritdoc cref="IHostProcessLifetime"/>
public sealed class HostProcessLifetime : IHostProcessLifetime
{
    /// <inheritdoc/>
    public async Task RunAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
    }
}
