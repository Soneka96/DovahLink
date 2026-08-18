using DovahLink.BridgeBuilder.Build;

namespace DovahLink.BridgeBuilder.Tests;

/// <summary>Verifies Papyrus compiler toolchain discovery.</summary>
public sealed class PapyrusToolchainTests
{
    /// <summary>Finds an installation containing the required compiler and flags file.</summary>
    [Fact]
    public void FindsTheFirstInstallationWithTheRequiredFiles()
    {
        using var temporaryDirectory = new TemporaryDirectory();
        string installationRoot = Path.Combine(temporaryDirectory.Path, "Skyrim Special Edition");
        string compilerPath = Path.Combine(installationRoot, "Papyrus Compiler", "PapyrusCompiler.exe");
        string importDirectory = Path.Combine(installationRoot, "Data", "Scripts", "Source");
        string flagsFilePath = Path.Combine(importDirectory, "TESV_Papyrus_Flags.flg");
        Directory.CreateDirectory(Path.GetDirectoryName(compilerPath)!);
        Directory.CreateDirectory(importDirectory);
        File.WriteAllText(compilerPath, "compiler");
        File.WriteAllText(flagsFilePath, "flags");

        PapyrusToolchain toolchain = PapyrusToolchainLocator.Find([installationRoot]);

        Assert.Equal(compilerPath, toolchain.CompilerPath);
        Assert.Equal(importDirectory, toolchain.ImportDirectory);
        Assert.Equal(flagsFilePath, toolchain.FlagsFilePath);
    }

    /// <summary>Finds the flags file under the alternate <c>Data\Source\Scripts</c> import layout when the default layout has no flags file.</summary>
    [Fact]
    public void FindsTheAlternateImportDirectoryLayout()
    {
        using var temporaryDirectory = new TemporaryDirectory();
        string installationRoot = Path.Combine(temporaryDirectory.Path, "Skyrim Special Edition");
        string compilerPath = Path.Combine(installationRoot, "Papyrus Compiler", "PapyrusCompiler.exe");
        string defaultImportDirectory = Path.Combine(installationRoot, "Data", "Scripts", "Source");
        string alternateImportDirectory = Path.Combine(installationRoot, "Data", "Source", "Scripts");
        string flagsFilePath = Path.Combine(alternateImportDirectory, "TESV_Papyrus_Flags.flg");
        Directory.CreateDirectory(Path.GetDirectoryName(compilerPath)!);
        Directory.CreateDirectory(defaultImportDirectory);
        Directory.CreateDirectory(alternateImportDirectory);
        File.WriteAllText(compilerPath, "compiler");
        File.WriteAllText(flagsFilePath, "flags");

        PapyrusToolchain toolchain = PapyrusToolchainLocator.Find([installationRoot]);

        Assert.Equal(alternateImportDirectory, toolchain.ImportDirectory);
    }

    /// <summary>Rejects installations missing the Papyrus compiler executable.</summary>
    [Fact]
    public void RejectsInstallationsWithoutTheCompiler()
    {
        using var temporaryDirectory = new TemporaryDirectory();
        string importDirectory = Path.Combine(temporaryDirectory.Path, "Data", "Scripts", "Source");
        Directory.CreateDirectory(importDirectory);
        File.WriteAllText(Path.Combine(importDirectory, "TESV_Papyrus_Flags.flg"), "flags");

        Assert.Throws<InvalidOperationException>(() =>
            PapyrusToolchainLocator.Find([temporaryDirectory.Path]));
    }

    /// <summary>Rejects installations whose compiler exists but no known layout has a flags file.</summary>
    [Fact]
    public void RejectsInstallationsWithoutAFlagsFile()
    {
        using var temporaryDirectory = new TemporaryDirectory();
        string compilerPath = Path.Combine(temporaryDirectory.Path, "Papyrus Compiler", "PapyrusCompiler.exe");
        Directory.CreateDirectory(Path.GetDirectoryName(compilerPath)!);
        File.WriteAllText(compilerPath, "compiler");

        Assert.Throws<InvalidOperationException>(() =>
            PapyrusToolchainLocator.Find([temporaryDirectory.Path]));
    }

    /// <summary>Rejects a direct toolchain value whose compiler is missing.</summary>
    [Fact]
    public void ValidateRejectsAMissingCompiler()
    {
        using var temporaryDirectory = new TemporaryDirectory();
        string importDirectory = Path.Combine(temporaryDirectory.Path, "Source");
        string flagsFilePath = Path.Combine(importDirectory, "TESV_Papyrus_Flags.flg");
        Directory.CreateDirectory(importDirectory);
        File.WriteAllText(flagsFilePath, "flags");

        Assert.Throws<InvalidOperationException>(() => PapyrusToolchainLocator.Validate(
            new PapyrusToolchain(Path.Combine(temporaryDirectory.Path, "missing.exe"), importDirectory, flagsFilePath)));
    }

    /// <summary>Rejects a direct toolchain value whose import directory is missing.</summary>
    [Fact]
    public void ValidateRejectsAMissingImportDirectory()
    {
        using var temporaryDirectory = new TemporaryDirectory();
        string compilerPath = Path.Combine(temporaryDirectory.Path, "PapyrusCompiler.exe");
        string importDirectory = Path.Combine(temporaryDirectory.Path, "missing-source");
        File.WriteAllText(compilerPath, "compiler");

        Assert.Throws<InvalidOperationException>(() => PapyrusToolchainLocator.Validate(
            new PapyrusToolchain(compilerPath, importDirectory, Path.Combine(importDirectory, "TESV_Papyrus_Flags.flg"))));
    }

    /// <summary>Rejects a direct toolchain value whose flags file is missing.</summary>
    [Fact]
    public void ValidateRejectsAMissingFlagsFile()
    {
        using var temporaryDirectory = new TemporaryDirectory();
        string compilerPath = Path.Combine(temporaryDirectory.Path, "PapyrusCompiler.exe");
        string importDirectory = Path.Combine(temporaryDirectory.Path, "Source");
        Directory.CreateDirectory(importDirectory);
        File.WriteAllText(compilerPath, "compiler");

        Assert.Throws<InvalidOperationException>(() => PapyrusToolchainLocator.Validate(
            new PapyrusToolchain(compilerPath, importDirectory, Path.Combine(importDirectory, "TESV_Papyrus_Flags.flg"))));
    }

    /// <summary>Accepts and normalizes a toolchain whose paths all exist.</summary>
    [Fact]
    public void ValidateAcceptsAToolchainWithAllRequiredPaths()
    {
        using var temporaryDirectory = new TemporaryDirectory();
        string compilerPath = Path.Combine(temporaryDirectory.Path, "PapyrusCompiler.exe");
        string importDirectory = Path.Combine(temporaryDirectory.Path, "Source");
        string flagsFilePath = Path.Combine(importDirectory, "TESV_Papyrus_Flags.flg");
        Directory.CreateDirectory(importDirectory);
        File.WriteAllText(compilerPath, "compiler");
        File.WriteAllText(flagsFilePath, "flags");

        PapyrusToolchain validated = PapyrusToolchainLocator.Validate(
            new PapyrusToolchain(compilerPath, importDirectory, flagsFilePath));

        Assert.Equal(Path.GetFullPath(compilerPath), validated.CompilerPath);
        Assert.Equal(Path.GetFullPath(importDirectory), validated.ImportDirectory);
        Assert.Equal(Path.GetFullPath(flagsFilePath), validated.FlagsFilePath);
    }

    /// <summary>Creates and removes an isolated temporary directory for a test.</summary>
    private sealed class TemporaryDirectory : IDisposable
    {
        /// <summary>Creates a unique temporary directory.</summary>
        public TemporaryDirectory()
        {
            Path = System.IO.Path.Combine(
                System.IO.Path.GetTempPath(),
                "DovahLinkBridgeBuilderTests",
                Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Path);
        }

        /// <summary>Gets the temporary directory path.</summary>
        public string Path { get; }

        /// <summary>Removes the temporary directory when the test completes.</summary>
        public void Dispose()
        {
            if (Directory.Exists(Path))
            {
                Directory.Delete(Path, recursive: true);
            }
        }
    }
}
