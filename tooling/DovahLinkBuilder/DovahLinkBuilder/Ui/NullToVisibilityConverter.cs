using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace DovahLink.DovahLinkBuilder.Ui;

/// <summary>Converts <see langword="null"/> (or an empty string) to <see cref="Visibility.Collapsed"/>; any other value to <see cref="Visibility.Visible"/>.</summary>
public sealed class NullToVisibilityConverter : IValueConverter
{
    /// <inheritdoc/>
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is null or "" ? Visibility.Collapsed : Visibility.Visible;

    /// <inheritdoc/>
    /// <exception cref="NotSupportedException">Always thrown; this converter is one-way.</exception>
    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
