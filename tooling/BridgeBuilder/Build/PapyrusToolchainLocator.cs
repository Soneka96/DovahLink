namespace DovahLink.BridgeBuilder.Build;

/// <summary>Describes the Creation Kit Papyrus compiler and source paths required by a build.</summary>
/// <param name="CompilerPath">The path to <c>PapyrusCompiler.exe</c>.</param>
/// <param name="ImportDirectory">The Papyrus source import directory passed to the compiler.</param>
/// <param name="FlagsFilePath">The path to <c>TESV_Papyrus_Flags.flg</c>.</param>
public sealed record PapyrusToolchain(string CompilerPath, string ImportDirectory, string FlagsFilePath);

/// <summary>Locates a supported Creation Kit Papyrus compiler installation.</summary>
public static class PapyrusToolchainLocator
{
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
    public static PapyrusToolchain Find()
    {
        string programFilesX86 = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86);
        string? skyrimInstall = Environment.GetEnvironmentVariable("SKYRIM_INSTALL_DIR");

        IEnumerable<string> roots = new[]
        {
            skyrimInstall,
            Path.Combine(programFilesX86, "Steam", "steamapps", "common", "Skyrim Special Edition"),
        }.Where(path => !string.IsNullOrWhiteSpace(path))!;

        return Find(roots);
    }

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
}
