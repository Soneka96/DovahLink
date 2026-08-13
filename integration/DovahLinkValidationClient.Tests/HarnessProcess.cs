using System.Diagnostics;
using System.Text;

namespace DovahLinkValidationClient.Tests;

/// <summary>Launches and controls one deterministic bridge harness subprocess.</summary>
public sealed class HarnessProcess : IDisposable
{
    /// <summary>The default time allowed for one harness output line.</summary>
    private static readonly TimeSpan DefaultReadTimeout = TimeSpan.FromSeconds(10);

    /// <summary>The running harness process.</summary>
    private readonly Process _process;

    /// <summary>Standard-error output captured from the harness.</summary>
    private readonly StringBuilder _stderr = new();

    /// <summary>
    /// Starts the bridge harness with redirected standard streams and the specified environment configuration.
    /// </summary>
    /// <param name="token">The bridge token to provide to the harness, or <c>null</c> to omit it.</param>
    /// <param name="extraEnvironmentVariables">Additional environment variables to provide to the harness.</param>
    public HarnessProcess(string? token, IReadOnlyDictionary<string, string>? extraEnvironmentVariables = null)
    {
        var startInfo = new ProcessStartInfo(LocateHarnessExe())
        {
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };
        startInfo.EnvironmentVariables.Remove("DOVAHLINK_BRIDGE_TOKEN");
        if (token is not null)
        {
            startInfo.EnvironmentVariables["DOVAHLINK_BRIDGE_TOKEN"] = token;
        }
        if (extraEnvironmentVariables is not null)
        {
            foreach ((string key, string value) in extraEnvironmentVariables)
            {
                startInfo.EnvironmentVariables[key] = value;
            }
        }

        _process = Process.Start(startInfo) ?? throw new InvalidOperationException("Failed to start the bridge harness process.");

        _process.ErrorDataReceived += (_, e) =>
        {
            if (e.Data is not null)
            {
                _stderr.AppendLine(e.Data);
            }
        };
        _process.BeginErrorReadLine();
    }

    /// <summary>Diagnostic output captured from the harness's standard error stream.</summary>
    public string StandardError => _stderr.ToString();

    /// <summary>
    /// Reads a line from the harness output within the specified timeout.
    /// </summary>
    /// <param name="timeout">The maximum time to wait for a line, or the default read timeout when omitted.</param>
    /// <returns>The next output line, or <c>null</c> when the output stream reaches its end.</returns>
    /// <exception cref="TimeoutException">Thrown when no line is produced before the timeout.</exception>
    public async Task<string?> ReadLineAsync(TimeSpan? timeout = null)
    {
        Task<string?> readTask = _process.StandardOutput.ReadLineAsync();
        Task completed = await Task.WhenAny(readTask, Task.Delay(timeout ?? DefaultReadTimeout));
        if (completed != readTask)
        {
            throw new TimeoutException(
                $"Harness did not write a line within {timeout ?? DefaultReadTimeout}. Stderr so far: {StandardError}");
        }
        return await readTask;
    }

    /// <summary>
    /// Writes a line to the harness and flushes the input stream.
    /// </summary>
    /// <param name="line">The line to write.</param>
    public async Task WriteLineAsync(string line)
    {
        await _process.StandardInput.WriteLineAsync(line);
        await _process.StandardInput.FlushAsync();
    }

    /// <summary>
    /// Waits for the harness process to exit within the specified timeout.
    /// </summary>
    /// <param name="timeout">The maximum time to wait for process termination.</param>
    /// <returns><c>true</c> if the process exits within the timeout; <c>false</c> if the wait times out.</returns>
    public async Task<bool> WaitForExitAsync(TimeSpan timeout)
    {
        using var cts = new CancellationTokenSource(timeout);
        try
        {
            await _process.WaitForExitAsync(cts.Token);
            return true;
        }
        catch (OperationCanceledException)
        {
            return false;
        }
    }

    /// <summary>The harness process exit code.</summary>
    public int ExitCode => _process.ExitCode;

    /// <summary>
    /// Terminates the harness process tree if it is still running and releases the process resources.
    /// </summary>
    public void Dispose()
    {
        if (!_process.HasExited)
        {
            _process.Kill(entireProcessTree: true);
            _process.WaitForExit(5000);
        }
        _process.Dispose();
    }

    /// <summary>
    /// Locates the Windows debug-build DovahLink bridge harness executable.
    /// </summary>
    /// <returns>The path to the harness executable.</returns>
    /// <exception cref="FileNotFoundException">Thrown when the harness executable cannot be found.</exception>
    private static string LocateHarnessExe()
    {
        string? dir = AppContext.BaseDirectory;
        for (int i = 0; i < 10 && dir is not null; i++)
        {
            string candidate = Path.Combine(dir, "bridge", "build", "windows-x64-debug", "dovahlink_bridge_harness.exe");
            if (File.Exists(candidate))
            {
                return candidate;
            }
            dir = Path.GetDirectoryName(dir);
        }

        throw new FileNotFoundException(
            "Could not find dovahlink_bridge_harness.exe under any ancestor of " +
            $"{AppContext.BaseDirectory}. Build it first: " +
            "cmake --build --preset windows-x64-debug --target dovahlink_bridge_harness");
    }
}
