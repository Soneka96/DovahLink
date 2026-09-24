using System.ComponentModel;
using System.Diagnostics;
using DovahLink.DovahLinkBuilder;
using DovahLink.DovahLinkBuilder.Build;
using Xunit.Abstractions;

namespace DovahLink.DovahLinkBuilder.Tests;

/// <summary>Verifies structured child-process execution, output forwarding, and cancellation.</summary>
public sealed class ProcessCommandRunnerTests
{
    /// <summary>
    /// Captures diagnostic lines for this test run. Writing to this happens immediately, unlike an
    /// assertion failure: if a later exception (for example <see cref="TemporaryDirectory.Dispose"/>
    /// hitting a transient file lock during cleanup) replaces the exception a failed assertion would
    /// otherwise have thrown, a line already written here still survives and is visible in the test's
    /// captured output.
    /// </summary>
    private readonly ITestOutputHelper output;

    /// <summary>Initializes this test class with xUnit's per-test output sink.</summary>
    /// <param name="output">Writes diagnostic lines to this test's captured xUnit output.</param>
    public ProcessCommandRunnerTests(ITestOutputHelper output)
    {
        this.output = output;
    }

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
        string pidPath = Path.Combine(temporaryDirectory.Path, "child-pid.txt");
        string startedPath = Path.Combine(temporaryDirectory.Path, "child-started.txt");
        string sentinelPath = Path.Combine(temporaryDirectory.Path, "child-sentinel.txt");
        string goPath = Path.Combine(temporaryDirectory.Path, "child-go.txt");
        string batchPath = Path.Combine(temporaryDirectory.Path, "child-tree.bat");
        string childScriptPath = Path.Combine(temporaryDirectory.Path, "child-tree.ps1");
        string powershellPath = Path.Combine(Environment.SystemDirectory, "WindowsPowerShell", "v1.0", "powershell.exe");
        File.WriteAllText(
            childScriptPath,
            $"Set-Content -LiteralPath '{pidPath.Replace("'", "''")}' -Value $PID\n" +
            "Start-Sleep -Milliseconds 500\n" +
            $"Set-Content -LiteralPath '{sentinelPath.Replace("'", "''")}' -Value orphan\n");
        File.WriteAllText(
            batchPath,
            "@echo off\n" +
            $"echo started > \"{startedPath}\"\n" +
            ":wait\n" +
            $"if not exist \"{goPath}\" goto wait\n" +
            $"start \"\" /b \"{powershellPath}\" -NoProfile -File \"{childScriptPath}\"\n" +
            "ping -n 30 127.0.0.1 >nul\n");
        var command = new BuildCommand(
            Path.Combine(Environment.SystemDirectory, "cmd.exe"),
            ["/d", "/c", $".\\{Path.GetFileName(batchPath)}"],
            temporaryDirectory.Path,
            new Dictionary<string, string> { ["NoDefaultCurrentDirectoryInExePath"] = "1" });
        var jobAssigned = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var runner = new ProcessCommandRunner(
            process => process.Kill(entireProcessTree: true),
            () => new SignalingProcessTreeJob(new ProcessTreeJob(), jobAssigned));
        using var cancellation = new CancellationTokenSource();
        Task<int> runTask = runner.RunAsync(
            command,
            line => output.WriteLine($"stdout: {line}"),
            line => output.WriteLine($"stderr: {line}"),
            cancellation.Token);
        Exception? bodyException = null;
        try
        {
            await jobAssigned.Task.WaitAsync(TimeSpan.FromSeconds(5));
            File.WriteAllText(goPath, string.Empty);
            DateTime startDeadline = DateTime.UtcNow + TimeSpan.FromSeconds(5);
            while (!File.Exists(startedPath) && DateTime.UtcNow < startDeadline)
            {
                await Task.Delay(TimeSpan.FromMilliseconds(10));
            }
            Assert.True(File.Exists(startedPath));
            int descendantProcessId = await ReadDescendantProcessIdAsync(pidPath);
            var elapsed = Stopwatch.StartNew();
            cancellation.Cancel();

            await Assert.ThrowsAnyAsync<OperationCanceledException>(
                async () => await runTask);

            Assert.True(elapsed.Elapsed < TimeSpan.FromSeconds(5));
            await AssertDescendantProcessExitedAsync(descendantProcessId);

            // Captured and logged before asserting, rather than passed directly to Assert.False: if this
            // is false (the real proof the process tree was actually killed) but the temporary directory
            // then fails to delete on a loaded CI runner, .NET discards this method's own exception in
            // favor of the one TemporaryDirectory.Dispose() throws during the using statement's unwind --
            // silently replacing "the assertion failed" with an unrelated-looking IOException. Logging the
            // captured value first means a future failure's CI output still shows which one actually
            // happened, even when the exception itself gets masked.
            bool sentinelExists = File.Exists(sentinelPath);
            output.WriteLine($"Sentinel file exists after cancellation: {sentinelExists}");
            Assert.False(sentinelExists);
        }
        catch (Exception exception)
        {
            bodyException = exception;
            throw;
        }
        finally
        {
            cancellation.Cancel();
            try
            {
                await runTask;
            }
            catch (OperationCanceledException exception)
                when (runTask.IsCanceled
                    && cancellation.IsCancellationRequested
                    && (exception.CancellationToken == cancellation.Token
                        || exception.CancellationToken == default))
            {
            }
            catch (Exception cleanupException) when (bodyException is not null)
            {
                throw new AggregateException(bodyException, cleanupException);
            }
        }
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

    /// <summary>Assigns the started process to the tracking job and waits for it to report empty before returning from cancellation.</summary>
    [Fact]
    public async Task CancellationAssignsTheProcessToTheTrackingJobAndWaitsForItToEmpty()
    {
        using var temporaryDirectory = new TemporaryDirectory();
        var command = new BuildCommand(
            Path.Combine(Environment.SystemDirectory, "cmd.exe"),
            ["/d", "/s", "/c", "ping -n 3 127.0.0.1 >nul"],
            temporaryDirectory.Path,
            new Dictionary<string, string>());
        var fakeJob = new FakeProcessTreeJob { ActiveCallsBeforeEmpty = 2 };
        var runner = new ProcessCommandRunner(process => process.Kill(), () => fakeJob);
        using var cancellation = new CancellationTokenSource(TimeSpan.FromMilliseconds(50));

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => runner.RunAsync(command, null, null, cancellation.Token));

        Assert.NotNull(fakeJob.AssignedProcess);
        Assert.Equal(fakeJob.ActiveCallsBeforeEmpty + 1, fakeJob.HasActiveProcessesCallCount);
        Assert.Equal(1, fakeJob.TerminateCallCount);
        Assert.True(fakeJob.Disposed);
    }

    /// <summary>Gives up waiting on a process tree that never reports empty, rather than hanging forever.</summary>
    [Fact]
    public async Task CancellationGivesUpWaitingOnATreeThatNeverReportsEmpty()
    {
        using var temporaryDirectory = new TemporaryDirectory();
        var command = new BuildCommand(
            Path.Combine(Environment.SystemDirectory, "cmd.exe"),
            ["/d", "/s", "/c", "ping -n 3 127.0.0.1 >nul"],
            temporaryDirectory.Path,
            new Dictionary<string, string>());
        var fakeJob = new FakeProcessTreeJob { ActiveCallsBeforeEmpty = int.MaxValue };
        var runner = new ProcessCommandRunner(process => process.Kill(), () => fakeJob);
        using var cancellation = new CancellationTokenSource(TimeSpan.FromMilliseconds(50));

        var elapsed = Stopwatch.StartNew();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => runner.RunAsync(command, null, null, cancellation.Token));

        // Proves the wait is actually bounded by Constants.ProcessTreeTerminationTimeout, not that
        // this specific run happened to be fast: a regression that polls forever would never reach
        // this assertion at all.
        Assert.True(
            elapsed.Elapsed < Constants.ProcessTreeTerminationTimeout + TimeSpan.FromSeconds(2),
            $"Expected a bounded wait even when the tree never reports empty, took {elapsed.Elapsed}.");
        Assert.Equal(1, fakeJob.TerminateCallCount);
        Assert.True(fakeJob.Disposed);
    }

    /// <summary>
    /// Terminates the tracked job even when the root process could not be terminated, so a detached
    /// descendant outside the root's own kill is not left waited on for the full poll timeout.
    /// </summary>
    [Fact]
    public async Task CancellationTerminatesTheTrackedJobWhenRootTerminationFails()
    {
        using var temporaryDirectory = new TemporaryDirectory();
        var command = new BuildCommand(
            Path.Combine(Environment.SystemDirectory, "cmd.exe"),
            ["/d", "/s", "/c", "ping -n 8 127.0.0.1 >nul"],
            temporaryDirectory.Path,
            new Dictionary<string, string>());
        var fakeJob = new FakeProcessTreeJob();
        var runner = new ProcessCommandRunner(_ => throw new Win32Exception("access denied"), () => fakeJob);
        using var cancellation = new CancellationTokenSource(TimeSpan.FromMilliseconds(50));

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => runner.RunAsync(command, null, null, cancellation.Token));

        Assert.Equal(1, fakeJob.TerminateCallCount);
        Assert.True(fakeJob.Disposed);

        // The runner gives up on this un-terminated process rather than waiting for it; wait for it to
        // exit naturally before the temporary directory is disposed below.
        await Task.Delay(TimeSpan.FromSeconds(10));
    }

    /// <summary>Terminates the tracked job even when tree termination itself reports a partial failure.</summary>
    [Fact]
    public async Task CancellationTerminatesTheTrackedJobWhenRootTerminationThrowsAnAggregateException()
    {
        using var temporaryDirectory = new TemporaryDirectory();
        var command = new BuildCommand(
            Path.Combine(Environment.SystemDirectory, "cmd.exe"),
            ["/d", "/s", "/c", "ping -n 8 127.0.0.1 >nul"],
            temporaryDirectory.Path,
            new Dictionary<string, string>());
        var fakeJob = new FakeProcessTreeJob();
        var runner = new ProcessCommandRunner(_ => throw new AggregateException(new Win32Exception("access denied")), () => fakeJob);
        using var cancellation = new CancellationTokenSource(TimeSpan.FromMilliseconds(50));

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => runner.RunAsync(command, null, null, cancellation.Token));

        Assert.Equal(1, fakeJob.TerminateCallCount);
        Assert.True(fakeJob.Disposed);

        // The runner gives up on this un-terminated process rather than waiting for it; wait for it to
        // exit naturally before the temporary directory is disposed below.
        await Task.Delay(TimeSpan.FromSeconds(10));
    }

    /// <summary>Preserves cancellation and still waits for the process tree to empty when job termination itself fails.</summary>
    [Fact]
    public async Task CancellationIsNotMaskedWhenJobTerminationFails()
    {
        using var temporaryDirectory = new TemporaryDirectory();
        var command = new BuildCommand(
            Path.Combine(Environment.SystemDirectory, "cmd.exe"),
            ["/d", "/s", "/c", "ping -n 3 127.0.0.1 >nul"],
            temporaryDirectory.Path,
            new Dictionary<string, string>());
        var fakeJob = new FakeProcessTreeJob { ActiveCallsBeforeEmpty = 2, ThrowOnTerminate = new Win32Exception("access denied") };
        var runner = new ProcessCommandRunner(process => process.Kill(), () => fakeJob);
        using var cancellation = new CancellationTokenSource(TimeSpan.FromMilliseconds(50));

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => runner.RunAsync(command, null, null, cancellation.Token));

        Assert.Equal(1, fakeJob.TerminateCallCount);
        Assert.Equal(fakeJob.ActiveCallsBeforeEmpty + 1, fakeJob.HasActiveProcessesCallCount);
        Assert.True(fakeJob.Disposed);
    }

    /// <summary>Terminates the already-started process and rethrows when job assignment fails.</summary>
    [Fact]
    public async Task AssignFailureTerminatesTheStartedProcessAndRethrows()
    {
        using var temporaryDirectory = new TemporaryDirectory();
        var command = new BuildCommand(
            Path.Combine(Environment.SystemDirectory, "cmd.exe"),
            ["/d", "/s", "/c", "ping -n 3 127.0.0.1 >nul"],
            temporaryDirectory.Path,
            new Dictionary<string, string>());
        var fakeJob = new FakeProcessTreeJob { ThrowOnAssign = new Win32Exception("access denied") };
        bool terminateCalled = false;
        var runner = new ProcessCommandRunner(process => { terminateCalled = true; process.Kill(); }, () => fakeJob);

        await Assert.ThrowsAsync<Win32Exception>(() => runner.RunAsync(command, null, null));

        Assert.True(terminateCalled);
        Assert.True(fakeJob.Disposed);
    }

    /// <summary>
    /// Waits for the detached descendant to publish its own process id at <paramref name="pidPath"/>
    /// and parses it, retrying past the short window in which the descendant may still hold the file
    /// open while writing it.
    /// </summary>
    /// <param name="pidPath">The file the descendant writes its own process id to.</param>
    /// <returns>The descendant's process id.</returns>
    private static async Task<int> ReadDescendantProcessIdAsync(string pidPath)
    {
        DateTime deadline = DateTime.UtcNow + TimeSpan.FromSeconds(15);
        string lastContents = "<missing>";
        while (DateTime.UtcNow < deadline)
        {
            if (File.Exists(pidPath))
            {
                try
                {
                    lastContents = File.ReadAllText(pidPath).Trim();
                    if (int.TryParse(lastContents, out int processId) && processId > 0)
                    {
                        return processId;
                    }
                }
                catch (IOException)
                {
                    // The descendant may still be writing or closing the file; retry.
                    lastContents = "<temporarily unavailable>";
                }
            }

            await Task.Delay(TimeSpan.FromMilliseconds(10));
        }

        throw new InvalidOperationException(
            $"The descendant process never published a valid process id to '{pidPath}'. Last contents: '{lastContents}'.");
    }

    /// <summary>
    /// Bounded-waits for the descendant process to actually exit, rather than assuming a fixed delay
    /// after cancellation proves it is gone -- so a real regression fails with an actionable message
    /// naming the still-alive descendant instead of only the unrelated-looking
    /// <see cref="IOException"/> a subsequent <see cref="TemporaryDirectory.Dispose"/> would raise.
    /// </summary>
    /// <param name="processId">The descendant's process id, as published to <c>child-pid.txt</c>.</param>
    private static async Task AssertDescendantProcessExitedAsync(int processId)
    {
        DateTime deadline = DateTime.UtcNow + Constants.ProcessTreeTerminationTimeout + TimeSpan.FromSeconds(2);
        while (DateTime.UtcNow < deadline)
        {
            Process descendant;
            try
            {
                descendant = Process.GetProcessById(processId);
            }
            catch (ArgumentException)
            {
                // No process with this id remains: the descendant has already exited.
                return;
            }

            using (descendant)
            {
                if (descendant.HasExited)
                {
                    return;
                }
            }

            await Task.Delay(TimeSpan.FromMilliseconds(20));
        }

        Assert.Fail($"Expected descendant PID {processId} to exit after cancellation.");
    }

    /// <summary>
    /// A controllable fake of <see cref="IProcessTreeJob"/> proving <see cref="ProcessCommandRunner"/>'s
    /// own orchestration -- assign, poll until empty, dispose -- without a real process tree.
    /// </summary>
    private sealed class FakeProcessTreeJob : IProcessTreeJob
    {
        /// <summary>The number of leading <see cref="HasActiveProcesses"/> calls that report an active process before reporting empty.</summary>
        public int ActiveCallsBeforeEmpty { get; set; }

        /// <summary>The exception <see cref="Assign"/> throws instead of recording the process, or <see langword="null"/> to assign normally.</summary>
        public Win32Exception? ThrowOnAssign { get; set; }

        /// <summary>The process passed to <see cref="Assign"/>, or <see langword="null"/> before it is ever called.</summary>
        public Process? AssignedProcess { get; private set; }

        /// <summary>The number of times <see cref="HasActiveProcesses"/> has been called.</summary>
        public int HasActiveProcessesCallCount { get; private set; }

        /// <summary>Whether <see cref="Dispose"/> has been called.</summary>
        public bool Disposed { get; private set; }

        /// <summary>The number of times <see cref="Terminate"/> has been called.</summary>
        public int TerminateCallCount { get; private set; }

        /// <summary>The exception <see cref="Terminate"/> throws after recording the call, or <see langword="null"/> to succeed.</summary>
        public Win32Exception? ThrowOnTerminate { get; set; }

        /// <inheritdoc/>
        public void Assign(Process process)
        {
            if (ThrowOnAssign is not null)
            {
                throw ThrowOnAssign;
            }

            AssignedProcess = process;
        }

        /// <inheritdoc/>
        public bool HasActiveProcesses() => ++HasActiveProcessesCallCount <= ActiveCallsBeforeEmpty;

        /// <inheritdoc/>
        public void Terminate()
        {
            TerminateCallCount++;
            if (ThrowOnTerminate is not null)
            {
                throw ThrowOnTerminate;
            }
        }

        /// <inheritdoc/>
        public void Dispose() => Disposed = true;
    }

    /// <summary>Signals a test after a real process has been assigned to its tracking job.</summary>
    private sealed class SignalingProcessTreeJob : IProcessTreeJob
    {
        /// <summary>The real process-tree job delegated to by this test seam.</summary>
        private readonly IProcessTreeJob innerJob;

        /// <summary>The signal completed after <see cref="Assign"/> succeeds.</summary>
        private readonly TaskCompletionSource<bool> assignedSignal;

        /// <summary>Creates a job wrapper that signals after assignment succeeds.</summary>
        /// <param name="innerJob">The real process-tree job that owns process membership.</param>
        /// <param name="assignedSignal">The signal completed after the root process is assigned.</param>
        public SignalingProcessTreeJob(
            IProcessTreeJob innerJob,
            TaskCompletionSource<bool> assignedSignal)
        {
            this.innerJob = innerJob;
            this.assignedSignal = assignedSignal;
        }

        /// <inheritdoc/>
        public void Assign(Process process)
        {
            innerJob.Assign(process);
            assignedSignal.TrySetResult(true);
        }

        /// <inheritdoc/>
        public bool HasActiveProcesses() => innerJob.HasActiveProcesses();

        /// <inheritdoc/>
        public void Terminate() => innerJob.Terminate();

        /// <inheritdoc/>
        public void Dispose() => innerJob.Dispose();
    }
}
