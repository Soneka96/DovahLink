using DovahLink.DovahLinkBuilder.Ui;

namespace DovahLink.DovahLinkBuilder.Tests;

/// <summary>Verifies the shared runtime build settings' initial values, updates, and change notification.</summary>
public sealed class RuntimeBuildSettingsContextTests
{
    /// <summary>Starts with the values passed to the constructor.</summary>
    [Fact]
    public void StartsWithTheConstructorSuppliedValues()
    {
        var context = new RuntimeBuildSettingsContext(openOutputFolderAfterSuccessfulBuild: true, autoScrollLogs: false);

        Assert.True(context.OpenOutputFolderAfterSuccessfulBuild);
        Assert.False(context.AutoScrollLogs);
    }

    /// <summary>Updates the reported value when OpenOutputFolderAfterSuccessfulBuild changes.</summary>
    [Fact]
    public void SetOpenOutputFolderAfterSuccessfulBuildUpdatesTheReportedValue()
    {
        var context = new RuntimeBuildSettingsContext(openOutputFolderAfterSuccessfulBuild: true, autoScrollLogs: true);

        context.SetOpenOutputFolderAfterSuccessfulBuild(false);

        Assert.False(context.OpenOutputFolderAfterSuccessfulBuild);
    }

    /// <summary>Updates the reported value when AutoScrollLogs changes, without affecting OpenOutputFolderAfterSuccessfulBuild.</summary>
    [Fact]
    public void SetAutoScrollLogsUpdatesTheReportedValueOnly()
    {
        var context = new RuntimeBuildSettingsContext(openOutputFolderAfterSuccessfulBuild: true, autoScrollLogs: true);

        context.SetAutoScrollLogs(false);

        Assert.False(context.AutoScrollLogs);
        Assert.True(context.OpenOutputFolderAfterSuccessfulBuild);
    }

    /// <summary>Raises PropertyChanged for OpenOutputFolderAfterSuccessfulBuild only, when it actually changes.</summary>
    [Fact]
    public void SetOpenOutputFolderAfterSuccessfulBuildRaisesPropertyChangedForThatPropertyOnly()
    {
        var context = new RuntimeBuildSettingsContext(openOutputFolderAfterSuccessfulBuild: true, autoScrollLogs: true);
        var raisedProperties = new List<string?>();
        context.PropertyChanged += (_, e) => raisedProperties.Add(e.PropertyName);

        context.SetOpenOutputFolderAfterSuccessfulBuild(false);

        Assert.Contains(nameof(RuntimeBuildSettingsContext.OpenOutputFolderAfterSuccessfulBuild), raisedProperties);
        Assert.DoesNotContain(nameof(RuntimeBuildSettingsContext.AutoScrollLogs), raisedProperties);
    }

    /// <summary>Raises PropertyChanged for AutoScrollLogs only, when it actually changes.</summary>
    [Fact]
    public void SetAutoScrollLogsRaisesPropertyChangedForThatPropertyOnly()
    {
        var context = new RuntimeBuildSettingsContext(openOutputFolderAfterSuccessfulBuild: true, autoScrollLogs: true);
        var raisedProperties = new List<string?>();
        context.PropertyChanged += (_, e) => raisedProperties.Add(e.PropertyName);

        context.SetAutoScrollLogs(false);

        Assert.Contains(nameof(RuntimeBuildSettingsContext.AutoScrollLogs), raisedProperties);
        Assert.DoesNotContain(nameof(RuntimeBuildSettingsContext.OpenOutputFolderAfterSuccessfulBuild), raisedProperties);
    }

    /// <summary>Raises no notification when OpenOutputFolderAfterSuccessfulBuild is set to the value it already reports.</summary>
    [Fact]
    public void SetOpenOutputFolderAfterSuccessfulBuildRaisesNoNotificationWhenUnchanged()
    {
        var context = new RuntimeBuildSettingsContext(openOutputFolderAfterSuccessfulBuild: true, autoScrollLogs: true);
        var raisedProperties = new List<string?>();
        context.PropertyChanged += (_, e) => raisedProperties.Add(e.PropertyName);

        context.SetOpenOutputFolderAfterSuccessfulBuild(true);

        Assert.Empty(raisedProperties);
    }

    /// <summary>Raises no notification when AutoScrollLogs is set to the value it already reports.</summary>
    [Fact]
    public void SetAutoScrollLogsRaisesNoNotificationWhenUnchanged()
    {
        var context = new RuntimeBuildSettingsContext(openOutputFolderAfterSuccessfulBuild: true, autoScrollLogs: true);
        var raisedProperties = new List<string?>();
        context.PropertyChanged += (_, e) => raisedProperties.Add(e.PropertyName);

        context.SetAutoScrollLogs(true);

        Assert.Empty(raisedProperties);
    }
}
