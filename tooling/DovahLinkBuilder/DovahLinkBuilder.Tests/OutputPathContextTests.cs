using DovahLink.DovahLinkBuilder.Ui;

namespace DovahLink.DovahLinkBuilder.Tests;

/// <summary>Verifies the shared output path override's initial value, update, and change notification.</summary>
public sealed class OutputPathContextTests
{
    /// <summary>Starts with the output path passed to the constructor.</summary>
    [Fact]
    public void StartsWithTheConstructorSuppliedOutputPath()
    {
        var context = new OutputPathContext(@"C:\out");

        Assert.Equal(@"C:\out", context.OutputPath);
    }

    /// <summary>Starts with a null output path when constructed without an override.</summary>
    [Fact]
    public void StartsWithNullWhenConstructedWithoutAnOverride()
    {
        var context = new OutputPathContext(null);

        Assert.Null(context.OutputPath);
    }

    /// <summary>Updates the reported output path when it changes.</summary>
    [Fact]
    public void SetOutputPathUpdatesTheReportedOutputPath()
    {
        var context = new OutputPathContext(@"C:\out-a");

        context.SetOutputPath(@"C:\out-b");

        Assert.Equal(@"C:\out-b", context.OutputPath);
    }

    /// <summary>Clears the reported output path back to null, for reverting to the profile's own default.</summary>
    [Fact]
    public void SetOutputPathCanClearBackToNull()
    {
        var context = new OutputPathContext(@"C:\out");

        context.SetOutputPath(null);

        Assert.Null(context.OutputPath);
    }

    /// <summary>Sets a real output path override starting from no override at all.</summary>
    [Fact]
    public void SetOutputPathSetsAnOverrideStartingFromNull()
    {
        var context = new OutputPathContext(null);

        context.SetOutputPath(@"C:\out");

        Assert.Equal(@"C:\out", context.OutputPath);
    }

    /// <summary>Raises PropertyChanged for OutputPath when it actually changes.</summary>
    [Fact]
    public void SetOutputPathRaisesPropertyChangedWhenTheValueChanges()
    {
        var context = new OutputPathContext(@"C:\out-a");
        var raisedProperties = new List<string?>();
        context.PropertyChanged += (_, e) => raisedProperties.Add(e.PropertyName);

        context.SetOutputPath(@"C:\out-b");

        Assert.Contains(nameof(OutputPathContext.OutputPath), raisedProperties);
    }

    /// <summary>Raises no notification when set to the same output path it already reports.</summary>
    [Fact]
    public void SetOutputPathRaisesNoNotificationWhenTheOutputPathIsUnchanged()
    {
        var context = new OutputPathContext(@"C:\out");
        var raisedProperties = new List<string?>();
        context.PropertyChanged += (_, e) => raisedProperties.Add(e.PropertyName);

        context.SetOutputPath(@"C:\out");

        Assert.Empty(raisedProperties);
    }

    /// <summary>Raises no notification when set to null while it already reports null.</summary>
    [Fact]
    public void SetOutputPathRaisesNoNotificationWhenAlreadyNull()
    {
        var context = new OutputPathContext(null);
        var raisedProperties = new List<string?>();
        context.PropertyChanged += (_, e) => raisedProperties.Add(e.PropertyName);

        context.SetOutputPath(null);

        Assert.Empty(raisedProperties);
    }
}
