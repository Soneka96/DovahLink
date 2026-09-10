using System.Collections.Specialized;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;

namespace DovahLink.DovahLinkBuilder.Ui;

/// <summary>The Build page's view: preflight/git gating, the pipeline, and the log panel.</summary>
public partial class BuildPage : UserControl
{
    /// <summary>Initializes the view and wires log auto-scroll to whichever ViewModel is bound.</summary>
    public BuildPage()
    {
        InitializeComponent();
        DataContextChanged += OnDataContextChanged;
    }

    /// <summary>Re-subscribes log auto-scroll to the newly bound ViewModel's log lines.</summary>
    /// <param name="sender">The unused event source.</param>
    /// <param name="e">Carries the previous and new <see cref="FrameworkElement.DataContext"/> values.</param>
    private void OnDataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (e.OldValue is BuildPageViewModel oldViewModel)
        {
            ((INotifyCollectionChanged)oldViewModel.Log.Lines).CollectionChanged -= OnLogLinesChanged;
        }

        if (e.NewValue is BuildPageViewModel newViewModel)
        {
            ((INotifyCollectionChanged)newViewModel.Log.Lines).CollectionChanged += OnLogLinesChanged;
        }
    }

    /// <summary>
    /// Schedules a scroll to the log list's newest line when auto-scroll is enabled. Deferred to a
    /// background-priority dispatcher callback rather than run inline here: <see cref="ListBox.ScrollIntoView"/>
    /// pumps a nested layout pass, and running it synchronously inside this <see
    /// cref="INotifyCollectionChanged.CollectionChanged"/> handler let that nested pump dispatch another
    /// pending collection add while <see cref="LogListBox"/>'s item generator was still processing the
    /// current one, desyncing the generator's tracked count from the collection.
    /// </summary>
    /// <param name="sender">The unused event source.</param>
    /// <param name="e">The unused collection-change details; every change scrolls to the current last line.</param>
    private void OnLogLinesChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        Dispatcher.BeginInvoke(ScrollToLastLineIfAutoScrolling, DispatcherPriority.Background);
    }

    /// <summary>
    /// Scrolls the log list to its newest line, re-checking auto-scroll and the current line count since
    /// this runs after a deferral during which either may have changed.
    /// </summary>
    private void ScrollToLastLineIfAutoScrolling()
    {
        if (DataContext is BuildPageViewModel { Log.AutoScroll: true } && LogListBox.Items.Count > 0)
        {
            LogListBox.ScrollIntoView(LogListBox.Items[^1]);
        }
    }
}
