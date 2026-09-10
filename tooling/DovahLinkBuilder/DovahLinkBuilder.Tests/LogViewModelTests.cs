using System.Runtime.InteropServices;
using DovahLink.DovahLinkBuilder.Ui;

namespace DovahLink.DovahLinkBuilder.Tests;

/// <summary>Verifies the Build page's log panel: accumulation, clear, and copy-all.</summary>
public sealed class LogViewModelTests
{
    /// <summary>Starts empty, expanded, and with auto-scroll on.</summary>
    [Fact]
    public void StartsEmptyExpandedWithAutoScrollOn()
    {
        var log = new LogViewModel(_ => { });

        Assert.Empty(log.Lines);
        Assert.True(log.IsExpanded);
        Assert.True(log.AutoScroll);
    }

    /// <summary>Accumulates appended lines in order.</summary>
    [Fact]
    public void AppendLineAccumulatesLinesInOrder()
    {
        var log = new LogViewModel(_ => { });

        log.AppendLine("first");
        log.AppendLine("second");

        Assert.Equal(["first", "second"], log.Lines);
    }

    /// <summary>Empties the accumulated lines.</summary>
    [Fact]
    public void ClearEmptiesTheAccumulatedLines()
    {
        var log = new LogViewModel(_ => { });
        log.AppendLine("first");

        log.Clear();

        Assert.Empty(log.Lines);
    }

    /// <summary>Executing the Clear command empties the accumulated lines the same way <see cref="LogViewModel.Clear"/> does.</summary>
    [Fact]
    public void ClearCommandEmptiesTheAccumulatedLines()
    {
        var log = new LogViewModel(_ => { });
        log.AppendLine("first");

        log.ClearCommand.Execute(null);

        Assert.Empty(log.Lines);
    }

    /// <summary>Copies every line, joined by newlines, to the clipboard.</summary>
    [Fact]
    public void CopyAllCommandWritesTheJoinedLinesToTheClipboard()
    {
        string? copiedText = null;
        var log = new LogViewModel(text => copiedText = text);
        log.AppendLine("first");
        log.AppendLine("second");

        log.CopyAllCommand.Execute(null);

        Assert.Equal($"first{Environment.NewLine}second", copiedText);
    }

    /// <summary>Copies an empty string when there are no accumulated lines.</summary>
    [Fact]
    public void CopyAllCommandWritesAnEmptyStringWhenThereAreNoLines()
    {
        string? copiedText = null;
        var log = new LogViewModel(text => copiedText = text);

        log.CopyAllCommand.Execute(null);

        Assert.Equal(string.Empty, copiedText);
    }

    /// <summary>
    /// Swallows a transient clipboard-access failure (another process briefly holding the clipboard, a
    /// real and fairly common Windows condition) rather than crashing the application.
    /// </summary>
    [Fact]
    public void CopyAllCommandSwallowsAClipboardAccessFailure()
    {
        var log = new LogViewModel(_ => throw new ExternalException("clipboard busy"));
        log.AppendLine("first");

        Exception? thrown = Record.Exception(() => log.CopyAllCommand.Execute(null));

        Assert.Null(thrown);
    }
}
