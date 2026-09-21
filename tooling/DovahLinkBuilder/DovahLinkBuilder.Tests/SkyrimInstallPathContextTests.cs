using DovahLink.DovahLinkBuilder.Ui;

namespace DovahLink.DovahLinkBuilder.Tests;

/// <summary>Verifies the shared Skyrim install path context.</summary>
public sealed class SkyrimInstallPathContextTests
{
    /// <summary>Stores the selected path and notifies consumers when it changes.</summary>
    [Fact]
    public void SetSkyrimInstallPathUpdatesTheValueAndRaisesPropertyChanged()
    {
        var context = new SkyrimInstallPathContext(null);
        var raisedProperties = new List<string?>();
        context.PropertyChanged += (_, e) => raisedProperties.Add(e.PropertyName);

        context.SetSkyrimInstallPath(@"D:\Skyrim Special Edition");

        Assert.Equal(@"D:\Skyrim Special Edition", context.SkyrimInstallPath);
        Assert.Equal([nameof(SkyrimInstallPathContext.SkyrimInstallPath)], raisedProperties);
    }

    /// <summary>Does not notify consumers when the selected path is unchanged.</summary>
    [Fact]
    public void SetSkyrimInstallPathDoesNotRaisePropertyChangedForTheSameValue()
    {
        var context = new SkyrimInstallPathContext(@"D:\Skyrim Special Edition");
        var raisedProperties = new List<string?>();
        context.PropertyChanged += (_, e) => raisedProperties.Add(e.PropertyName);

        context.SetSkyrimInstallPath(@"D:\Skyrim Special Edition");

        Assert.Empty(raisedProperties);
    }
}
