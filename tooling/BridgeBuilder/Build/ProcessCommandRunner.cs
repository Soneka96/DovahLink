using System.Diagnostics;

namespace DovahLink.BridgeBuilder.Build;

public interface ICommandRunner
{
    Task<int> RunAsync(
        string command,
        string workingDirectory,
        Action<string>? onOutput,
        CancellationToken cancellationToken = default);
}

public sealed class ProcessCommandRunner : ICommandRunner
{
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

    private static async Task ForwardLinesAsync(StreamReader reader, Action<string>? onOutput)
    {
        while (await reader.ReadLineAsync() is { } line)
        {
            onOutput?.Invoke(line);
        }
    }
}
