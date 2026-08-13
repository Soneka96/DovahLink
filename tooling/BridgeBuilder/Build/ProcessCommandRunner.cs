using System.Diagnostics;

namespace DovahLink.BridgeBuilder.Build;

/// <summary>Runs external commands and forwards their output.</summary>
public interface ICommandRunner
{
    /// <summary>
    /// Executes a command in the specified working directory.
    /// </summary>
    /// <param name="command">The command to execute.</param>
    /// <param name="workingDirectory">The directory in which to execute the command.</param>
    /// <param name="onOutput">The callback invoked for each line of command output, or <see langword="null"/> to ignore output.</param>
    /// <param name="cancellationToken">The token used to cancel command execution.</param>
    /// <returns>The command's exit code.</returns>
    Task<int> RunAsync(
        string command,
        string workingDirectory,
        Action<string>? onOutput,
        CancellationToken cancellationToken = default);
}

/// <summary>Runs commands through the Windows command shell.</summary>
public sealed class ProcessCommandRunner : ICommandRunner
{
    /// <summary>
    /// Executes a command in the specified working directory and forwards its output.
    /// </summary>
    /// <param name="onOutput">The callback invoked for each line written to standard output or standard error.</param>
    /// <returns>The command's exit code.</returns>
    public async Task<int> RunAsync(
        string command,
        string workingDirectory,
        Action<string>? onOutput,
        CancellationToken cancellationToken = default)
    {
        using var process = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = Environment.GetEnvironmentVariable("ComSpec") ?? "cmd.exe",
                WorkingDirectory = workingDirectory,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
            },
        };
        // Pass the complete command as raw cmd.exe arguments. ArgumentList
        // escapes the quotes inside a /c command as backslashes, causing
        // cmd.exe to look for a literal path such as \"C:\\Program Files...\".
        process.StartInfo.Arguments = $"/d /s /c {command}";

        process.Start();
        Task outputTask = ForwardLinesAsync(process.StandardOutput, onOutput);
        Task errorTask = ForwardLinesAsync(process.StandardError, onOutput);
        try
        {
            await process.WaitForExitAsync(cancellationToken);
        }
        catch (OperationCanceledException)
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
            }

            await process.WaitForExitAsync(CancellationToken.None);
            await Task.WhenAll(outputTask, errorTask);
            throw;
        }

        await Task.WhenAll(outputTask, errorTask);
        return process.ExitCode;
    }

    /// <summary>
    /// Forwards each line read from a stream to the output callback.
    /// </summary>
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
