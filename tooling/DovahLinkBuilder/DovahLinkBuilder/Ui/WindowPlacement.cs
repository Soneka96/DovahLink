using System.Windows;

namespace DovahLink.DovahLinkBuilder.Ui;

/// <summary>Resolves the main window's saved bounds against the current display configuration.</summary>
public static class WindowPlacement
{
    /// <summary>
    /// Gets the saved window bounds to apply at startup, or <see langword="null"/> when no bounds were
    /// ever saved, the saved width or height is not positive (a corrupt or hand-edited settings file),
    /// or the saved bounds do not intersect <paramref name="virtualScreenBounds"/> at all (per
    /// <see cref="Rect.IntersectsWith"/>, which treats a shared edge as intersecting) -- for example
    /// after disconnecting the monitor the window was last on. The window then keeps its
    /// markup-declared default size and OS-determined position instead of restoring an unreachable
    /// position.
    /// </summary>
    /// <param name="savedLeft">The saved window left position, or <see langword="null"/> when never saved.</param>
    /// <param name="savedTop">The saved window top position, or <see langword="null"/> when never saved.</param>
    /// <param name="savedWidth">The saved window width, or <see langword="null"/> when never saved.</param>
    /// <param name="savedHeight">The saved window height, or <see langword="null"/> when never saved.</param>
    /// <param name="virtualScreenBounds">The current virtual screen's bounds, spanning every connected monitor.</param>
    /// <returns>The resolved bounds to apply to the window, or <see langword="null"/> to leave it at its default.</returns>
    public static Rect? Resolve(double? savedLeft, double? savedTop, double? savedWidth, double? savedHeight, Rect virtualScreenBounds)
    {
        if (savedLeft is not { } left || savedTop is not { } top || savedWidth is not { } width || savedHeight is not { } height
            || width <= 0 || height <= 0)
        {
            return null;
        }

        var saved = new Rect(left, top, width, height);
        return virtualScreenBounds.IntersectsWith(saved) ? saved : null;
    }
}
