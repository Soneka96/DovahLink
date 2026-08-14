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

    /// <summary>Serializes asynchronous standard-error writes with diagnostic reads.</summary>
    private readonly object _stderrLock = new();

    /// <summary>Completes when the asynchronous standard-error reader receives its end-of-stream marker.</summary>
    private readonly TaskCompletionSource<bool> _stderrReadCompleted =
        new(TaskCreationOptions.RunContinuationsAsynchronously);

    /// <summary>
    /// Starts the bridge harness with redirected standard streams and the specified environment configuration.
    /// </summary>
    /// <param name="token">The bridge token to provide to the harness, or <c>null</c> to omit it.</param>
    /// <param name="extraEnvironmentVariables">Additional environment variables to provide to the harness.</param>
    public HarnessProcess(string? token, IReadOnlyDictionary<string, string>? extraEnvironmentVariables = null)
        : this(CreateStartInfo(token, extraEnvironmentVariables))
    {
    }

    /// <summary>Starts a process from an injected specification for deterministic diagnostic tests.</summary>
    /// <param name="startInfo">The redirected process specification to start.</param>
    internal HarnessProcess(ProcessStartInfo startInfo)
    {
        _process = Process.Start(startInfo) ?? throw new InvalidOperationException("Failed to start the bridge harness process.");

        _process.ErrorDataReceived += (_, e) =>
        {
            if (e.Data is null)
            {
                _stderrReadCompleted.TrySetResult(true);
            }
            else
            {
                AppendStandardError(e.Data);
            }
        };
        _process.BeginErrorReadLine();
    }

    /// <summary>Diagnostic output captured from the harness's standard error stream.</summary>
    public string StandardError
    {
        get
        {
            lock (_stderrLock)
            {
                return _stderr.ToString();
            }
        }
    }

    /// <summary>The bridge instance identifier reported by the harness during <see cref="WaitForReadyAsync"/>, or <c>null</c> before that completes.</summary>
    public string? BridgeInstanceId { get; private set; }

    /// <summary>
    /// Reads and validates the harness startup handshake: the <c>READY</c> line followed by its <c>BRIDGE_INSTANCE &lt;id&gt;</c> line.
    /// </summary>
    /// <param name="timeout">The maximum time to wait for each line, or the default read timeout when omitted.</param>
    /// <returns>The bridge instance ID reported by the harness, which is also stored in <see cref="BridgeInstanceId"/>.</returns>
    /// <exception cref="InvalidOperationException">Thrown when the harness does not report READY followed by its bridge instance ID.</exception>
    public async Task<string> WaitForReadyAsync(TimeSpan? timeout = null)
    {
        string? ready = await ReadLineAsync(timeout);
        if (ready != "READY")
        {
            throw new InvalidOperationException($"Harness did not report READY: {ready}. Stderr: {StandardError}");
        }

        const string prefix = "BRIDGE_INSTANCE ";
        string? instanceLine = await ReadLineAsync(timeout);
        if (instanceLine is null || !instanceLine.StartsWith(prefix, StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                $"Harness did not report its bridge instance ID: {instanceLine}. Stderr: {StandardError}");
        }

        BridgeInstanceId = instanceLine[prefix.Length..];
        return BridgeInstanceId;
    }

    /// <summary>Appends one diagnostic line while excluding concurrent readers.</summary>
    /// <param name="line">The standard-error line to append.</param>
    private void AppendStandardError(string line)
    {
        lock (_stderrLock)
        {
            _stderr.AppendLine(line);
        }
    }

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
        return await WaitForProcessAndStandardErrorAsync(
            _process.WaitForExitAsync(cts.Token), _stderrReadCompleted.Task, cts.Token);
    }

    /// <summary>Coordinates process exit with completion of the asynchronous standard-error reader.
    /// </summary>
    /// <param name="processExitTask">The task that completes when the process exits.</param>
    /// <param name="stderrReadCompletedTask">The task that completes when stderr reaches EOF.</param>
    /// <param name="cancellationToken">The timeout or cancellation signal for both waits.</param>
    /// <returns><c>true</c> when both completion signals arrive; otherwise <c>false</c> when canceled.</returns>
    internal static async Task<bool> WaitForProcessAndStandardErrorAsync(
        Task processExitTask,
        Task stderrReadCompletedTask,
        CancellationToken cancellationToken)
    {
        try
        {
            await processExitTask.WaitAsync(cancellationToken);
            await stderrReadCompletedTask.WaitAsync(cancellationToken);
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

    /// <summary>Builds the redirected bridge-harness process specification.</summary>
    /// <param name="token">The bridge token to provide, or <c>null</c> to omit it.</param>
    /// <param name="extraEnvironmentVariables">Additional environment variables for the harness.</param>
    /// <returns>The configured process start information.</returns>
    private static ProcessStartInfo CreateStartInfo(
        string? token,
        IReadOnlyDictionary<string, string>? extraEnvironmentVariables)
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
        return startInfo;
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
