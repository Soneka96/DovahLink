using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using DovahLink.DovahLinkBuilder;

namespace DovahLink.DovahLinkBuilder.Build;

/// <summary>Runs structured external commands and forwards their output.</summary>
public interface ICommandRunner
{
    /// <summary>Executes a structured command.</summary>
    /// <param name="command">The executable, arguments, working directory, and environment to apply.</param>
    /// <param name="onStandardOutput">The callback invoked for each standard-output line, or <see langword="null"/>.</param>
    /// <param name="onStandardError">The callback invoked for each standard-error line, or <see langword="null"/>.</param>
    /// <param name="cancellationToken">The token used to cancel command execution.</param>
    /// <returns>The process exit code.</returns>
    Task<int> RunAsync(
        BuildCommand command,
        Action<string>? onStandardOutput,
        Action<string>? onStandardError,
        CancellationToken cancellationToken = default);
}

/// <summary>Runs structured commands as direct child processes.</summary>
public sealed class ProcessCommandRunner : ICommandRunner
{
    /// <summary>The delay between successive <see cref="IProcessTreeJob.HasActiveProcesses"/> polls.</summary>
    private static readonly TimeSpan ProcessTreePollInterval = TimeSpan.FromMilliseconds(50);

    /// <summary>Terminates a process tree after cancellation.</summary>
    private readonly Action<Process> terminateProcess;

    /// <summary>Creates the Job Object used to track a command's complete process tree.</summary>
    private readonly Func<IProcessTreeJob> createJob;

    /// <summary>Creates a runner that terminates the complete child process tree.</summary>
    public ProcessCommandRunner()
        : this(process => process.Kill(entireProcessTree: true))
    {
    }

    /// <summary>Creates a runner with a controllable process-termination seam and, optionally, a controllable process-tree-tracking seam.</summary>
    /// <param name="terminateProcess">The action used to terminate a running child process.</param>
    /// <param name="createJob">
    /// Creates the Job Object used to track a command's complete process tree, or
    /// <see langword="null"/> to use a real <see cref="ProcessTreeJob"/>.
    /// </param>
    internal ProcessCommandRunner(Action<Process> terminateProcess, Func<IProcessTreeJob>? createJob = null)
    {
        this.terminateProcess = terminateProcess;
        this.createJob = createJob ?? (() => new ProcessTreeJob());
    }

    /// <inheritdoc/>
    public async Task<int> RunAsync(
        BuildCommand command,
        Action<string>? onStandardOutput,
        Action<string>? onStandardError,
        CancellationToken cancellationToken = default)
    {
        using var process = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = command.ExecutablePath,
                WorkingDirectory = command.WorkingDirectory,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
            },
        };
        foreach (string argument in command.Arguments)
        {
            process.StartInfo.ArgumentList.Add(argument);
        }
        foreach ((string key, string value) in command.EnvironmentVariables)
        {
            process.StartInfo.Environment[key] = value;
        }

        using IProcessTreeJob job = createJob();
        process.Start();
        job.Assign(process);
        Task outputTask = ForwardLinesAsync(process.StandardOutput, onStandardOutput);
        Task errorTask = ForwardLinesAsync(process.StandardError, onStandardError);
        try
        {
            await process.WaitForExitAsync(cancellationToken);
        }
        catch (OperationCanceledException)
        {
            // Whether termination is believed to have actually taken effect: false only when Kill
            // itself failed and the process is presumed still running, since waiting below for a
            // process nothing has actually terminated would otherwise hang this cancellation --
            // and every reader of a still-open stdout/stderr pipe -- indefinitely.
            bool terminated = true;
            try
            {
                if (!process.HasExited)
                {
                    terminateProcess(process);
                }
            }
            catch (InvalidOperationException)
            {
                // The process exited between the state check and termination.
            }
            catch (Win32Exception)
            {
                // The process could not be terminated (for example access denied). Still reported as
                // cancelled, not failed, below -- this call simply stops waiting on a process (and its
                // output) it could not actually stop, rather than hanging on one that may never exit.
                terminated = false;
            }
            catch (AggregateException)
            {
                // Process.Kill(entireProcessTree: true) reports a partial tree-kill failure this way.
                // Treated exactly like the Win32Exception case above: cancellation still wins, and this
                // call stops waiting on a tree it could not fully stop.
                terminated = false;
            }

            if (terminated)
            {
                await process.WaitForExitAsync(CancellationToken.None);
                await WaitForProcessTreeToEmptyAsync(job);
                await Task.WhenAll(outputTask, errorTask);
            }

            throw;
        }

        await Task.WhenAll(outputTask, errorTask);
        return process.ExitCode;
    }

    /// <summary>
    /// Polls <paramref name="job"/> until every process it tracks has exited, or
    /// <see cref="Constants.ProcessTreeTerminationTimeout"/> elapses. Confirms the complete tree
    /// <see cref="terminateProcess"/> signaled is actually gone -- not only the root process,
    /// which the caller's own <see cref="Process.WaitForExitAsync(CancellationToken)"/> call
    /// already waited for -- so a caller that assumes no descendant is left running once this
    /// returns is not racing one that is still tearing down.
    /// </summary>
    /// <param name="job">The job tracking the command's complete process tree.</param>
    private static async Task WaitForProcessTreeToEmptyAsync(IProcessTreeJob job)
    {
        DateTime deadline = DateTime.UtcNow + Constants.ProcessTreeTerminationTimeout;
        while (job.HasActiveProcesses() && DateTime.UtcNow < deadline)
        {
            await Task.Delay(ProcessTreePollInterval);
        }
    }

    /// <summary>Forwards each line read from a stream to the supplied callback.</summary>
    /// <param name="reader">The reader supplying the lines.</param>
    /// <param name="onOutput">The callback invoked for each line, when provided.</param>
    private static async Task ForwardLinesAsync(StreamReader reader, Action<string>? onOutput)
    {
        while (await reader.ReadLineAsync() is { } line)
        {
            onOutput?.Invoke(line);
        }
    }
}
