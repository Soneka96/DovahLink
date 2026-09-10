using System.Diagnostics;
using DovahLink.DovahLinkBuilder.Build;

namespace DovahLink.DovahLinkBuilder.Tests;

/// <summary>Verifies Job Object membership tracking and termination.</summary>
public sealed class ProcessTreeJobTests
{
    /// <summary>Reports no active processes before anything is ever assigned.</summary>
    [Fact]
    public void HasActiveProcessesReturnsFalseForAFreshJob()
    {
        using IProcessTreeJob job = new ProcessTreeJob();

        Assert.False(job.HasActiveProcesses());
    }

    /// <summary>Reports active while an assigned process runs, and inactive once it exits naturally.</summary>
    [Fact]
    public async Task HasActiveProcessesReflectsAnAssignedProcessUntilItExitsNaturally()
    {
        using IProcessTreeJob job = new ProcessTreeJob();
        using Process process = StartProcess("ping -n 2 127.0.0.1 >nul");
        job.Assign(process);

        Assert.True(job.HasActiveProcesses());

        await process.WaitForExitAsync();

        // The job's own process-count accounting can lag the exited process's own handle signal
        // by a short window (Windows updates job membership asynchronously to process termination
        // itself), so this polls rather than asserting immediately -- matching every other
        // wait-for-false assertion in this file.
        DateTime deadline = DateTime.UtcNow + TimeSpan.FromSeconds(5);
        while (job.HasActiveProcesses() && DateTime.UtcNow < deadline)
        {
            await Task.Delay(TimeSpan.FromMilliseconds(50));
        }

        Assert.False(job.HasActiveProcesses());
    }

    /// <summary>Terminate ends every assigned process promptly, without waiting for a natural exit.</summary>
    [Fact]
    public async Task TerminateEndsEveryAssignedProcessPromptly()
    {
        using IProcessTreeJob job = new ProcessTreeJob();
        using Process process = StartProcess("ping -n 30 127.0.0.1 >nul");
        job.Assign(process);
        Assert.True(job.HasActiveProcesses());

        job.Terminate();

        DateTime deadline = DateTime.UtcNow + TimeSpan.FromSeconds(5);
        while (job.HasActiveProcesses() && DateTime.UtcNow < deadline)
        {
            await Task.Delay(TimeSpan.FromMilliseconds(50));
        }

        Assert.False(job.HasActiveProcesses());
    }

    /// <summary>Terminate is a harmless no-op when the job has no active processes.</summary>
    [Fact]
    public void TerminateDoesNotThrowForAnEmptyJob()
    {
        using IProcessTreeJob job = new ProcessTreeJob();

        job.Terminate();
    }

    /// <summary>
    /// Tracks a detached ("start /b") grandchild process even after its own immediate parent has
    /// already exited -- the exact scenario <see cref="Process.Kill(bool)"/>'s own PID-snapshot
    /// tree walk can miss or race, and the reason this wrapper exists.
    /// </summary>
    [Fact]
    public async Task HasActiveProcessesTracksADetachedGrandchildAfterItsParentExits()
    {
        using var temporaryDirectory = new TemporaryDirectory();
        string startedPath = Path.Combine(temporaryDirectory.Path, "started");
        string goPath = Path.Combine(temporaryDirectory.Path, "go");
        string batchPath = Path.Combine(temporaryDirectory.Path, "start-detached-child.bat");
        // Writes a "started" marker as its very first action, before the wait loop: cmd.exe's /c
        // argument does not support goto/labels the way a real .bat file does (confirmed directly;
        // it silently stops at the first line otherwise), so this must stay a real file on disk --
        // which means StartAndConfirmLaunchedAsync below must tolerate it being transiently locked
        // by Windows Defender's real-time scan right after being written, the same class of risk
        // TemporaryDirectory.Dispose()'s own retry already tolerates on the cleanup side. Waiting
        // for "go" before launching the detached grandchild (rather than doing so the instant the
        // batch starts) still matters on top of that: Process.Start() returns as soon as the OS
        // accepts process creation, and this trivial a batch script could otherwise run its entire
        // "start /b" line before the Assign() call inside the helper even executes -- in which case
        // the grandchild would never have been a job member to begin with, regardless of how long
        // anything afterward polls.
        File.WriteAllText(
            batchPath,
            "@echo off\n" +
            $"echo started > \"{startedPath}\"\n" +
            ":wait\n" +
            $"if not exist \"{goPath}\" goto wait\n" +
            "start \"\" /b ping -n 10 127.0.0.1 >nul\n");

        (IProcessTreeJob job, Process process) = await StartAndConfirmLaunchedAsync(batchPath, startedPath);
        using (job)
        using (process)
        {
            File.WriteAllText(goPath, string.Empty);

            await process.WaitForExitAsync();

            Assert.True(job.HasActiveProcesses());

            job.Terminate();

            DateTime deadline = DateTime.UtcNow + TimeSpan.FromSeconds(5);
            while (job.HasActiveProcesses() && DateTime.UtcNow < deadline)
            {
                await Task.Delay(TimeSpan.FromMilliseconds(50));
            }

            Assert.False(job.HasActiveProcesses());
        }
    }

    /// <summary>Throws once every launch attempt fails to write the started marker within the retry budget.</summary>
    [Fact]
    public async Task StartAndConfirmLaunchedAsyncThrowsWhenTheBatchNeverStarts()
    {
        using var temporaryDirectory = new TemporaryDirectory();
        string startedPath = Path.Combine(temporaryDirectory.Path, "started");
        string batchPath = Path.Combine(temporaryDirectory.Path, "never-starts.bat");
        // Deliberately never writes startedPath, standing in for every real launch attempt failing
        // the same way a Defender-locked file would: the process exits without the retry loop's own
        // success signal ever appearing.
        File.WriteAllText(batchPath, "@echo off\nexit /b 1\n");

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => StartAndConfirmLaunchedAsync(batchPath, startedPath));
    }

    /// <summary>
    /// Starts <paramref name="batchPath"/> assigned to a fresh job, retrying the launch a bounded
    /// number of times when <paramref name="startedPath"/> -- written by the batch's own first
    /// line -- never appears. A missing marker means the batch failed to actually run (for example
    /// a transient Windows Defender lock on the freshly-written file), not that it ran and found
    /// nothing; conflating the two would let a launch failure masquerade as a real assertion
    /// failure about job tracking.
    /// </summary>
    /// <param name="batchPath">The batch file to execute.</param>
    /// <param name="startedPath">The marker file the batch writes as its first action.</param>
    /// <exception cref="InvalidOperationException">The batch did not start within the retry budget.</exception>
    private static async Task<(IProcessTreeJob Job, Process Process)> StartAndConfirmLaunchedAsync(
        string batchPath, string startedPath)
    {
        const int maxAttempts = 3;
        for (int attempt = 1; attempt <= maxAttempts; attempt++)
        {
            // A prior attempt's own process, killed below via kill-on-close after this method gave
            // up on it, could still complete its pending marker write in the narrow window before
            // it actually terminates. Clearing the marker here, before starting this attempt's own
            // process, means only this attempt's own process can make the check below true --
            // otherwise a stale marker from a killed previous attempt could falsely confirm this one
            // launched before its own process had done anything at all.
            File.Delete(startedPath);
            var job = new ProcessTreeJob();
            // StartProcess's own ArgumentList already quotes/escapes this as needed; wrapping it in
            // quotes here too double-quotes it, and cmd's /s flag then strips only the resulting
            // string's first and last quote characters (not matched pairs), producing a mangled
            // path cmd.exe reports as an unrecognized command.
            Process process = StartProcess(batchPath);
            job.Assign(process);

            DateTime deadline = DateTime.UtcNow + TimeSpan.FromSeconds(5);
            while (!File.Exists(startedPath) && !process.HasExited && DateTime.UtcNow < deadline)
            {
                await Task.Delay(TimeSpan.FromMilliseconds(50));
            }

            if (File.Exists(startedPath))
            {
                return (job, process);
            }

            job.Dispose();
            process.Dispose();
            if (attempt < maxAttempts)
            {
                await Task.Delay(TimeSpan.FromMilliseconds(200));
            }
        }

        throw new InvalidOperationException(
            $"The batch at '{batchPath}' did not start within {maxAttempts} attempts.");
    }

    /// <summary>Disposing without an explicit Terminate still kills every process still assigned, via kill-on-close.</summary>
    [Fact]
    public async Task DisposeKillsEveryAssignedProcessWithoutAnExplicitTerminate()
    {
        var job = new ProcessTreeJob();
        using Process process = StartProcess("ping -n 30 127.0.0.1 >nul");
        job.Assign(process);
        Assert.False(process.HasExited);

        job.Dispose();

        DateTime deadline = DateTime.UtcNow + TimeSpan.FromSeconds(5);
        while (!process.HasExited && DateTime.UtcNow < deadline)
        {
            await Task.Delay(TimeSpan.FromMilliseconds(50));
        }

        Assert.True(process.HasExited);
    }

    /// <summary>Starts a real, output-redirected <c>cmd.exe</c> child running <paramref name="command"/>.</summary>
    /// <param name="command">The command line passed to <c>cmd.exe /c</c>.</param>
    private static Process StartProcess(string command)
    {
        var process = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = Path.Combine(Environment.SystemDirectory, "cmd.exe"),
                ArgumentList = { "/d", "/s", "/c", command },
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
            },
        };
        process.Start();
        return process;
    }
}
