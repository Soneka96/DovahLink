using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.InteropServices;

namespace DovahLink.DovahLinkBuilder.Ui;

/// <summary>
/// Owns the Build page's log panel: accumulated build/packaging output, collapsible display, and
/// clear/copy-all actions. Never cleared by a build's own status transitions -- only an explicit
/// <see cref="ClearCommand"/> or the start of a new build clears it.
/// </summary>
public interface ILogViewModel : INotifyPropertyChanged
{
    /// <summary>Gets the accumulated log lines, oldest first.</summary>
    IReadOnlyList<string> Lines { get; }

    /// <summary>Gets or sets whether the log panel is expanded.</summary>
    bool IsExpanded { get; set; }

    /// <summary>
    /// Gets or sets whether the log panel scrolls to the newest line automatically. Defaults to
    /// <see cref="Persistence.BuilderSettings.AutoScrollLogs"/>'s own default.
    /// </summary>
    bool AutoScroll { get; set; }

    /// <summary>Gets the command that clears the log panel.</summary>
    RelayCommand ClearCommand { get; }

    /// <summary>Gets the command that copies every log line to the clipboard.</summary>
    RelayCommand CopyAllCommand { get; }

    /// <summary>Appends one line to the log panel.</summary>
    /// <param name="line">The line to append.</param>
    void AppendLine(string line);

    /// <summary>Clears every accumulated log line.</summary>
    void Clear();
}

/// <inheritdoc cref="ILogViewModel"/>
public sealed class LogViewModel : ObservableObject, ILogViewModel
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

    /// <inheritdoc/>
    public IReadOnlyList<string> Lines => lines;

    /// <inheritdoc/>
    public bool IsExpanded
    {
        get => isExpanded;
        set => SetProperty(ref isExpanded, value);
    }

    /// <inheritdoc/>
    public bool AutoScroll
    {
        get => autoScroll;
        set => SetProperty(ref autoScroll, value);
    }

    /// <inheritdoc/>
    public RelayCommand ClearCommand { get; }

    /// <inheritdoc/>
    public RelayCommand CopyAllCommand { get; }

    /// <inheritdoc/>
    public void AppendLine(string line)
    {
        lines.Add(line);
    }

    /// <inheritdoc/>
    public void Clear()
    {
        lines.Clear();
    }

    /// <summary>
    /// Copies every log line, joined by newlines, to the clipboard. A failure here (for example
    /// another process briefly holding clipboard access, a real and fairly common Windows condition)
    /// is a convenience-action failure and never changes any reported state.
    /// </summary>
    private void OnCopyAll()
    {
        try
        {
            setClipboardText(string.Join(Environment.NewLine, lines));
        }
        catch (ExternalException)
        {
            // Another process briefly holding clipboard access is a normal, transient Windows
            // condition; a failure here must not crash the application or change any reported state.
        }
    }
}
