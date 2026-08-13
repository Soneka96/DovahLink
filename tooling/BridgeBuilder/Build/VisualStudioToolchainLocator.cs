namespace DovahLink.BridgeBuilder.Build;

/// <summary>Locates supported Visual Studio and bundled vcpkg installations.</summary>
public static class VisualStudioToolchainLocator
{
    /// <summary>
    /// Locates the first supported Visual Studio 2022 toolchain from the configured and standard installation paths.
    /// </summary>
    /// <returns>The located Visual Studio toolchain.</returns>
    public static VisualStudioToolchain Find()
    {
        string programFiles = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
        string programFilesX86 = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86);
        string? visualStudioInstall = Environment.GetEnvironmentVariable("VSINSTALLDIR");

        IEnumerable<string> roots = new[]
        {
            visualStudioInstall,
            Path.Combine(programFiles, "Microsoft Visual Studio", "2022", "Community"),
            Path.Combine(programFiles, "Microsoft Visual Studio", "2022", "Professional"),
            Path.Combine(programFiles, "Microsoft Visual Studio", "2022", "Enterprise"),
            Path.Combine(programFilesX86, "Microsoft Visual Studio", "2022", "BuildTools"),
        }.Where(path => !string.IsNullOrWhiteSpace(path))!;

        return Find(roots);
    }

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
}
