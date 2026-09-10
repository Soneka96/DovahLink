using DovahLink.DovahLinkBuilder.Build;

namespace DovahLink.DovahLinkBuilder.Tests;

/// <summary>Verifies <see cref="BuildOutputOwnershipGuard"/> against real filesystem state.</summary>
public sealed class BuildOutputOwnershipGuardTests
{
    /// <summary>Trusts the repository's own default output root without requiring a marker, even when it already has unrelated content.</summary>
    [Fact]
    public void EnsureOwnedTrustsTheDefaultOutputRootWithoutAMarker()
    {
        using var temporaryDirectory = new TemporaryDirectory();
        string outputRoot = Path.Combine(temporaryDirectory.Path, "tooling", "out");
        Directory.CreateDirectory(outputRoot);
        File.WriteAllText(Path.Combine(outputRoot, "leftover-from-before-this-feature.txt"), "pre-existing");
        var guard = new BuildOutputOwnershipGuard();

        guard.EnsureOwned(outputRoot, temporaryDirectory.Path);

        Assert.False(File.Exists(Path.Combine(outputRoot, BuildOutputOwnershipGuard.MarkerFileName)));
    }

    /// <summary>Trusts a profile-specific subfolder of the default output root (for example <c>tooling/out/debug</c>) the same way as the root itself.</summary>
    [Fact]
    public void EnsureOwnedTrustsAProfileSubfolderOfTheDefaultOutputRoot()
    {
        using var temporaryDirectory = new TemporaryDirectory();
        string outputRoot = Path.Combine(temporaryDirectory.Path, "tooling", "out", "debug");
        var guard = new BuildOutputOwnershipGuard();

        guard.EnsureOwned(outputRoot, temporaryDirectory.Path);

        Assert.False(File.Exists(Path.Combine(outputRoot, BuildOutputOwnershipGuard.MarkerFileName)));
    }

    /// <summary>Adopts a nonexistent custom output root: creates it and marks it Builder-owned.</summary>
    [Fact]
    public void EnsureOwnedAdoptsANonexistentCustomOutputRoot()
    {
        using var temporaryDirectory = new TemporaryDirectory();
        string outputRoot = Path.Combine(temporaryDirectory.Path, "custom-out");
        var guard = new BuildOutputOwnershipGuard();

        guard.EnsureOwned(outputRoot, Path.Combine(temporaryDirectory.Path, "repo"));

        Assert.True(Directory.Exists(outputRoot));
        string markerPath = Path.Combine(outputRoot, BuildOutputOwnershipGuard.MarkerFileName);
        Assert.True(File.Exists(markerPath));
        Assert.NotEmpty(File.ReadAllText(markerPath));
    }

    /// <summary>Adopts an existing but empty custom output root, marking it Builder-owned.</summary>
    [Fact]
    public void EnsureOwnedAdoptsAnEmptyCustomOutputRoot()
    {
        using var temporaryDirectory = new TemporaryDirectory();
        string outputRoot = Path.Combine(temporaryDirectory.Path, "custom-out");
        Directory.CreateDirectory(outputRoot);
        var guard = new BuildOutputOwnershipGuard();

        guard.EnsureOwned(outputRoot, Path.Combine(temporaryDirectory.Path, "repo"));

        string markerPath = Path.Combine(outputRoot, BuildOutputOwnershipGuard.MarkerFileName);
        Assert.True(File.Exists(markerPath));
        Assert.NotEmpty(File.ReadAllText(markerPath));
    }

    /// <summary>
    /// Normalizes a filesystem exception raised while adopting a new custom output root -- here a
    /// path containing a colon outside the drive designator, which Windows rejects when the
    /// directory is actually created -- into the same documented <see cref="InvalidOperationException"/>
    /// every other output-location failure in this Builder reports, rather than letting the raw
    /// framework exception escape.
    /// </summary>
    [Fact]
    public void EnsureOwnedNormalizesAFilesystemExceptionWhileCreatingACustomOutputRoot()
    {
        using var temporaryDirectory = new TemporaryDirectory();
        string outputRoot = Path.Combine(temporaryDirectory.Path, "custom-out:bad");
        var guard = new BuildOutputOwnershipGuard();

        InvalidOperationException exception = Assert.Throws<InvalidOperationException>(
            () => guard.EnsureOwned(outputRoot, Path.Combine(temporaryDirectory.Path, "repo")));

        Assert.NotNull(exception.InnerException);
    }

    /// <summary>
    /// Normalizes a filesystem exception raised while resolving a malformed output root -- here an
    /// embedded null character, which <see cref="Path.GetFullPath(string)"/> itself rejects before
    /// any directory is touched -- into the same documented <see cref="InvalidOperationException"/>,
    /// proving the normalization covers path resolution, not only directory creation.
    /// </summary>
    [Fact]
    public void EnsureOwnedNormalizesAFilesystemExceptionWhileResolvingAMalformedOutputRoot()
    {
        using var temporaryDirectory = new TemporaryDirectory();
        string outputRoot = Path.Combine(temporaryDirectory.Path, "custom-out\0bad");
        var guard = new BuildOutputOwnershipGuard();

        InvalidOperationException exception = Assert.Throws<InvalidOperationException>(
            () => guard.EnsureOwned(outputRoot, Path.Combine(temporaryDirectory.Path, "repo")));

        Assert.IsType<ArgumentException>(exception.InnerException);
    }

    /// <summary>Treats a custom output root that already carries the ownership marker as already owned, without rewriting it.</summary>
    [Fact]
    public void EnsureOwnedAcceptsAnAlreadyMarkedCustomOutputRoot()
    {
        using var temporaryDirectory = new TemporaryDirectory();
        string outputRoot = Path.Combine(temporaryDirectory.Path, "custom-out");
        Directory.CreateDirectory(outputRoot);
        string markerPath = Path.Combine(outputRoot, BuildOutputOwnershipGuard.MarkerFileName);
        File.WriteAllText(markerPath, "already owned");
        File.WriteAllText(Path.Combine(outputRoot, "package"), "a previous build's real output");
        var guard = new BuildOutputOwnershipGuard();

        guard.EnsureOwned(outputRoot, Path.Combine(temporaryDirectory.Path, "repo"));

        Assert.Equal("already owned", File.ReadAllText(markerPath));
        Assert.True(File.Exists(Path.Combine(outputRoot, "package")));
    }

    /// <summary>
    /// Refuses an existing, non-empty custom output root with no ownership marker, leaving its
    /// content untouched -- an arbitrary folder a user picked in Settings must never be silently
    /// adopted, since a later clean or normal build would then be free to delete its real content.
    /// </summary>
    [Fact]
    public void EnsureOwnedRefusesANonEmptyUnmarkedCustomOutputRoot()
    {
        using var temporaryDirectory = new TemporaryDirectory();
        string outputRoot = Path.Combine(temporaryDirectory.Path, "custom-out");
        Directory.CreateDirectory(outputRoot);
        string unrelatedFilePath = Path.Combine(outputRoot, "package");
        byte[] unrelatedContent = "unrelated user data, not DovahLink's"u8.ToArray();
        File.WriteAllBytes(unrelatedFilePath, unrelatedContent);
        var guard = new BuildOutputOwnershipGuard();

        InvalidOperationException exception = Assert.Throws<InvalidOperationException>(
            () => guard.EnsureOwned(outputRoot, Path.Combine(temporaryDirectory.Path, "repo")));

        Assert.Contains(outputRoot, exception.Message);
        Assert.False(File.Exists(Path.Combine(outputRoot, BuildOutputOwnershipGuard.MarkerFileName)));
        Assert.Equal(unrelatedContent, File.ReadAllBytes(unrelatedFilePath));
    }

    /// <summary>Refuses a non-empty unmarked custom output root even when its only content is a nested subfolder rather than a file.</summary>
    [Fact]
    public void EnsureOwnedRefusesACustomOutputRootContainingOnlyASubfolder()
    {
        using var temporaryDirectory = new TemporaryDirectory();
        string outputRoot = Path.Combine(temporaryDirectory.Path, "custom-out");
        string unrelatedSubfolder = Path.Combine(outputRoot, "package");
        Directory.CreateDirectory(unrelatedSubfolder);
        File.WriteAllText(Path.Combine(unrelatedSubfolder, "real-file.txt"), "unrelated user data");
        var guard = new BuildOutputOwnershipGuard();

        Assert.Throws<InvalidOperationException>(
            () => guard.EnsureOwned(outputRoot, Path.Combine(temporaryDirectory.Path, "repo")));

        Assert.True(File.Exists(Path.Combine(unrelatedSubfolder, "real-file.txt")));
    }

    /// <summary>
    /// A second <see cref="BuildOutputOwnershipGuard.EnsureOwned"/> call against the same adopted
    /// custom root succeeds again without error, matching a Builder restart reusing the same
    /// Settings-configured output folder across launches.
    /// </summary>
    [Fact]
    public void EnsureOwnedIsIdempotentAcrossRepeatedCallsAgainstAnAdoptedRoot()
    {
        using var temporaryDirectory = new TemporaryDirectory();
        string outputRoot = Path.Combine(temporaryDirectory.Path, "custom-out");
        string repositoryRoot = Path.Combine(temporaryDirectory.Path, "repo");
        var guard = new BuildOutputOwnershipGuard();
        guard.EnsureOwned(outputRoot, repositoryRoot);
        File.WriteAllText(Path.Combine(outputRoot, "package"), "this run's real output");

        guard.EnsureOwned(outputRoot, repositoryRoot);

        Assert.True(File.Exists(Path.Combine(outputRoot, "package")));
    }
}
