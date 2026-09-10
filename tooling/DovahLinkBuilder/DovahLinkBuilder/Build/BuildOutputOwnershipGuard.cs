using System.IO;

namespace DovahLink.DovahLinkBuilder.Build;

/// <summary>
/// Verifies or safely establishes DovahLink Builder's ownership of an output root before any
/// destructive filesystem operation (cleaning or replacing its <c>publish</c>/<c>package</c>
/// subfolders) runs against it. An output root under the repository's own default
/// <c>tooling/out</c> is trusted by repository contract; any other, user-chosen root must be
/// nonexistent or empty (safely adopted, marking it Builder-owned) or already carry that mark from
/// a previous run. An existing, non-empty custom root without that mark is refused, so a Settings
/// output-path override that happens to point at an unrelated folder full of real user data is
/// never destructively adopted.
/// </summary>
public interface IBuildOutputOwnershipGuard
{
    /// <summary>
    /// Verifies <paramref name="outputRoot"/> is safe for DovahLink Builder to destructively manage,
    /// adopting a new or empty custom root as Builder-owned when needed.
    /// </summary>
    /// <param name="outputRoot">The resolved output root a build is about to write to or clean.</param>
    /// <param name="repositoryRoot">The repository root the build targets, used to recognize the default <c>tooling/out</c> location.</param>
    /// <exception cref="InvalidOperationException">
    /// Thrown when <paramref name="outputRoot"/> is a custom folder that already has unrelated
    /// content and no valid Builder-ownership mark, or when the mark could not be created because
    /// the location could not be accessed.
    /// </exception>
    void EnsureOwned(string outputRoot, string repositoryRoot);
}

/// <inheritdoc cref="IBuildOutputOwnershipGuard"/>
public sealed class BuildOutputOwnershipGuard : IBuildOutputOwnershipGuard
{
    /// <summary>
    /// The marker file DovahLink Builder writes into a custom output root it has adopted, proving
    /// ownership on a later run. Matches the identical invariant
    /// <c>tooling/build_output_ownership.py</c> implements for the Python packaging path, which
    /// cannot share this C# type directly; the two must be kept in sync by hand.
    /// </summary>
    public const string MarkerFileName = ".dovahlink-builder-output";

    /// <summary>The marker file's contents, explaining its purpose to anyone who finds it manually.</summary>
    private const string MarkerContents = "This folder is managed by DovahLink Builder. Do not delete this file.\n";

    /// <inheritdoc/>
    public void EnsureOwned(string outputRoot, string repositoryRoot)
    {
        // Wraps every filesystem-touching step below, not just the final create/write: resolving
        // outputRoot itself (Path.GetFullPath) can throw for a malformed value exactly like the
        // create/write calls can, and every one of those failures must reach the same normalized
        // InvalidOperationException rather than letting whichever step happens to fail first escape
        // as a raw framework exception.
        try
        {
            string normalizedRoot = Path.GetFullPath(outputRoot);
            string normalizedDefaultRoot = Path.GetFullPath(Path.Combine(repositoryRoot, "tooling", "out"));
            if (IsUnderDefaultRoot(normalizedRoot, normalizedDefaultRoot))
            {
                return;
            }

            string markerPath = Path.Combine(normalizedRoot, MarkerFileName);
            if (File.Exists(markerPath))
            {
                return;
            }

            if (Directory.Exists(normalizedRoot) && Directory.EnumerateFileSystemEntries(normalizedRoot).Any())
            {
                throw new InvalidOperationException(
                    $"'{normalizedRoot}' already contains files and has never been used as a DovahLink " +
                    "Builder output folder. Choose an empty or previously-used output folder to avoid " +
                    "deleting unrelated content.");
            }

            Directory.CreateDirectory(normalizedRoot);
            File.WriteAllText(markerPath, MarkerContents);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            throw new InvalidOperationException($"Could not create or mark the build output location: {outputRoot}", exception);
        }
    }

    /// <summary>Gets whether <paramref name="normalizedRoot"/> is the default output root itself, or nested under it.</summary>
    /// <param name="normalizedRoot">The output root being checked, already resolved to a full path.</param>
    /// <param name="normalizedDefaultRoot">The repository's default output root, already resolved to a full path.</param>
    private static bool IsUnderDefaultRoot(string normalizedRoot, string normalizedDefaultRoot) =>
        normalizedRoot.Equals(normalizedDefaultRoot, StringComparison.OrdinalIgnoreCase)
        || normalizedRoot.StartsWith(normalizedDefaultRoot + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
}
