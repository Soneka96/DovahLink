namespace DovahLink.DovahLinkBuilder.Build;

/// <summary>Locates supported Visual Studio and bundled vcpkg installations.</summary>
public static class VisualStudioToolchainLocator
{
    /// <summary>The tool name reported by <see cref="TryFind()"/> and <see cref="TryFind(IEnumerable{string})"/>.</summary>
    private const string ToolName = "Visual Studio";

    /// <summary>
    /// Locates the first supported Visual Studio 2022 toolchain from the configured and standard installation paths.
    /// </summary>
    /// <returns>The located Visual Studio toolchain.</returns>
    public static VisualStudioToolchain Find() => Find(GetDefaultInstallationRoots());

    /// <summary>
    /// Locates the first supported Visual Studio 2022 installation among the specified roots.
    /// </summary>
    /// <param name="installationRoots">The installation roots to search.</param>
    /// <returns>The toolchain for the first root containing both the Visual Studio environment script and bundled vcpkg.</returns>
    public static VisualStudioToolchain Find(IEnumerable<string> installationRoots)
    {
        foreach (string root in installationRoots)
        {
            string vcvarsallPath = Path.Combine(root, "VC", "Auxiliary", "Build", "vcvarsall.bat");
            string vcpkgRoot = Path.Combine(root, "VC", "vcpkg");
            if (File.Exists(vcvarsallPath) && Directory.Exists(vcpkgRoot))
            {
                return new VisualStudioToolchain(vcvarsallPath, vcpkgRoot);
            }
        }

        throw new InvalidOperationException(
            "Could not find a supported Visual Studio 2022 installation with vcvarsall.bat and bundled vcpkg.");
    }

    /// <summary>Validates and normalizes the toolchain paths before any shell boundary is entered.</summary>
    /// <param name="toolchain">The candidate environment script and vcpkg directory.</param>
    /// <returns>The toolchain with absolute normalized paths.</returns>
    /// <exception cref="InvalidOperationException">Thrown when either required path is unavailable.</exception>
    public static VisualStudioToolchain Validate(VisualStudioToolchain toolchain)
    {
        string vcvarsallPath = Path.GetFullPath(toolchain.VcvarsallPath);
        string vcpkgRoot = Path.GetFullPath(toolchain.VcpkgRoot);
        if (!string.Equals(Path.GetFileName(vcvarsallPath), "vcvarsall.bat", StringComparison.OrdinalIgnoreCase) ||
            !File.Exists(vcvarsallPath))
        {
            throw new InvalidOperationException($"The Visual Studio environment script does not exist: {vcvarsallPath}");
        }
        if (!Directory.Exists(vcpkgRoot))
        {
            throw new InvalidOperationException($"The Visual Studio vcpkg directory does not exist: {vcpkgRoot}");
        }
        return new VisualStudioToolchain(vcvarsallPath, vcpkgRoot);
    }

    /// <summary>
    /// Locates the first supported Visual Studio 2022 toolchain from the configured and standard
    /// installation paths, reporting the result instead of throwing.
    /// </summary>
    /// <returns>A <see cref="ToolchainCheckResult"/> describing whether Visual Studio was found.</returns>
    public static ToolchainCheckResult TryFind() => TryFind(GetDefaultInstallationRoots());

    /// <summary>
    /// Locates the first supported Visual Studio 2022 installation among the specified roots,
    /// reporting the result instead of throwing.
    /// </summary>
    /// <param name="installationRoots">The installation roots to search.</param>
    /// <returns>A <see cref="ToolchainCheckResult"/> describing whether Visual Studio was found.</returns>
    public static ToolchainCheckResult TryFind(IEnumerable<string> installationRoots)
    {
        try
        {
            VisualStudioToolchain toolchain = Find(installationRoots);
            return new ToolchainCheckResult(ToolName, ToolchainAvailability.Found, toolchain.VcvarsallPath, null);
        }
        catch (InvalidOperationException exception)
        {
            return new ToolchainCheckResult(ToolName, ToolchainAvailability.Missing, null, exception.Message);
        }
        catch (Exception exception) when (exception is ArgumentException or IOException or UnauthorizedAccessException)
        {
            return new ToolchainCheckResult(ToolName, ToolchainAvailability.CouldNotCheck, null, exception.Message);
        }
    }

    /// <summary>Gets the configured and standard Visual Studio 2022 installation roots to search.</summary>
    private static IEnumerable<string> GetDefaultInstallationRoots()
    {
        string programFiles = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
        string programFilesX86 = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86);
        string? visualStudioInstall = Environment.GetEnvironmentVariable("VSINSTALLDIR");

        return new[]
        {
            visualStudioInstall,
            Path.Combine(programFiles, "Microsoft Visual Studio", "2022", "Community"),
            Path.Combine(programFiles, "Microsoft Visual Studio", "2022", "Professional"),
            Path.Combine(programFiles, "Microsoft Visual Studio", "2022", "Enterprise"),
            Path.Combine(programFilesX86, "Microsoft Visual Studio", "2022", "BuildTools"),
        }.Where(path => !string.IsNullOrWhiteSpace(path))!;
    }
}
