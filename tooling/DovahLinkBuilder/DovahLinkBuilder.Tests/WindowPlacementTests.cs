using System.Windows;
using DovahLink.DovahLinkBuilder.Ui;

namespace DovahLink.DovahLinkBuilder.Tests;

/// <summary>Verifies <see cref="WindowPlacement"/>'s saved-bounds resolution against the current virtual screen.</summary>
public sealed class WindowPlacementTests
{
    /// <summary>The virtual screen every test resolves against, unless a test overrides it.</summary>
    private static readonly Rect VirtualScreenBounds = new(0, 0, 1920, 1080);

    /// <summary>Returns null when no bounds were ever saved.</summary>
    [Fact]
    public void ResolveReturnsNullWhenNoBoundsWereSaved()
    {
        Rect? resolved = WindowPlacement.Resolve(null, null, null, null, VirtualScreenBounds);

        Assert.Null(resolved);
    }

    /// <summary>Returns null when any one of the four saved values is missing.</summary>
    /// <param name="savedLeft">The saved left position to resolve with.</param>
    /// <param name="savedTop">The saved top position to resolve with.</param>
    /// <param name="savedWidth">The saved width to resolve with.</param>
    /// <param name="savedHeight">The saved height to resolve with.</param>
    [Theory]
    [InlineData(null, 100d, 980d, 700d)]
    [InlineData(100d, null, 980d, 700d)]
    [InlineData(100d, 100d, null, 700d)]
    [InlineData(100d, 100d, 980d, null)]
    public void ResolveReturnsNullWhenAnySavedValueIsMissing(double? savedLeft, double? savedTop, double? savedWidth, double? savedHeight)
    {
        Rect? resolved = WindowPlacement.Resolve(savedLeft, savedTop, savedWidth, savedHeight, VirtualScreenBounds);

        Assert.Null(resolved);
    }

    /// <summary>Returns the saved bounds unchanged when they sit fully inside the current virtual screen.</summary>
    [Fact]
    public void ResolveReturnsTheSavedBoundsWhenFullyInsideTheVirtualScreen()
    {
        Rect? resolved = WindowPlacement.Resolve(100, 150, 980, 700, VirtualScreenBounds);

        Assert.Equal(new Rect(100, 150, 980, 700), resolved);
    }

    /// <summary>Returns the saved bounds unchanged when they only partially overlap the current virtual screen.</summary>
    [Fact]
    public void ResolveReturnsTheSavedBoundsWhenPartiallyOverlappingTheVirtualScreen()
    {
        Rect? resolved = WindowPlacement.Resolve(-100, -100, 980, 700, VirtualScreenBounds);

        Assert.Equal(new Rect(-100, -100, 980, 700), resolved);
    }

    /// <summary>Returns null when the saved bounds fall entirely outside the current virtual screen, for example after disconnecting a monitor.</summary>
    [Fact]
    public void ResolveReturnsNullWhenTheSavedBoundsAreFullyOutsideTheVirtualScreen()
    {
        Rect? resolved = WindowPlacement.Resolve(3000, 3000, 980, 700, VirtualScreenBounds);

        Assert.Null(resolved);
    }

    /// <summary>
    /// Returns the saved bounds unchanged when they only share an edge with the virtual screen and no
    /// interior area, matching <see cref="Rect.IntersectsWith"/>'s own inclusive edge behavior.
    /// </summary>
    [Fact]
    public void ResolveReturnsTheSavedBoundsWhenTheyOnlyShareTheVirtualScreenEdge()
    {
        Rect? resolved = WindowPlacement.Resolve(-980, 0, 980, 700, VirtualScreenBounds);

        Assert.Equal(new Rect(-980, 0, 980, 700), resolved);
    }

    /// <summary>Returns null for a non-positive saved width or height, rather than throwing while constructing the resolved rectangle.</summary>
    /// <param name="savedWidth">The saved width to resolve with.</param>
    /// <param name="savedHeight">The saved height to resolve with.</param>
    [Theory]
    [InlineData(0, 700)]
    [InlineData(-1, 700)]
    [InlineData(980, 0)]
    [InlineData(980, -1)]
    public void ResolveReturnsNullForANonPositiveSavedWidthOrHeight(double savedWidth, double savedHeight)
    {
        Rect? resolved = WindowPlacement.Resolve(100, 100, savedWidth, savedHeight, VirtualScreenBounds);

        Assert.Null(resolved);
    }
}
