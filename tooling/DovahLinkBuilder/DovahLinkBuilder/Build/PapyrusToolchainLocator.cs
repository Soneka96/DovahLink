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
    /// Locates the Papyrus compiler from the configured or standard Skyrim Special Edition installation paths.
    /// </summary>
    /// <returns>The located Papyrus toolchain.</returns>
    public static PapyrusToolchain Find() => Find(GetDefaultInstallationRoots());

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
    public static ToolchainCheckResult TryFind() => TryFind(GetDefaultInstallationRoots());

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

    /// <summary>Gets the configured or standard Skyrim Special Edition installation roots to search.</summary>
    private static IEnumerable<string> GetDefaultInstallationRoots()
    {
        string programFilesX86 = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86);
        string? skyrimInstall = Environment.GetEnvironmentVariable("SKYRIM_INSTALL_DIR");

        return new[]
        {
            skyrimInstall,
            Path.Combine(programFilesX86, "Steam", "steamapps", "common", "Skyrim Special Edition"),
        }.Where(path => !string.IsNullOrWhiteSpace(path))!;
    }
}
