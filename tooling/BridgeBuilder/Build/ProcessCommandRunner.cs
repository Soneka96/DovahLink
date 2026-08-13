using System.Diagnostics;

namespace DovahLink.BridgeBuilder.Build;

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
    /// <summary>Terminates a process tree after cancellation.</summary>
    private readonly Action<Process> terminateProcess;

    /// <summary>Creates a runner that terminates the complete child process tree.</summary>
    public ProcessCommandRunner()
        : this(process => process.Kill(entireProcessTree: true))
    {
    }

    /// <summary>Creates a runner with a controllable process-termination seam.</summary>
    /// <param name="terminateProcess">The action used to terminate a running child process.</param>
    internal ProcessCommandRunner(Action<Process> terminateProcess)
    {
        this.terminateProcess = terminateProcess;
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

        process.Start();
        Task outputTask = ForwardLinesAsync(process.StandardOutput, onStandardOutput);
        Task errorTask = ForwardLinesAsync(process.StandardError, onStandardError);
        try
        {
            await process.WaitForExitAsync(cancellationToken);
        }
        catch (OperationCanceledException)
        {
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

            await process.WaitForExitAsync(CancellationToken.None);
            await Task.WhenAll(outputTask, errorTask);
            throw;
        }

        await Task.WhenAll(outputTask, errorTask);
        return process.ExitCode;
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
