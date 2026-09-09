using System.Globalization;
using System.Windows.Data;
using System.Windows.Media;

namespace DovahLink.DovahLinkBuilder.Ui;

/// <summary>Colors a log line's text: the theme's "bad" color for a line reporting an error, the secondary text color otherwise.</summary>
public sealed class LogLineBrushConverter : IValueConverter
{
    /// <inheritdoc/>
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        bool isError = value is string line && line.Contains("error", StringComparison.OrdinalIgnoreCase);
        return isError
            ? (Brush)System.Windows.Application.Current.Resources["BadBrush"]
            : (Brush)System.Windows.Application.Current.Resources["TextSecBrush"];
    }

    /// <inheritdoc/>
    /// <exception cref="NotSupportedException">Always thrown; this converter is one-way.</exception>
    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
