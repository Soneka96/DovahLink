using System.Windows.Input;

namespace DovahLink.DovahLinkBuilder.Ui;

/// <summary>An <see cref="ICommand"/> that delegates execution and eligibility to supplied delegates.</summary>
public sealed class RelayCommand : ICommand
{
    /// <summary>The action run when the command executes.</summary>
    private readonly Action execute;

    /// <summary>The optional predicate controlling whether the command can currently execute.</summary>
    private readonly Func<bool>? canExecute;

    /// <summary>Initializes a command over the supplied execution delegate.</summary>
    /// <param name="execute">The action to run when the command executes.</param>
    /// <param name="canExecute">
    /// An optional predicate controlling whether the command can currently execute; when omitted the
    /// command is always eligible.
    /// </param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="execute"/> is <see langword="null"/>.</exception>
    public RelayCommand(Action execute, Func<bool>? canExecute = null)
    {
        this.execute = execute ?? throw new ArgumentNullException(nameof(execute));
        this.canExecute = canExecute;
    }

    /// <inheritdoc/>
    public event EventHandler? CanExecuteChanged;

    /// <inheritdoc/>
    /// <remarks><paramref name="parameter"/> is not used; every command in this application is parameterless.</remarks>
    public bool CanExecute(object? parameter) => canExecute?.Invoke() ?? true;

    /// <inheritdoc/>
    /// <remarks><paramref name="parameter"/> is not used; every command in this application is parameterless.</remarks>
    public void Execute(object? parameter) => execute();

    /// <summary>Raises <see cref="CanExecuteChanged"/> so bound controls re-query <see cref="CanExecute"/>.</summary>
    public void RaiseCanExecuteChanged() => CanExecuteChanged?.Invoke(this, EventArgs.Empty);
}
