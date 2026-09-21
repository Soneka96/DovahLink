using System.IO;

namespace DovahLink.DovahLinkBuilder.Build;

/// <summary>Locates a supported Creation Kit Papyrus compiler installation.</summary>
public static class PapyrusToolchainLocator
{
    /// <summary>The tool name reported by <see cref="TryFind()"/> and <see cref="TryFind(IEnumerable{string})"/>.</summary>
    private const string ToolName = "Papyrus Compiler";

    /// <summary>The Papyrus source import directory layouts checked under each installation root.</summary>
    private static readonly string[] ImportDirectoryLayouts =
    [
        Path.Combine("Data", "Scripts", "Source"),
        Path.Combine("Data", "Source", "Scripts"),
    ];

    /// <summary>
    /// Locates the Papyrus compiler from <c>SKYRIM_INSTALL_DIR</c> or the standard Skyrim Special Edition installation path.
    /// </summary>
    /// <returns>The located Papyrus toolchain.</returns>
    public static PapyrusToolchain Find() => Find(GetDefaultInstallationRoots(null));

    /// <summary>Locates the Papyrus compiler, preferring a configured Skyrim / Creation Kit installation path.</summary>
    /// <param name="configuredInstallPath">The configured installation path, or <see langword="null"/> to rely on automatic discovery.</param>
    /// <returns>The toolchain for the first root containing the compiler and a flags file under a known import layout.</returns>
    /// <exception cref="InvalidOperationException">Thrown when no root contains a supported Papyrus compiler installation.</exception>
    public static PapyrusToolchain FindWithInstallationPath(string? configuredInstallPath) =>
        Find(GetDefaultInstallationRoots(configuredInstallPath));

    /// <summary>
    /// Locates the Papyrus compiler among the specified installation roots.
    /// </summary>
    /// <param name="installationRoots">The installation roots to search.</param>
    /// <returns>The toolchain for the first root containing the compiler and a flags file under a known import layout.</returns>
    /// <exception cref="InvalidOperationException">Thrown when no root contains a supported Papyrus compiler installation.</exception>
    public static PapyrusToolchain Find(IEnumerable<string> installationRoots)
    {
        foreach (string root in installationRoots)
        {
            string compilerPath = Path.Combine(root, "Papyrus Compiler", "PapyrusCompiler.exe");
            if (!File.Exists(compilerPath))
            {
                continue;
            }

            foreach (string importDirectoryLayout in ImportDirectoryLayouts)
            {
                string importDirectory = Path.Combine(root, importDirectoryLayout);
                string flagsFilePath = Path.Combine(importDirectory, "TESV_Papyrus_Flags.flg");
                if (File.Exists(flagsFilePath))
                {
                    return new PapyrusToolchain(compilerPath, importDirectory, flagsFilePath);
                }
            }
        }

        throw new InvalidOperationException(
            "Could not find a supported Creation Kit Papyrus compiler installation with PapyrusCompiler.exe and TESV_Papyrus_Flags.flg.");
    }

    /// <summary>Validates and normalizes the toolchain paths before any process is started.</summary>
    /// <param name="toolchain">The candidate compiler, import, and flags paths.</param>
    /// <returns>The toolchain with absolute normalized paths.</returns>
    /// <exception cref="InvalidOperationException">Thrown when any required path is unavailable.</exception>
    public static PapyrusToolchain Validate(PapyrusToolchain toolchain)
    {
        string compilerPath = Path.GetFullPath(toolchain.CompilerPath);
        string importDirectory = Path.GetFullPath(toolchain.ImportDirectory);
        string flagsFilePath = Path.GetFullPath(toolchain.FlagsFilePath);
        if (!File.Exists(compilerPath))
        {
            throw new InvalidOperationException($"The Papyrus compiler does not exist: {compilerPath}");
        }
        if (!Directory.Exists(importDirectory))
        {
            throw new InvalidOperationException($"The Papyrus import directory does not exist: {importDirectory}");
        }
        if (!File.Exists(flagsFilePath))
        {
            throw new InvalidOperationException($"The Papyrus flags file does not exist: {flagsFilePath}");
        }
        return new PapyrusToolchain(compilerPath, importDirectory, flagsFilePath);
    }

    /// <summary>
    /// Locates the Papyrus compiler from the configured or standard Skyrim Special Edition
    /// installation paths, reporting the result instead of throwing.
    /// </summary>
    /// <returns>A <see cref="ToolchainCheckResult"/> describing whether the Papyrus compiler was found.</returns>
    public static ToolchainCheckResult TryFind() => TryFind(GetDefaultInstallationRoots(null));

    /// <summary>Locates the Papyrus compiler with the configured installation path first, reporting the result instead of throwing.</summary>
    /// <param name="configuredInstallPath">The configured installation path, or <see langword="null"/> to rely on automatic discovery.</param>
    /// <returns>A <see cref="ToolchainCheckResult"/> describing whether the Papyrus compiler was found.</returns>
    public static ToolchainCheckResult TryFindWithInstallationPath(string? configuredInstallPath) =>
        TryFind(GetDefaultInstallationRoots(configuredInstallPath));

    /// <summary>
    /// Locates the Papyrus compiler among the specified installation roots, reporting the result
    /// instead of throwing.
    /// </summary>
    /// <param name="installationRoots">The installation roots to search.</param>
    /// <returns>A <see cref="ToolchainCheckResult"/> describing whether the Papyrus compiler was found.</returns>
    public static ToolchainCheckResult TryFind(IEnumerable<string> installationRoots)
    {
        try
        {
            PapyrusToolchain toolchain = Find(installationRoots);
            return new ToolchainCheckResult(ToolName, ToolchainAvailability.Found, toolchain.CompilerPath, null);
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

    /// <summary>Gets the configured, environment, and standard Skyrim Special Edition installation roots in search order.</summary>
    /// <param name="configuredInstallPath">The Settings-page installation path, or <see langword="null"/>.</param>
    /// <returns>Distinct roots ordered from the Settings-page path to environment and standard Steam defaults.</returns>
    private static IEnumerable<string> GetDefaultInstallationRoots(string? configuredInstallPath) => GetDefaultInstallationRoots(
        configuredInstallPath,
        Environment.GetEnvironmentVariable("SKYRIM_INSTALL_DIR"),
        Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86));

    /// <summary>Builds ordered Papyrus installation candidates from explicit inputs.</summary>
    /// <param name="configuredInstallPath">The Settings-page installation path, or <see langword="null"/>.</param>
    /// <param name="environmentInstallPath">The <c>SKYRIM_INSTALL_DIR</c> path, or <see langword="null"/>.</param>
    /// <param name="programFilesX86">The Program Files (x86) directory containing the standard Steam path.</param>
    /// <returns>Distinct roots ordered from the Settings-page path to environment and standard Steam defaults.</returns>
    internal static IEnumerable<string> GetDefaultInstallationRoots(
        string? configuredInstallPath,
        string? environmentInstallPath,
        string programFilesX86)
    {
        return new[]
        {
            configuredInstallPath,
            environmentInstallPath,
            Path.Combine(programFilesX86, "Steam", "steamapps", "common", "Skyrim Special Edition"),
        }
        .Where(path => !string.IsNullOrWhiteSpace(path))
        .Select(path => path!)
        .Distinct(StringComparer.OrdinalIgnoreCase);
    }
}
