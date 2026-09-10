using DovahLink.DovahLinkBuilder.Ui;

namespace DovahLink.DovahLinkBuilder.Tests;

/// <summary>Verifies the shared repository root's initial value, update, and change notification.</summary>
public sealed class RepositoryContextTests
{
    /// <summary>Starts with the repository root passed to the constructor.</summary>
    [Fact]
    public void StartsWithTheConstructorSuppliedRoot()
    {
        var context = new RepositoryContext(@"C:\repo");

        Assert.Equal(@"C:\repo", context.RepositoryRoot);
    }

    /// <summary>Updates the reported root when it changes.</summary>
    [Fact]
    public void SetRepositoryRootUpdatesTheReportedRoot()
    {
        var context = new RepositoryContext(@"C:\repo-a");

        context.SetRepositoryRoot(@"C:\repo-b");

        Assert.Equal(@"C:\repo-b", context.RepositoryRoot);
    }

    /// <summary>Raises PropertyChanged for RepositoryRoot when it actually changes.</summary>
    [Fact]
    public void SetRepositoryRootRaisesPropertyChangedWhenTheValueChanges()
    {
        var context = new RepositoryContext(@"C:\repo-a");
        var raisedProperties = new List<string?>();
        context.PropertyChanged += (_, e) => raisedProperties.Add(e.PropertyName);

        context.SetRepositoryRoot(@"C:\repo-b");

        Assert.Contains(nameof(RepositoryContext.RepositoryRoot), raisedProperties);
    }

    /// <summary>Raises no notification when set to the same root it already reports.</summary>
    [Fact]
    public void SetRepositoryRootRaisesNoNotificationWhenTheRootIsUnchanged()
    {
        var context = new RepositoryContext(@"C:\repo");
        var raisedProperties = new List<string?>();
        context.PropertyChanged += (_, e) => raisedProperties.Add(e.PropertyName);

        context.SetRepositoryRoot(@"C:\repo");

        Assert.Empty(raisedProperties);
    }
}
