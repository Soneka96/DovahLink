using System.Collections.Specialized;
using System.Windows;
using System.Windows.Controls;

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

    /// <summary>Scrolls the log list to its newest line when auto-scroll is enabled.</summary>
    /// <param name="sender">The unused event source.</param>
    /// <param name="e">The unused collection-change details; every change scrolls to the current last line.</param>
    private void OnLogLinesChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (DataContext is BuildPageViewModel { Log.AutoScroll: true } && LogListBox.Items.Count > 0)
        {
            LogListBox.ScrollIntoView(LogListBox.Items[^1]);
        }
    }
}
