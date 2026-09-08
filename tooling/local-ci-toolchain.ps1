# Shared Visual Studio/native-toolchain discovery and invocation helpers used by
# tooling/run-local-ci.ps1. Dot-source this file; it defines functions only and has no
# direct-invocation entry point of its own.

$ErrorActionPreference = "Stop"

<#
.SYNOPSIS
Finds a complete Visual Studio 2022 C++ toolchain through Visual Studio Installer.

.PARAMETER LocatorPath
The path to Visual Studio Installer's vswhere executable.

.OUTPUTS
A toolchain object containing the validated Visual Studio, vcvars, vcpkg, CMake, and Ninja paths.
#>
function Find-VisualStudioToolchain {
    param([string]$LocatorPath)

    if (-not (Test-Path -LiteralPath $LocatorPath -PathType Leaf)) {
        throw "Visual Studio Installer's vswhere.exe was not found at '$LocatorPath'. Install Visual Studio Installer and the Desktop development with C++ workload."
    }

    $installations = @(& $LocatorPath `
            -latest `
            -products * `
            -version '[17.0,18.0)' `
            -requires `
            Microsoft.VisualStudio.Workload.NativeDesktop `
            Microsoft.VisualStudio.Component.VC.Tools.x86.x64 `
            Microsoft.VisualStudio.Component.VC.CMake.Project `
            -property installationPath)
    if ($LASTEXITCODE -ne 0) {
        throw "vswhere failed with exit code $LASTEXITCODE while locating Visual Studio 2022. Repair or update Visual Studio Installer."
    }

    $installationPath = $installations | Where-Object { -not [string]::IsNullOrWhiteSpace($_) } | Select-Object -First 1
    if ($null -eq $installationPath) {
        throw "No complete Visual Studio 2022 installation has the Desktop development with C++ workload, MSVC x64/x86 tools, and CMake tools. Add them in Visual Studio Installer."
    }
    $installationPath = $installationPath.Trim()

    $requiredPaths = [ordered]@{
        VcvarsallPath  = Join-Path $installationPath "VC\Auxiliary\Build\vcvarsall.bat"
        VcpkgRoot      = Join-Path $installationPath "VC\vcpkg"
        CMakeDirectory = Join-Path $installationPath "Common7\IDE\CommonExtensions\Microsoft\CMake\CMake\bin"
        NinjaDirectory = Join-Path $installationPath "Common7\IDE\CommonExtensions\Microsoft\CMake\Ninja"
    }
    $requiredFiles = @(
        $requiredPaths.VcvarsallPath,
        (Join-Path $requiredPaths.CMakeDirectory "cmake.exe"),
        (Join-Path $requiredPaths.NinjaDirectory "ninja.exe")
    )
    foreach ($requiredFile in $requiredFiles) {
        if (-not (Test-Path -LiteralPath $requiredFile -PathType Leaf)) {
            throw "Visual Studio discovery returned '$installationPath', but required path '$requiredFile' is missing. Repair the Desktop development with C++ workload in Visual Studio Installer."
        }
    }
    if (-not (Test-Path -LiteralPath $requiredPaths.VcpkgRoot -PathType Container)) {
        throw "Visual Studio discovery returned '$installationPath', but required path '$($requiredPaths.VcpkgRoot)' is missing. Repair the Desktop development with C++ workload in Visual Studio Installer."
    }

    [pscustomobject]@{
        InstallationPath = $installationPath
        VcvarsallPath    = $requiredPaths.VcvarsallPath
        VcpkgRoot        = $requiredPaths.VcpkgRoot
        CMakeDirectory   = $requiredPaths.CMakeDirectory
        NinjaDirectory   = $requiredPaths.NinjaDirectory
    }
}

<#
.SYNOPSIS
Imports the selected Visual Studio x64 environment into the current process.

.PARAMETER Toolchain
The validated toolchain returned by Find-VisualStudioToolchain.
#>
function Import-VisualStudioEnvironment {
    param($Toolchain)

    $tempEnvFile = Join-Path ([System.IO.Path]::GetTempPath()) "vs_env_$([System.Diagnostics.Process]::GetCurrentProcess().Id).txt"
    try {
        & $env:ComSpec /c "`"$($Toolchain.VcvarsallPath)`" x64 >nul && set > `"$tempEnvFile`""
        if ($LASTEXITCODE -ne 0) {
            throw "Visual Studio x64 environment initialization failed with exit code $LASTEXITCODE."
        }
    }
    catch {
        throw $_
    }

    try {
        $environmentLines = Get-Content -Path $tempEnvFile -Encoding ASCII
        foreach ($line in $environmentLines) {
            $separator = $line.IndexOf('=')
            if ($separator -gt 0) {
                [System.Environment]::SetEnvironmentVariable($line.Substring(0, $separator), $line.Substring($separator + 1), 'Process')
            }
        }
    }
    finally {
        Remove-Item -Path $tempEnvFile -Force -ErrorAction SilentlyContinue
    }
    $env:VCPKG_ROOT = $Toolchain.VcpkgRoot
    $env:PATH = "$($Toolchain.CMakeDirectory);$($Toolchain.NinjaDirectory);$env:PATH"
}

<#
.SYNOPSIS
Runs a native command and converts a non-zero exit code into a terminating PowerShell error.

.PARAMETER FilePath
The executable or script to invoke.

.PARAMETER ArgumentList
The arguments passed to the native command.
#>
function Invoke-CheckedNativeCommand {
    param(
        [Parameter(Mandatory = $true)][string]$FilePath,
        [string[]]$ArgumentList = @()
    )

    & $FilePath @ArgumentList
    $exitCode = $LASTEXITCODE
    if ($exitCode -ne 0) {
        throw "$FilePath failed with exit code $exitCode."
    }
}
