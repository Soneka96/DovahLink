using System.Collections.ObjectModel;

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
}

/// <summary>Converts Visual Studio's environment output into child-process environment values.</summary>
public static class VisualStudioEnvironment
{
    /// <summary>Parses environment lines and applies the validated bundled vcpkg root.</summary>
    /// <param name="environmentLines">The <c>set</c> output produced after importing the developer environment.</param>
    /// <param name="toolchain">The validated toolchain whose vcpkg root overrides inherited values.</param>
    /// <returns>A case-insensitive, read-only environment map for CMake.</returns>
    public static IReadOnlyDictionary<string, string> Create(
        IEnumerable<string> environmentLines,
        VisualStudioToolchain toolchain)
    {
        VisualStudioToolchain validated = VisualStudioToolchainLocator.Validate(toolchain);
        var environment = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (string line in environmentLines)
        {
            int separator = line.IndexOf('=');
            if (separator <= 0)
            {
                continue;
            }
            environment[line[..separator]] = line[(separator + 1)..];
        }
        environment["VCPKG_ROOT"] = validated.VcpkgRoot;
        return new ReadOnlyDictionary<string, string>(environment);
    }
}
