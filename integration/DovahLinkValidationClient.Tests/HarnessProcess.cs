using System.Diagnostics;
using System.Text;

namespace DovahLinkValidationClient.Tests;

// Launches the Skyrim-independent bridge harness
// (bridge/harness/dovahlink_bridge_harness.cpp) as a real subprocess so
// scenarios exercise the real bridge stack without requiring Skyrim. Each
// test gets its own instance: the harness enforces Phase 1's one-connected-
// client limit, so instances cannot be shared across tests running in the
// same process.
public sealed class HarnessProcess : IDisposable
{
    private static readonly TimeSpan DefaultReadTimeout = TimeSpan.FromSeconds(10);

    private readonly Process _process;
    private readonly StringBuilder _stderr = new();

    // `token` becomes the harness's DOVAHLINK_BRIDGE_TOKEN; pass null to
    // test the missing-token startup path.
    public HarnessProcess(string? token)
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

        _process = Process.Start(startInfo) ?? throw new InvalidOperationException("Failed to start the bridge harness process.");

        // Drained continuously via the event-based API, not read on demand:
        // if stderr fills its OS pipe buffer with nothing reading it, the
        // harness blocks on its own write, deadlocking the whole test.
        // Captured rather than discarded so a failing test can inspect
        // StandardError for why.
        _process.ErrorDataReceived += (_, e) =>
        {
            if (e.Data is not null)
            {
                _stderr.AppendLine(e.Data);
            }
        };
        _process.BeginErrorReadLine();
    }

    // Diagnostic output captured from the harness's stderr so far.
    public string StandardError => _stderr.ToString();

    // Bounded by `timeout` (default 10s): a harness that never writes a
    // line fails the caller instead of hanging the test indefinitely.
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

    public async Task WriteLineAsync(string line)
    {
        await _process.StandardInput.WriteLineAsync(line);
        await _process.StandardInput.FlushAsync();
    }

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

    public int ExitCode => _process.ExitCode;

    public void Dispose()
    {
        if (!_process.HasExited)
        {
            _process.Kill(entireProcessTree: true);
            _process.WaitForExit(5000);
        }
        _process.Dispose();
    }

    // Walks up from the test's own output directory to the repo root, then
    // into the C++ build output -- the same executable
    // bridge/harness/dovahlink_bridge_harness_test.cpp locates via CMake's
    // DOVAHLINK_HARNESS_EXE compile definition, just found by directory
    // search instead since MSBuild has no equivalent generator expression
    // into a sibling CMake build.
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
