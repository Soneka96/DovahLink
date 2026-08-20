using System.Diagnostics;

namespace DovahLinkValidationClient.Tests;

/// <summary>Exercises failure-safe process-harness diagnostics.</summary>
public class HarnessProcessTests
{
    /// <summary>Verifies that process exit does not complete before asynchronous stderr EOF.</summary>
    [Fact]
    public async Task WaitForExitCoordinationWaitsForStandardErrorCompletion()
    {
        var processExited = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var stderrCompleted = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        Task<bool> wait = HarnessProcess.WaitForProcessAndStandardErrorAsync(
            processExited.Task,
            stderrCompleted.Task,
            CancellationToken.None);

        processExited.SetResult(true);
        await Task.Yield();
        Assert.False(wait.IsCompleted);

        stderrCompleted.SetResult(true);
        Assert.True(await wait);
    }

    /// <summary>Verifies that the combined process and stderr wait honors cancellation.</summary>
    [Fact]
    public async Task WaitForExitCoordinationHonorsCancellation()
    {
        var stderrCompleted = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        using var cancellation = new CancellationTokenSource(TimeSpan.FromMilliseconds(100));

        Assert.False(await HarnessProcess.WaitForProcessAndStandardErrorAsync(
            Task.CompletedTask,
            stderrCompleted.Task,
            cancellation.Token));
    }

    /// <summary>Verifies that standard error can be read while its asynchronous producer appends lines.</summary>
    [Fact]
    public async Task StandardErrorSupportsConcurrentReadsAndWrites()
    {
        var startInfo = new ProcessStartInfo("cmd.exe")
        {
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };
        startInfo.ArgumentList.Add("/d");
        startInfo.ArgumentList.Add("/c");
        startInfo.ArgumentList.Add("for /L %i in (1,1,2000) do @echo diagnostic-%i 1>&2");
        using var harness = new HarnessProcess(startInfo);

        Task<bool> processExited = harness.WaitForExitAsync(TimeSpan.FromSeconds(10));
        int readCount = 0;
        while (!processExited.IsCompleted)
        {
            _ = harness.StandardError;
            readCount++;
            await Task.Yield();
        }
        Assert.True(await processExited);

        string[] lines = harness.StandardError.Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries);
        Assert.True(readCount > 0);
        Assert.Equal(2_000, lines.Length);
        Assert.Equal("diagnostic-1", lines[0].TrimEnd());
        Assert.Equal("diagnostic-2000", lines[^1].TrimEnd());
    }

    /// <summary>Verifies that a valid startup handshake yields the reported bridge instance ID and port.</summary>
    [Fact]
    public async Task WaitForReadyAsyncReturnsTheBridgeInstanceIdAfterReady()
    {
        using var harness = new HarnessProcess(
            HarnessProcess.CreateEchoingStartInfo("READY", "BRIDGE_INSTANCE abc123", "PORT 58231"));

        string bridgeInstanceId = await harness.WaitForReadyAsync();

        Assert.Equal("abc123", bridgeInstanceId);
        Assert.Equal("abc123", harness.BridgeInstanceId);
        Assert.Equal(58231, harness.Port);
    }

    /// <summary>Verifies that a non-READY first line fails the handshake clearly.</summary>
    [Fact]
    public async Task WaitForReadyAsyncThrowsWhenTheFirstLineIsNotReady()
    {
        using var harness = new HarnessProcess(HarnessProcess.CreateEchoingStartInfo("NOT_READY"));

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => harness.WaitForReadyAsync());
        Assert.Contains("did not report READY", exception.Message);
    }

    /// <summary>Verifies that a bridge instance line missing its prefix fails the handshake clearly.</summary>
    [Fact]
    public async Task WaitForReadyAsyncThrowsWhenTheBridgeInstanceLineIsMissingItsPrefix()
    {
        using var harness = new HarnessProcess(HarnessProcess.CreateEchoingStartInfo("READY", "NOT_THE_RIGHT_PREFIX"));

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => harness.WaitForReadyAsync());
        Assert.Contains("did not report its bridge instance ID", exception.Message);
    }

    /// <summary>Verifies that a bridge instance line carrying only whitespace after its prefix fails the
    /// handshake clearly instead of yielding an empty identifier.</summary>
    [Fact]
    public async Task WaitForReadyAsyncThrowsWhenTheBridgeInstanceIdIsBlank()
    {
        using var harness = new HarnessProcess(HarnessProcess.CreateEchoingStartInfo("READY", "BRIDGE_INSTANCE    "));

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => harness.WaitForReadyAsync());
        Assert.Contains("reported an empty bridge instance ID", exception.Message);
        Assert.Null(harness.BridgeInstanceId);
    }

    /// <summary>Verifies that a port line missing its prefix fails the handshake clearly.</summary>
    [Fact]
    public async Task WaitForReadyAsyncThrowsWhenThePortLineIsMissingItsPrefix()
    {
        using var harness = new HarnessProcess(
            HarnessProcess.CreateEchoingStartInfo("READY", "BRIDGE_INSTANCE abc123", "NOT_THE_RIGHT_PREFIX"));

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => harness.WaitForReadyAsync());
        Assert.Contains("did not report the port it bound", exception.Message);
        Assert.Null(harness.Port);
    }

    /// <summary>Verifies that a port line whose value is not a number fails the handshake clearly instead of
    /// yielding a bogus port.</summary>
    [Fact]
    public async Task WaitForReadyAsyncThrowsWhenThePortIsNotANumber()
    {
        using var harness = new HarnessProcess(
            HarnessProcess.CreateEchoingStartInfo("READY", "BRIDGE_INSTANCE abc123", "PORT not-a-number"));

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => harness.WaitForReadyAsync());
        Assert.Contains("did not report the port it bound", exception.Message);
        Assert.Null(harness.Port);
    }

    /// <summary>Verifies that a negative port number fails the handshake clearly instead of yielding an
    /// unusable port value -- <c>int.TryParse</c> alone accepts "-1", so this needs its own range check.</summary>
    [Fact]
    public async Task WaitForReadyAsyncThrowsWhenThePortIsNegative()
    {
        using var harness = new HarnessProcess(
            HarnessProcess.CreateEchoingStartInfo("READY", "BRIDGE_INSTANCE abc123", "PORT -1"));

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => harness.WaitForReadyAsync());
        Assert.Contains("did not report the port it bound", exception.Message);
        Assert.Null(harness.Port);
    }
}
