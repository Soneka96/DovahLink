using System.ComponentModel;
using System.Diagnostics;
using DovahLink.DovahLinkBuilder.Build;

namespace DovahLink.DovahLinkBuilder.Tests;

/// <summary>Verifies structured child-process execution, output forwarding, and cancellation.</summary>
public sealed class ProcessCommandRunnerTests
{
    /// <summary>Imports a validated batch path containing spaces and shell metacharacters as environment data.</summary>
    [Fact]
    public async Task ImportsToolchainPathsWithoutInterpolatingThemIntoTheShellScript()
    {
        using var temporaryDirectory = new TemporaryDirectory();
        VisualStudioToolchain toolchain = Fixtures.BuildVisualStudioToolchain(
            temporaryDirectory.Path,
            "Visual Studio & tools");
        File.WriteAllText(toolchain.VcvarsallPath, "@echo off\nset DOVAHLINK_TEST_ENV=imported\n");
        BuildCommand command = BuildCommand.CreateEnvironmentImport(toolchain);
        BuildCommand restrictedSearchCommand = command with
        {
            EnvironmentVariables = new Dictionary<string, string>
            {
                ["NoDefaultCurrentDirectoryInExePath"] = "1",
            },
        };
        var output = new List<string>();
        var errors = new List<string>();

        int exitCode = await new ProcessCommandRunner().RunAsync(restrictedSearchCommand, output.Add, errors.Add);

        Assert.True(exitCode == 0, string.Join(Environment.NewLine, errors));
        Assert.DoesNotContain(toolchain.VcvarsallPath, command.Arguments);
        Assert.Empty(command.EnvironmentVariables);
        Assert.Equal(Path.GetDirectoryName(toolchain.VcvarsallPath), command.WorkingDirectory);
        IReadOnlyDictionary<string, string> environment = VisualStudioEnvironment.Create(output, toolchain);
        Assert.Equal("imported", environment["DOVAHLINK_TEST_ENV"]);
        Assert.Equal(toolchain.VcpkgRoot, environment["VCPKG_ROOT"]);
    }

    /// <summary>Forwards process output and returns the process exit code.</summary>
    [Fact]
    public async Task ForwardsProcessOutputAndReturnsTheExitCode()
    {
        using var temporaryDirectory = new TemporaryDirectory();
        var output = new List<string>();
        var errors = new List<string>();
        var command = new BuildCommand(
            Path.Combine(Environment.SystemDirectory, "cmd.exe"),
            ["/d", "/s", "/c", "echo stdout && echo stderr 1>&2"],
            temporaryDirectory.Path,
            new Dictionary<string, string>());

        int exitCode = await new ProcessCommandRunner().RunAsync(command, output.Add, errors.Add);

        Assert.Equal(0, exitCode);
        Assert.Contains(output, line => line.Trim() == "stdout");
        Assert.Contains(errors, line => line.Trim() == "stderr");
    }

    /// <summary>Applies structured environment values to the child process without altering their contents.</summary>
    [Fact]
    public async Task PassesEnvironmentValuesToTheChildProcess()
    {
        using var temporaryDirectory = new TemporaryDirectory();
        const string expectedValue = "value with spaces & symbols";
        var command = new BuildCommand(
            Path.Combine(Environment.SystemDirectory, "cmd.exe"),
            ["/d", "/c", "set DOVAHLINK_TEST_VALUE"],
            temporaryDirectory.Path,
            new Dictionary<string, string> { ["DOVAHLINK_TEST_VALUE"] = expectedValue });
        var output = new List<string>();

        int exitCode = await new ProcessCommandRunner().RunAsync(command, output.Add, null);

        Assert.Equal(0, exitCode);
        Assert.Equal([$"DOVAHLINK_TEST_VALUE={expectedValue}"], output);
    }

    /// <summary>Passes paths and arguments containing shell metacharacters directly to the executable.</summary>
    [Fact]
    public async Task KeepsShellMetacharactersAsProcessArgumentData()
    {
        using var temporaryDirectory = new TemporaryDirectory();
        string workingDirectory = Path.Combine(temporaryDirectory.Path, "working path & data");
        Directory.CreateDirectory(workingDirectory);
        string inputPath = Path.Combine(workingDirectory, "input & data.txt");
        string comparisonPath = Path.Combine(workingDirectory, "comparison & data.txt");
        File.WriteAllText(inputPath, "literal&value");
        File.WriteAllText(comparisonPath, "literal&value");
        string executablePath = Path.Combine(workingDirectory, "compare & tool.exe");
        File.Copy(Path.Combine(Environment.SystemDirectory, "fc.exe"), executablePath);
        var output = new List<string>();
        var command = new BuildCommand(
            executablePath,
            ["/b", Path.GetFileName(inputPath), Path.GetFileName(comparisonPath)],
            workingDirectory,
            new Dictionary<string, string>());

        int exitCode = await new ProcessCommandRunner().RunAsync(command, output.Add, null);

        Assert.Equal(0, exitCode);
        Assert.NotEmpty(output);
    }

    /// <summary>Preserves cancellation when process termination loses a race with natural exit.</summary>
    [Fact]
    public async Task CancellationIsNotMaskedByATerminationRace()
    {
        using var temporaryDirectory = new TemporaryDirectory();
        var command = new BuildCommand(
            Path.Combine(Environment.SystemDirectory, "cmd.exe"),
            ["/d", "/s", "/c", "ping -n 3 127.0.0.1 >nul"],
            temporaryDirectory.Path,
            new Dictionary<string, string>());
        var runner = new ProcessCommandRunner(_ => throw new InvalidOperationException("process already exited"));
        using var cancellation = new CancellationTokenSource(TimeSpan.FromMilliseconds(50));

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => runner.RunAsync(command, null, null, cancellation.Token));
    }

    /// <summary>Preserves cancellation, without hanging, when process termination itself fails.</summary>
    [Fact]
    public async Task CancellationIsNotMaskedWhenTerminationFails()
    {
        using var temporaryDirectory = new TemporaryDirectory();
        var command = new BuildCommand(
            Path.Combine(Environment.SystemDirectory, "cmd.exe"),
            // Deliberately longer than the 2-second threshold below by a wide margin: a regression
            // that waits for natural exit instead of giving up promptly must not be able to sneak
            // under that threshold by coincidence. Not matched to the tree-kill test's 30-second
            // child (CancellationTerminatesTheProcessTreeAndThrowsOperationCanceledException):
            // that test's cancellation actually kills the real process within seconds, so its longer
            // duration costs little; this test's injected termination failure never kills the real
            // process at all, so its duration is real wall-clock cost paid below regardless.
            ["/d", "/s", "/c", "ping -n 8 127.0.0.1 >nul"],
            temporaryDirectory.Path,
            new Dictionary<string, string>());
        var runner = new ProcessCommandRunner(_ => throw new Win32Exception("access denied"));
        using var cancellation = new CancellationTokenSource(TimeSpan.FromMilliseconds(50));

        var elapsed = Stopwatch.StartNew();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => runner.RunAsync(command, null, null, cancellation.Token));

        // A regression that waits for the child process to exit naturally (the "ping -n 8" above takes
        // roughly 7-8 seconds) would still eventually throw OperationCanceledException and pass the
        // assertion above alone; this threshold is what actually proves the runner gave up on
        // termination promptly instead of hanging until natural exit.
        Assert.True(elapsed.Elapsed < TimeSpan.FromSeconds(2), $"Expected a prompt return after a failed termination, took {elapsed.Elapsed}.");

        // The runner deliberately gives up on this un-terminated process rather than waiting for it (that's
        // the behaviour under test), so it's still holding the working directory open here; wait for it to
        // exit naturally before the temporary directory is disposed below. Kept separate from the timing
        // assertion above so this cleanup wait is never mistaken for the behavior under test.
        await Task.Delay(TimeSpan.FromSeconds(10));
    }

    /// <summary>Terminates a real child process tree promptly while preserving cancellation.</summary>
    [Fact]
    public async Task CancellationTerminatesTheProcessTreeAndThrowsOperationCanceledException()
    {
        using var temporaryDirectory = new TemporaryDirectory();
        string startedPath = Path.Combine(temporaryDirectory.Path, "child-started.txt");
        string sentinelPath = Path.Combine(temporaryDirectory.Path, "child-sentinel.txt");
        string batchPath = Path.Combine(temporaryDirectory.Path, "child-tree.bat");
        File.WriteAllText(
            batchPath,
            "@echo off\n" +
            "start \"\" /b powershell.exe -NoProfile -Command \"Set-Content -LiteralPath 'child-started.txt' -Value started; " +
            "Start-Sleep -Milliseconds 500; " +
            "Set-Content -LiteralPath 'child-sentinel.txt' -Value orphan\"\n" +
            "ping -n 30 127.0.0.1 >nul\n");
        var command = new BuildCommand(
            Path.Combine(Environment.SystemDirectory, "cmd.exe"),
            ["/d", "/c", $".\\{Path.GetFileName(batchPath)}"],
            temporaryDirectory.Path,
            new Dictionary<string, string> { ["NoDefaultCurrentDirectoryInExePath"] = "1" });
        using var cancellation = new CancellationTokenSource();
        Task<int> runTask = new ProcessCommandRunner().RunAsync(command, null, null, cancellation.Token);
        DateTime startDeadline = DateTime.UtcNow + TimeSpan.FromSeconds(5);
        while (!File.Exists(startedPath) && DateTime.UtcNow < startDeadline)
        {
            await Task.Delay(TimeSpan.FromMilliseconds(10));
        }
        Assert.True(File.Exists(startedPath));
        var elapsed = Stopwatch.StartNew();
        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            async () => await runTask);

        Assert.True(elapsed.Elapsed < TimeSpan.FromSeconds(5));
        await Task.Delay(TimeSpan.FromSeconds(1));
        Assert.False(File.Exists(sentinelPath));
    }

    /// <summary>Preserves cancellation, without hanging, when tree termination reports a partial failure.</summary>
    [Fact]
    public async Task CancellationIsNotMaskedWhenTerminationThrowsAnAggregateException()
    {
        using var temporaryDirectory = new TemporaryDirectory();
        var command = new BuildCommand(
            Path.Combine(Environment.SystemDirectory, "cmd.exe"),
            // See CancellationIsNotMaskedWhenTerminationFails above for why this is deliberately
            // longer than the 2-second threshold below, and deliberately not matched to the
            // tree-kill test's 30-second child.
            ["/d", "/s", "/c", "ping -n 8 127.0.0.1 >nul"],
            temporaryDirectory.Path,
            new Dictionary<string, string>());
        var runner = new ProcessCommandRunner(_ => throw new AggregateException(new Win32Exception("access denied")));
        using var cancellation = new CancellationTokenSource(TimeSpan.FromMilliseconds(50));

        var elapsed = Stopwatch.StartNew();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => runner.RunAsync(command, null, null, cancellation.Token));

        // As in CancellationIsNotMaskedWhenTerminationFails above, a regression that waits for the
        // child process to exit naturally would still eventually satisfy the assertion above alone;
        // this threshold is what actually proves the runner returned promptly.
        Assert.True(elapsed.Elapsed < TimeSpan.FromSeconds(2), $"Expected a prompt return after a failed termination, took {elapsed.Elapsed}.");

        // The runner gives up on this un-terminated process rather than waiting for it; wait for it to
        // exit naturally before the temporary directory is disposed below. Kept separate from the
        // timing assertion above so this cleanup wait is never mistaken for the behavior under test.
        await Task.Delay(TimeSpan.FromSeconds(10));
    }
}
