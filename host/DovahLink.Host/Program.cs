using DovahLink.Host.Process;

/// <summary>Composes and runs the headless DovahLink host process.</summary>
internal static class Program
{
    /// <summary>Starts the host and keeps it alive until the process is asked to exit.</summary>
    /// <returns>A successful process exit code.</returns>
    private static async Task<int> Main()
    {
        using var shutdown = new CancellationTokenSource();
        EventHandler processExitHandler = (_, _) => shutdown.Cancel();
        AppDomain.CurrentDomain.ProcessExit += processExitHandler;

        try
        {
            return await RunAsync(new HostProcessLifetime(), shutdown.Token);
        }
        finally
        {
            AppDomain.CurrentDomain.ProcessExit -= processExitHandler;
        }
    }

    /// <summary>Runs an injected host lifetime and maps clean shutdown to a successful exit code.</summary>
    /// <param name="lifetime">The host lifetime to run.</param>
    /// <param name="cancellationToken">The token used to request shutdown.</param>
    /// <returns>A successful process exit code after the lifetime ends.</returns>
    internal static async Task<int> RunAsync(IHostProcessLifetime lifetime, CancellationToken cancellationToken)
    {
        await lifetime.RunAsync(cancellationToken);
        return 0;
    }
}
