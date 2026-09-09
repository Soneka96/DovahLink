using System.Collections.ObjectModel;

namespace DovahLink.DovahLinkBuilder.Ui;

/// <summary>
/// Owns the Build page's log panel: accumulated build/packaging output, collapsible display, and
/// clear/copy-all actions. Never cleared by a build's own status transitions (correction #6) --
/// only an explicit <see cref="ClearCommand"/> or the start of a new build clears it.
/// </summary>
public sealed class LogViewModel : ObservableObject
{
    /// <summary>The backing collection for <see cref="Lines"/>.</summary>
    private readonly ObservableCollection<string> lines = [];

    /// <summary>Writes text to the system clipboard for <see cref="CopyAllCommand"/>.</summary>
    private readonly Action<string> setClipboardText;

    /// <summary>The backing field for <see cref="IsExpanded"/>.</summary>
    private bool isExpanded = true;

    /// <summary>The backing field for <see cref="AutoScroll"/>.</summary>
    private bool autoScroll = true;

    /// <summary>Initializes the log panel, writing copied text to the real system clipboard.</summary>
    public LogViewModel()
        : this(System.Windows.Clipboard.SetText)
    {
    }

    /// <summary>Initializes the log panel with a controllable clipboard-write seam.</summary>
    /// <param name="setClipboardText">Writes text to the clipboard for <see cref="CopyAllCommand"/>.</param>
    internal LogViewModel(Action<string> setClipboardText)
    {
        this.setClipboardText = setClipboardText;
        ClearCommand = new RelayCommand(Clear);
        CopyAllCommand = new RelayCommand(OnCopyAll);
    }

    /// <summary>Gets the accumulated log lines, oldest first.</summary>
    public IReadOnlyList<string> Lines => lines;

    /// <summary>Gets or sets whether the log panel is expanded.</summary>
    public bool IsExpanded
    {
        get => isExpanded;
        set => SetProperty(ref isExpanded, value);
    }

    /// <summary>
    /// Gets or sets whether the log panel scrolls to the newest line automatically. Defaults to
    /// <see cref="Persistence.BuilderSettings.AutoScrollLogs"/>'s own default.
    /// </summary>
    public bool AutoScroll
    {
        get => autoScroll;
        set => SetProperty(ref autoScroll, value);
    }

    /// <summary>Gets the command that clears the log panel.</summary>
    public RelayCommand ClearCommand { get; }

    /// <summary>Gets the command that copies every log line to the clipboard.</summary>
    public RelayCommand CopyAllCommand { get; }

    /// <summary>Appends one line to the log panel.</summary>
    /// <param name="line">The line to append.</param>
    public void AppendLine(string line)
    {
        lines.Add(line);
    }

    /// <summary>Clears every accumulated log line.</summary>
    public void Clear()
    {
        lines.Clear();
    }

    /// <summary>Copies every log line, joined by newlines, to the clipboard.</summary>
    private void OnCopyAll()
    {
        setClipboardText(string.Join(Environment.NewLine, lines));
    }
}
