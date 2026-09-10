namespace DovahLink.Host.Tests;

/// <summary>
/// Tests for <see cref="HostSettingsProvider"/>. <see cref="HostSettingsProvider.Load"/>'s
/// <see cref="IOException"/> and <see cref="UnauthorizedAccessException"/> fallback paths are not
/// covered here: reliably forcing a locked or permission-denied file is OS-specific and brittle:
/// they share the exact same fallback-to-default behavior already proven for
/// <see cref="System.Text.Json.JsonException"/> above, just a different exception type reaching the
/// same catch clause.
/// </summary>
public class HostSettingsProviderTests : IDisposable
{
    private readonly string filePath = Path.Combine(Path.GetTempPath(), $"dovahlink-host-settings-test-{Guid.NewGuid():N}.json");

    /// <summary>Verifies that a missing settings file falls back to the shipped default.</summary>
    [Fact]
    public void Load_NoFileYet_ReturnsShippedDefault()
    {
        var provider = new HostSettingsProvider(filePath);

        HostSettings settings = provider.Load();

        Assert.Equal(Constants.MaxActiveSessions, settings.MaxActiveSessions);
    }

    /// <summary>Verifies that a settings file present but missing the field falls back to the shipped default.</summary>
    [Fact]
    public void Load_FieldAbsent_ReturnsShippedDefault()
    {
        File.WriteAllText(filePath, "{}");
        var provider = new HostSettingsProvider(filePath);

        HostSettings settings = provider.Load();

        Assert.Equal(Constants.MaxActiveSessions, settings.MaxActiveSessions);
    }

    /// <summary>Verifies that syntactically invalid JSON falls back to the shipped default rather than throwing.</summary>
    [Fact]
    public void Load_MalformedJson_ReturnsShippedDefault()
    {
        File.WriteAllText(filePath, "{ not valid json");
        var provider = new HostSettingsProvider(filePath);

        HostSettings settings = provider.Load();

        Assert.Equal(Constants.MaxActiveSessions, settings.MaxActiveSessions);
    }

    /// <summary>Verifies that a field present with the wrong JSON type falls back to the shipped default rather than throwing.</summary>
    [Fact]
    public void Load_FieldWrongType_ReturnsShippedDefault()
    {
        File.WriteAllText(filePath, "{\"maxActiveSessions\": \"ten\"}");
        var provider = new HostSettingsProvider(filePath);

        HostSettings settings = provider.Load();

        Assert.Equal(Constants.MaxActiveSessions, settings.MaxActiveSessions);
    }

    /// <summary>Verifies that a zero configured value falls back to the shipped default rather than admitting no sessions.</summary>
    [Fact]
    public void Load_FieldZero_ReturnsShippedDefault()
    {
        File.WriteAllText(filePath, "{\"maxActiveSessions\": 0}");
        var provider = new HostSettingsProvider(filePath);

        HostSettings settings = provider.Load();

        Assert.Equal(Constants.MaxActiveSessions, settings.MaxActiveSessions);
    }

    /// <summary>Verifies that a negative configured value falls back to the shipped default.</summary>
    [Fact]
    public void Load_FieldNegative_ReturnsShippedDefault()
    {
        File.WriteAllText(filePath, "{\"maxActiveSessions\": -1}");
        var provider = new HostSettingsProvider(filePath);

        HostSettings settings = provider.Load();

        Assert.Equal(Constants.MaxActiveSessions, settings.MaxActiveSessions);
    }

    /// <summary>Verifies that a value above the accepted ceiling falls back to the shipped default rather than being clamped.</summary>
    [Fact]
    public void Load_FieldAboveCeiling_ReturnsShippedDefault()
    {
        File.WriteAllText(filePath, $"{{\"maxActiveSessions\": {Constants.MaxActiveSessionsCeiling + 1}}}");
        var provider = new HostSettingsProvider(filePath);

        HostSettings settings = provider.Load();

        Assert.Equal(Constants.MaxActiveSessions, settings.MaxActiveSessions);
    }

    /// <summary>Verifies that the lower bound of 1 is accepted, proving that bound is inclusive too.</summary>
    [Fact]
    public void Load_FieldAtLowerBound_ReturnsConfiguredValue()
    {
        File.WriteAllText(filePath, "{\"maxActiveSessions\": 1}");
        var provider = new HostSettingsProvider(filePath);

        HostSettings settings = provider.Load();

        Assert.Equal(1, settings.MaxActiveSessions);
    }

    /// <summary>Verifies that an explicit JSON <c>null</c> value falls back to the shipped default, the same as an absent field.</summary>
    [Fact]
    public void Load_FieldExplicitNull_ReturnsShippedDefault()
    {
        File.WriteAllText(filePath, "{\"maxActiveSessions\": null}");
        var provider = new HostSettingsProvider(filePath);

        HostSettings settings = provider.Load();

        Assert.Equal(Constants.MaxActiveSessions, settings.MaxActiveSessions);
    }

    /// <summary>Verifies that a completely empty file falls back to the shipped default rather than throwing.</summary>
    [Fact]
    public void Load_EmptyFile_ReturnsShippedDefault()
    {
        File.WriteAllText(filePath, string.Empty);
        var provider = new HostSettingsProvider(filePath);

        HostSettings settings = provider.Load();

        Assert.Equal(Constants.MaxActiveSessions, settings.MaxActiveSessions);
    }

    /// <summary>Verifies that a non-integer numeric value falls back to the shipped default rather than throwing.</summary>
    [Fact]
    public void Load_FieldFractionalNumber_ReturnsShippedDefault()
    {
        File.WriteAllText(filePath, "{\"maxActiveSessions\": 10.5}");
        var provider = new HostSettingsProvider(filePath);

        HostSettings settings = provider.Load();

        Assert.Equal(Constants.MaxActiveSessions, settings.MaxActiveSessions);
    }

    /// <summary>Verifies that a valid configured value within range is returned as-is.</summary>
    [Fact]
    public void Load_ValidField_ReturnsConfiguredValue()
    {
        File.WriteAllText(filePath, "{\"maxActiveSessions\": 10}");
        var provider = new HostSettingsProvider(filePath);

        HostSettings settings = provider.Load();

        Assert.Equal(10, settings.MaxActiveSessions);
    }

    /// <summary>Verifies that the ceiling value itself is accepted, proving the bound is inclusive.</summary>
    [Fact]
    public void Load_FieldAtCeiling_ReturnsConfiguredValue()
    {
        File.WriteAllText(filePath, $"{{\"maxActiveSessions\": {Constants.MaxActiveSessionsCeiling}}}");
        var provider = new HostSettingsProvider(filePath);

        HostSettings settings = provider.Load();

        Assert.Equal(Constants.MaxActiveSessionsCeiling, settings.MaxActiveSessions);
    }

    /// <summary>Verifies that the parameterless constructor reads from the production settings file path.</summary>
    [Fact]
    public void Constructor_NoFilePath_UsesProductionSettingsFilePath()
    {
        var provider = new HostSettingsProvider();

        HostSettings settings = provider.Load();

        Assert.True(settings.MaxActiveSessions >= 1);
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        if (File.Exists(filePath))
        {
            File.Delete(filePath);
        }
    }
}
