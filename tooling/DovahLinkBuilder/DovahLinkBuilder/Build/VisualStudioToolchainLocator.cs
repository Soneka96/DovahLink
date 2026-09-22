using System.Diagnostics;
using System.IO;

namespace DovahLink.DovahLinkBuilder.Build;

/// <summary>Locates supported Visual Studio installations and their bundled build tools.</summary>
public static class VisualStudioToolchainLocator
{
    /// <summary>The tool name reported by <see cref="TryFind()"/> and <see cref="TryFind(IEnumerable{string})"/>.</summary>
    private const string ToolName = "Visual Studio";

    /// <summary>
    /// Locates the first supported Visual Studio toolchain from the configured, discovered, and standard installation paths.
    /// </summary>
    /// <returns>The located Visual Studio toolchain.</returns>
    public static VisualStudioToolchain Find() => Find(GetDefaultInstallationRoots(
        Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
        Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86),
        Environment.GetEnvironmentVariable("VSINSTALLDIR"),
        FindVisualStudioInstallationRoots()));

    /// <summary>Locates the first supported Visual Studio installation among the specified roots.</summary>
    /// <param name="installationRoots">The installation roots to search.</param>
    /// <returns>The toolchain for the first root containing the environment script, bundled vcpkg, CMake, and Ninja.</returns>
    public static VisualStudioToolchain Find(IEnumerable<string> installationRoots)
    {
        foreach (string root in installationRoots)
        {
            string vcvarsallPath = GetVcvarsallPath(root);
            string vcpkgRoot = GetBundledVcpkgRoot(root);
            string cmakePath = GetBundledCMakePath(root);
            string ninjaPath = GetBundledNinjaPath(root);
            if (File.Exists(vcvarsallPath) && Directory.Exists(vcpkgRoot) && File.Exists(cmakePath) && File.Exists(ninjaPath))
            {
                return new VisualStudioToolchain(vcvarsallPath, vcpkgRoot, cmakePath, ninjaPath);
            }
        }

        throw new InvalidOperationException(
            "Could not find a supported Visual Studio installation with vcvarsall.bat, bundled vcpkg, CMake, and Ninja.");
    }

    /// <summary>
    /// Locates the first candidate installation root that contains a Visual Studio compiler
    /// environment, regardless of whether its bundled CMake, Ninja, or vcpkg are also present --
    /// used for preflight diagnostics, never for build tool selection.
    /// </summary>
    /// <returns>The first matching installation, or <see langword="null"/> when none was found.</returns>
    public static VisualStudioCompilerInstallation? FindCompilerOnly() => FindCompilerOnly(GetDefaultInstallationRoots(
        Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
        Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86),
        Environment.GetEnvironmentVariable("VSINSTALLDIR"),
        FindVisualStudioInstallationRoots()));

    /// <summary>
    /// Locates the first candidate installation root among the specified roots that contains a
    /// Visual Studio compiler environment, regardless of whether its bundled CMake, Ninja, or vcpkg
    /// are also present -- used for preflight diagnostics, never for build tool selection.
    /// </summary>
    /// <param name="installationRoots">The installation roots to search.</param>
    /// <returns>The first matching installation, or <see langword="null"/> when none was found.</returns>
    internal static VisualStudioCompilerInstallation? FindCompilerOnly(IEnumerable<string> installationRoots)
    {
        foreach (string root in installationRoots)
        {
            string vcvarsallPath = GetVcvarsallPath(root);
            if (File.Exists(vcvarsallPath))
            {
                return new VisualStudioCompilerInstallation(root, vcvarsallPath);
            }
        }

        return null;
    }

    /// <summary>Gets the environment script path bundled with the installation at <paramref name="root"/>.</summary>
    /// <param name="root">The Visual Studio installation root.</param>
    /// <returns>The candidate environment script path, whether or not it exists.</returns>
    internal static string GetVcvarsallPath(string root) => Path.Combine(root, "VC", "Auxiliary", "Build", "vcvarsall.bat");

    /// <summary>Gets the vcpkg directory bundled with the installation at <paramref name="root"/>.</summary>
    /// <param name="root">The Visual Studio installation root.</param>
    /// <returns>The candidate vcpkg directory, whether or not it exists.</returns>
    internal static string GetBundledVcpkgRoot(string root) => Path.Combine(root, "VC", "vcpkg");

    /// <summary>Gets the CMake executable path bundled with the installation at <paramref name="root"/>.</summary>
    /// <param name="root">The Visual Studio installation root.</param>
    /// <returns>The candidate CMake executable path, whether or not it exists.</returns>
    internal static string GetBundledCMakePath(string root) =>
        Path.Combine(root, "Common7", "IDE", "CommonExtensions", "Microsoft", "CMake", "CMake", "bin", "cmake.exe");

    /// <summary>Gets the Ninja executable path bundled with the installation at <paramref name="root"/>.</summary>
    /// <param name="root">The Visual Studio installation root.</param>
    /// <returns>The candidate Ninja executable path, whether or not it exists.</returns>
    internal static string GetBundledNinjaPath(string root) =>
        Path.Combine(root, "Common7", "IDE", "CommonExtensions", "Microsoft", "CMake", "Ninja", "ninja.exe");

    /// <summary>Validates and normalizes the toolchain paths before any shell boundary is entered.</summary>
    /// <param name="toolchain">The candidate environment script, vcpkg directory, CMake executable, and Ninja executable.</param>
    /// <returns>The toolchain with absolute normalized paths.</returns>
    /// <exception cref="InvalidOperationException">Thrown when a required Visual Studio tool path is unavailable.</exception>
    public static VisualStudioToolchain Validate(VisualStudioToolchain toolchain)
    {
        string vcvarsallPath = Path.GetFullPath(toolchain.VcvarsallPath);
        string vcpkgRoot = Path.GetFullPath(toolchain.VcpkgRoot);
        string cmakePath = Path.GetFullPath(toolchain.CMakePath);
        string ninjaPath = Path.GetFullPath(toolchain.NinjaPath);
        if (!string.Equals(Path.GetFileName(vcvarsallPath), "vcvarsall.bat", StringComparison.OrdinalIgnoreCase) ||
            !File.Exists(vcvarsallPath))
        {
            throw new InvalidOperationException($"The Visual Studio environment script does not exist: {vcvarsallPath}");
        }
        if (!Directory.Exists(vcpkgRoot))
        {
            throw new InvalidOperationException($"The Visual Studio vcpkg directory does not exist: {vcpkgRoot}");
        }
        if (!string.Equals(Path.GetFileName(cmakePath), "cmake.exe", StringComparison.OrdinalIgnoreCase) ||
            !File.Exists(cmakePath))
        {
            throw new InvalidOperationException($"The Visual Studio CMake executable does not exist: {cmakePath}");
        }
        if (!string.Equals(Path.GetFileName(ninjaPath), "ninja.exe", StringComparison.OrdinalIgnoreCase) ||
            !File.Exists(ninjaPath))
        {
            throw new InvalidOperationException($"The Visual Studio Ninja executable does not exist: {ninjaPath}");
        }
        return new VisualStudioToolchain(vcvarsallPath, vcpkgRoot, cmakePath, ninjaPath);
    }

    /// <summary>Locates the default Visual Studio toolchain and reports the result instead of throwing.</summary>
    /// <returns>A <see cref="ToolchainCheckResult"/> describing whether Visual Studio was found.</returns>
    public static ToolchainCheckResult TryFind() => TryFind(() => Find(), out _);

    /// <summary>Locates the default Visual Studio toolchain and returns it along with its preflight result.</summary>
    /// <param name="toolchain">The located toolchain, or <see langword="null"/> when discovery fails.</param>
    /// <returns>A <see cref="ToolchainCheckResult"/> describing whether Visual Studio was found.</returns>
    public static ToolchainCheckResult TryFind(out VisualStudioToolchain? toolchain) => TryFind(() => Find(), out toolchain);

    /// <summary>Locates a toolchain among the supplied roots and reports the result instead of throwing.</summary>
    /// <param name="installationRoots">The installation roots to search.</param>
    /// <returns>A <see cref="ToolchainCheckResult"/> describing whether Visual Studio was found.</returns>
    public static ToolchainCheckResult TryFind(IEnumerable<string> installationRoots) =>
        TryFind(() => Find(installationRoots), out _);

    /// <summary>Runs a supplied Visual Studio lookup and reports its result without throwing.</summary>
    /// <param name="toolchainProvider">The lookup that returns the candidate toolchain.</param>
    /// <param name="toolchain">The located toolchain, or <see langword="null"/> when discovery fails.</param>
    /// <returns>A <see cref="ToolchainCheckResult"/> describing whether Visual Studio was found.</returns>
    internal static ToolchainCheckResult TryFind(Func<VisualStudioToolchain> toolchainProvider, out VisualStudioToolchain? toolchain)
    {
        try
        {
            toolchain = toolchainProvider();
            return new ToolchainCheckResult(ToolName, ToolchainAvailability.Found, toolchain.VcvarsallPath, null);
        }
        catch (InvalidOperationException exception)
        {
            toolchain = null;
            return new ToolchainCheckResult(ToolName, ToolchainAvailability.Missing, null, exception.Message);
        }
        catch (Exception exception) when (exception is ArgumentException or IOException or UnauthorizedAccessException)
        {
            toolchain = null;
            return new ToolchainCheckResult(ToolName, ToolchainAvailability.CouldNotCheck, null, exception.Message);
        }
    }

    /// <summary>Gets configured, dynamically discovered, and standard Visual Studio roots in search order.</summary>
    /// <param name="programFiles">The Program Files directory used by standard 64-bit Visual Studio installations.</param>
    /// <param name="programFilesX86">The Program Files (x86) directory used by standard Visual Studio 2022 Build Tools installations.</param>
    /// <param name="visualStudioInstall">The configured installation root, if present.</param>
    /// <param name="discoveredInstallationRoots">Roots returned by vswhere, ordered newest first.</param>
    /// <returns>The configured root, vswhere roots, then standard Visual Studio 2026 and 2022 roots.</returns>
    internal static IEnumerable<string> GetDefaultInstallationRoots(
        string programFiles,
        string programFilesX86,
        string? visualStudioInstall,
        IEnumerable<string>? discoveredInstallationRoots = null)
    {
        return new[] { visualStudioInstall }
            .Concat(discoveredInstallationRoots ?? [])
            .Concat(new[]
            {
                Path.Combine(programFiles, "Microsoft Visual Studio", "18", "Community"),
                Path.Combine(programFiles, "Microsoft Visual Studio", "18", "Professional"),
                Path.Combine(programFiles, "Microsoft Visual Studio", "18", "Enterprise"),
                Path.Combine(programFiles, "Microsoft Visual Studio", "18", "BuildTools"),
                Path.Combine(programFiles, "Microsoft Visual Studio", "2022", "Community"),
                Path.Combine(programFiles, "Microsoft Visual Studio", "2022", "Professional"),
                Path.Combine(programFiles, "Microsoft Visual Studio", "2022", "Enterprise"),
                Path.Combine(programFilesX86, "Microsoft Visual Studio", "2022", "BuildTools"),
            })
            .Where(path => !string.IsNullOrWhiteSpace(path))
            .Select(path => path!)
            .Distinct(StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>Gets installation roots from Visual Studio Installer's <c>vswhere.exe</c>, newest first.</summary>
    /// <returns>Discovered installation roots, or an empty sequence when vswhere is unavailable or fails.</returns>
    private static IEnumerable<string> FindVisualStudioInstallationRoots()
    {
        try
        {
            string? vswherePath = FindVsWherePath();
            if (vswherePath is null)
            {
                return [];
            }

            var startInfo = new ProcessStartInfo
            {
                FileName = vswherePath,
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
            };
            foreach (string argument in GetVsWhereArguments())
            {
                startInfo.ArgumentList.Add(argument);
            }

            using Process? process = Process.Start(startInfo);
            if (process is null)
            {
                return [];
            }

            Task<string> standardOutput = process.StandardOutput.ReadToEndAsync();
            if (!process.WaitForExit((int)Constants.VisualStudioDiscoveryTimeout.TotalMilliseconds))
            {
                process.Kill(entireProcessTree: true);
                process.WaitForExit((int)Constants.VisualStudioDiscoveryTimeout.TotalMilliseconds);
                return [];
            }
            if (process.ExitCode != 0)
            {
                return [];
            }

            return ParseVsWhereInstallationRoots(standardOutput.GetAwaiter().GetResult());
        }
        catch (Exception exception) when (exception is SystemException)
        {
            return [];
        }
    }

    /// <summary>Builds the arguments that select supported C++ Visual Studio installations from vswhere.</summary>
    /// <returns>Arguments that return supported installation paths from newest to oldest.</returns>
    internal static IReadOnlyList<string> GetVsWhereArguments() =>
    [
        "-products", "*",
        "-version", "[17.0,19.0)",
        "-requires",
        "Microsoft.VisualStudio.Workload.NativeDesktop",
        "Microsoft.VisualStudio.Component.VC.Tools.x86.x64",
        "-sort",
        "-property", "installationPath",
        "-utf8",
    ];

    /// <summary>Parses newline-separated installation paths returned by vswhere.</summary>
    /// <param name="standardOutput">The command's standard output.</param>
    /// <returns>Non-empty installation paths with surrounding whitespace removed.</returns>
    internal static IReadOnlyList<string> ParseVsWhereInstallationRoots(string standardOutput) =>
        standardOutput.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

    /// <summary>Locates Visual Studio Installer's vswhere executable.</summary>
    /// <returns>The configured, standard, or PATH-resolved executable path, or <see langword="null"/>.</returns>
    private static string? FindVsWherePath()
    {
        string? configuredPath = Environment.GetEnvironmentVariable("DOVAHLINK_VSWHERE_PATH");
        if (!string.IsNullOrWhiteSpace(configuredPath) && File.Exists(configuredPath))
        {
            return Path.GetFullPath(configuredPath);
        }

        string programFilesX86 = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86);
        string standardPath = Path.Combine(programFilesX86, "Microsoft Visual Studio", "Installer", "vswhere.exe");
        if (File.Exists(standardPath))
        {
            return standardPath;
        }

        string? pathValue = Environment.GetEnvironmentVariable("PATH");
        foreach (string directory in (pathValue ?? string.Empty).Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            try
            {
                string candidate = Path.Combine(directory.Trim('"'), "vswhere.exe");
                if (File.Exists(candidate))
                {
                    return Path.GetFullPath(candidate);
                }
            }
            catch (Exception exception) when (exception is ArgumentException or IOException or NotSupportedException)
            {
                // Ignore invalid PATH entries and continue to the next candidate.
            }
        }

        return null;
    }
}
