using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace DovahLink.DovahLinkBuilder.Ui;

/// <summary>Base class for ViewModels that raises <see cref="PropertyChanged"/> only when a property's value actually changes.</summary>
public abstract class ObservableObject : INotifyPropertyChanged
{
    /// <inheritdoc/>
    public event PropertyChangedEventHandler? PropertyChanged;

    /// <summary>
    /// Sets <paramref name="field"/> to <paramref name="value"/> and raises <see cref="PropertyChanged"/>
    /// when the new value differs from the current one.
    /// </summary>
    /// <typeparam name="T">The property's type.</typeparam>
    /// <param name="field">A reference to the backing field.</param>
    /// <param name="value">The value to assign.</param>
    /// <param name="propertyName">
    /// The name of the changed property; supplied automatically by the compiler from the calling
    /// property when omitted.
    /// </param>
    /// <returns><see langword="true"/> if the value changed and <see cref="PropertyChanged"/> was raised.</returns>
    protected bool SetProperty<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value))
        {
            return false;
        }

        field = value;
        OnPropertyChanged(propertyName);
        return true;
    }

    /// <summary>Raises <see cref="PropertyChanged"/> for the named property.</summary>
    /// <param name="propertyName">
    /// The name of the changed property; supplied automatically by the compiler from the calling
    /// property when omitted.
    /// </param>
    protected void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}
