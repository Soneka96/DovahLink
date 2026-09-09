using DovahLink.DovahLinkBuilder.Build;

namespace DovahLink.DovahLinkBuilder.Tests;

/// <summary>Builders for representative test values, grouped by area.</summary>
internal static class Fixtures
{
    // ---- Toolchains ----

    /// <summary>
    /// Builds a validated <see cref="VisualStudioToolchain"/> by creating its required environment
    /// script and vcpkg directory under <paramref name="repositoryRoot"/>; a test that wants the
    /// default installation layout calls this with just a root, and a test that needs a different
    /// installation directory name overrides <paramref name="installationName"/>.
    /// </summary>
    /// <param name="repositoryRoot">The temporary root under which to create the installation.</param>
    /// <param name="installationName">The installation directory name, including any path characters under test.</param>
    public static VisualStudioToolchain BuildVisualStudioToolchain(
        string repositoryRoot,
        string installationName = "Visual Studio")
    {
        string installationRoot = Path.Combine(repositoryRoot, installationName);
        string vcvarsallPath = Path.Combine(installationRoot, "VC", "Auxiliary", "Build", "vcvarsall.bat");
        string vcpkgRoot = Path.Combine(installationRoot, "VC", "vcpkg");
        Directory.CreateDirectory(Path.GetDirectoryName(vcvarsallPath)!);
        Directory.CreateDirectory(vcpkgRoot);
        File.WriteAllText(vcvarsallPath, "@echo off\n");
        return new VisualStudioToolchain(vcvarsallPath, vcpkgRoot);
    }

    /// <summary>
    /// Builds a validated <see cref="PapyrusToolchain"/> by creating its required compiler, import
    /// directory, and flags file under <paramref name="repositoryRoot"/>.
    /// </summary>
    /// <param name="repositoryRoot">The temporary root under which to create the installation.</param>
    public static PapyrusToolchain BuildPapyrusToolchain(string repositoryRoot)
    {
        string installationRoot = Path.Combine(repositoryRoot, "Skyrim Special Edition");
        string compilerPath = Path.Combine(installationRoot, "Papyrus Compiler", "PapyrusCompiler.exe");
        string importDirectory = Path.Combine(installationRoot, "Data", "Scripts", "Source");
        string flagsFilePath = Path.Combine(importDirectory, "TESV_Papyrus_Flags.flg");
        Directory.CreateDirectory(Path.GetDirectoryName(compilerPath)!);
        Directory.CreateDirectory(importDirectory);
        File.WriteAllText(compilerPath, "compiler");
        File.WriteAllText(flagsFilePath, "flags");
        return new PapyrusToolchain(compilerPath, importDirectory, flagsFilePath);
    }

    // ---- Adapter+Host build inputs ----

    /// <summary>
    /// Creates the adapter manifest, repository VERSION file, packaging script, and console-admin
    /// script and YAML configuration required by <see cref="AdapterHostBuildCoordinator.BuildAsync"/>.
    /// </summary>
    /// <param name="repositoryRoot">The temporary repository root.</param>
    public static void CreateAdapterHostBuildInputs(string repositoryRoot)
    {
        Directory.CreateDirectory(Path.Combine(repositoryRoot, "adapter"));
        File.WriteAllText(Path.Combine(repositoryRoot, "adapter", "vcpkg.json"), "{}");
        File.WriteAllText(Path.Combine(repositoryRoot, "VERSION"), "0.1.0");

        string toolingRoot = Path.Combine(repositoryRoot, "tooling");
        Directory.CreateDirectory(toolingRoot);
        File.WriteAllText(Path.Combine(toolingRoot, "package_adapter_host.py"), "# fake packaging script");

        string consoleAdminRoot = Path.Combine(repositoryRoot, "console-admin");
        Directory.CreateDirectory(consoleAdminRoot);
        File.WriteAllText(Path.Combine(consoleAdminRoot, "DovahLinkAdmin.psc"), "Scriptname DovahLinkAdmin Hidden");
        File.WriteAllText(Path.Combine(consoleAdminRoot, "dovahlink.yaml"), "name: dovahlink");
    }
}
