using System.Collections.Concurrent;

namespace DovahLink.Host.Tests.PairingCeremony;

/// <summary>Tests asynchronous peer-output callbacks after collection completion and disposal.</summary>
public sealed class TestPeerProcessTests
{
    /// <summary>Verifies stdout and stderr lines are queued and stdout EOF completes the shared collection.</summary>
    [Fact]
    public void OutputCallbacks_QueueLinesAndCompleteAtStandardOutputEof()
    {
        using var lines = new BlockingCollection<string>();

        TestPeerProcess.HandleStandardOutputLine(lines, "stdout");
        TestPeerProcess.HandleStandardErrorLine(lines, "error");
        TestPeerProcess.HandleStandardErrorLine(lines, null);
        TestPeerProcess.HandleStandardOutputLine(lines, "stdout after stderr EOF");
        TestPeerProcess.HandleStandardOutputLine(lines, null);

        Assert.True(lines.TryTake(out string? output, TimeSpan.Zero));
        Assert.Equal("stdout", output);
        Assert.True(lines.TryTake(out string? error, TimeSpan.Zero));
        Assert.Equal("STDERR error", error);
        Assert.True(lines.TryTake(out string? laterOutput, TimeSpan.Zero));
        Assert.Equal("stdout after stderr EOF", laterOutput);
        Assert.True(lines.IsCompleted);
    }

    /// <summary>Verifies late output, error, and EOF notifications after completion are dropped without throwing.</summary>
    [Fact]
    public void OutputCallbacks_AfterCompletion_AreIgnored()
    {
        using var lines = new BlockingCollection<string>();
        lines.CompleteAdding();

        TestPeerProcess.HandleStandardOutputLine(lines, "late stdout");
        TestPeerProcess.HandleStandardErrorLine(lines, "late stderr");
        TestPeerProcess.HandleStandardOutputLine(lines, null);

        Assert.False(lines.TryTake(out _, TimeSpan.Zero));
    }

    /// <summary>Verifies callbacks that arrive after disposal cannot throw on their reader threads.</summary>
    [Fact]
    public void OutputCallbacks_AfterDisposal_AreIgnored()
    {
        var lines = new BlockingCollection<string>();
        lines.Dispose();

        TestPeerProcess.HandleStandardOutputLine(lines, "late stdout");
        TestPeerProcess.HandleStandardErrorLine(lines, "late stderr");
        TestPeerProcess.HandleStandardOutputLine(lines, null);
    }

    /// <summary>Verifies concurrent EOF and stderr callbacks can race without escaping a collection exception.</summary>
    [Fact]
    public async Task OutputCallbacks_CompletionRacesWithErrorWrite_AreContained()
    {
        using var lines = new BlockingCollection<string>();

        await Task.WhenAll(
            Task.Run(() => TestPeerProcess.HandleStandardOutputLine(lines, null)),
            Task.Run(() => TestPeerProcess.HandleStandardErrorLine(lines, "racing stderr")))
            .WaitAsync(TimeSpan.FromSeconds(2));

        lines.CompleteAdding();
        Assert.True(lines.IsAddingCompleted);
        if (lines.TryTake(out string? line, TimeSpan.Zero))
        {
            Assert.Equal("STDERR racing stderr", line);
            Assert.False(lines.TryTake(out _, TimeSpan.Zero));
        }

        Assert.True(lines.IsCompleted);
    }
}
