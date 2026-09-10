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
        string goPath = Path.Combine(temporaryDirectory.Path, "go");
        string batchPath = Path.Combine(temporaryDirectory.Path, "start-detached-child.bat");
        // Waits for a "go" marker file before launching its own detached grandchild, rather than
        // doing so the instant it starts: Process.Start() returns as soon as the OS accepts process
        // creation, and this trivial a batch script could otherwise run its entire "start /b" line
        // before the Assign() call below even executes -- in which case the grandchild would never
        // have been a job member to begin with, regardless of how long anything afterward polls.
        File.WriteAllText(
            batchPath,
            "@echo off\n" +
            ":wait\n" +
            $"if not exist \"{goPath}\" goto wait\n" +
            "start \"\" /b ping -n 10 127.0.0.1 >nul\n");
        using IProcessTreeJob job = new ProcessTreeJob();
        using Process process = StartProcess($"\"{batchPath}\"");
        job.Assign(process);
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
