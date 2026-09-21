# Shared Visual Studio/native-toolchain discovery and invocation helpers used by
# tooling/run-local-ci.ps1. Dot-source this file; it defines functions only and has no
# direct-invocation entry point of its own.

$ErrorActionPreference = "Stop"

<#
.SYNOPSIS
Gets executable paths that PowerShell resolves from the current process PATH.

.PARAMETER Name
The executable name to resolve, including its extension where applicable.

.OUTPUTS
Executable paths in PATH search order.
#>
function Get-ExecutablePathsFromPath {
    param([Parameter(Mandatory = $true)][string]$Name)

    @(Get-Command -Name $Name -CommandType Application -All -ErrorAction SilentlyContinue |
        ForEach-Object { $_.Source })
}

<#
.SYNOPSIS
Returns the first existing executable from an ordered candidate list.

.PARAMETER ToolName
The human-readable tool name used in errors.

.PARAMETER CandidatePaths
The configured, standard, and PATH-derived executable paths to check.

.PARAMETER OverrideVariable
The environment variable that can specify a non-standard executable path.

.OUTPUTS
The normalized path to the first existing executable.
#>
function Resolve-ExistingExecutablePath {
    param(
        [Parameter(Mandatory = $true)][string]$ToolName,
        [Parameter(Mandatory = $true)][AllowEmptyString()][string[]]$CandidatePaths,
        [Parameter(Mandatory = $true)][string]$OverrideVariable
    )

    foreach ($candidatePath in $CandidatePaths | Where-Object { -not [string]::IsNullOrWhiteSpace($_) } | Select-Object -Unique) {
        $normalizedCandidatePath = $candidatePath.Trim().Trim('"')
        if (Test-Path -LiteralPath $normalizedCandidatePath -PathType Leaf) {
            return (Get-Item -LiteralPath $normalizedCandidatePath).FullName
        }
    }

    throw "$ToolName was not found in the configured path, standard install locations, or PATH. Set $OverrideVariable to the full executable path or add its directory to PATH."
}

<#
.SYNOPSIS
Finds the first executable that reports the repository-pinned version.

.PARAMETER ToolName
The human-readable tool name used in errors.

.PARAMETER CandidatePaths
The configured, standard, and PATH-derived executable paths to check.

.PARAMETER ExpectedVersion
The exact first line expected from the executable's --version command.

.PARAMETER OverrideVariable
The environment variable that can specify a non-standard executable path.

.PARAMETER VersionProbe
An optional test seam that returns an object with ExitCode and Version properties.

.OUTPUTS
The normalized path to the first executable with the expected version.
#>
function Resolve-PinnedExecutablePath {
    param(
        [Parameter(Mandatory = $true)][string]$ToolName,
        [Parameter(Mandatory = $true)][AllowEmptyString()][string[]]$CandidatePaths,
        [Parameter(Mandatory = $true)][string]$ExpectedVersion,
        [Parameter(Mandatory = $true)][string]$OverrideVariable,
        [scriptblock]$VersionProbe
    )

    $checkedVersions = [System.Collections.Generic.List[string]]::new()
    foreach ($candidatePath in $CandidatePaths | Where-Object { -not [string]::IsNullOrWhiteSpace($_) } | Select-Object -Unique) {
        $normalizedCandidatePath = $candidatePath.Trim().Trim('"')
        if (-not (Test-Path -LiteralPath $normalizedCandidatePath -PathType Leaf)) {
            continue
        }

        try {
            if ($null -ne $VersionProbe) {
                $probeResult = & $VersionProbe $normalizedCandidatePath
                $exitCode = $probeResult.ExitCode
                $version = $probeResult.Version
            }
            else {
                $versionOutput = @(& $normalizedCandidatePath --version 2>&1)
                $exitCode = $LASTEXITCODE
                $version = ($versionOutput | Select-Object -First 1).ToString().Trim()
            }
        }
        catch {
            $checkedVersions.Add("${normalizedCandidatePath}: could not run ($($_.Exception.Message))")
            continue
        }

        if ($exitCode -eq 0 -and $version -eq $ExpectedVersion) {
            return (Get-Item -LiteralPath $normalizedCandidatePath).FullName
        }

        $checkedVersions.Add("${normalizedCandidatePath}: '$version'")
    }

    $checkedSummary = if ($checkedVersions.Count -gt 0) {
        " Checked candidates: $($checkedVersions -join '; ')."
    }
    else {
        " No candidate executable files were found."
    }
    throw "Pinned $ToolName '$ExpectedVersion' was not found in the configured path, standard install locations, or PATH.$checkedSummary Set $OverrideVariable to the full executable path or add its directory to PATH."
}

<#
.SYNOPSIS
Finds a Visual Studio 2022 or Visual Studio 2026 C++ toolchain through Visual Studio Installer.

.PARAMETER LocatorPath
The path to Visual Studio Installer's vswhere executable.

.OUTPUTS
A toolchain object containing the validated Visual Studio, vcvars, and vcpkg paths.
#>
function Find-VisualStudioToolchain {
    param([string]$LocatorPath)

    if (-not (Test-Path -LiteralPath $LocatorPath -PathType Leaf)) {
        throw "Visual Studio Installer's vswhere.exe was not found at '$LocatorPath'. Install Visual Studio Installer and the Desktop development with C++ workload."
    }

    $installations = @(& $LocatorPath `
            -latest `
            -products * `
            -version '[17.0,19.0)' `
            -requires `
            Microsoft.VisualStudio.Workload.NativeDesktop `
            Microsoft.VisualStudio.Component.VC.Tools.x86.x64 `
            -property installationPath)
    if ($LASTEXITCODE -ne 0) {
        throw "vswhere failed with exit code $LASTEXITCODE while locating a supported Visual Studio installation. Repair or update Visual Studio Installer."
    }

    $installationPath = $installations | Where-Object { -not [string]::IsNullOrWhiteSpace($_) } | Select-Object -First 1
    if ($null -eq $installationPath) {
        throw "No complete Visual Studio 2022 or Visual Studio 2026 installation has the Desktop development with C++ workload and MSVC x64/x86 tools. Add them in Visual Studio Installer."
    }
    $installationPath = $installationPath.Trim()

    $requiredPaths = [ordered]@{
        VcvarsallPath = Join-Path $installationPath "VC\Auxiliary\Build\vcvarsall.bat"
        VcpkgRoot     = Join-Path $installationPath "VC\vcpkg"
    }
    if (-not (Test-Path -LiteralPath $requiredPaths.VcvarsallPath -PathType Leaf)) {
        throw "Visual Studio discovery returned '$installationPath', but required path '$($requiredPaths.VcvarsallPath)' is missing. Repair the Desktop development with C++ workload in Visual Studio Installer."
    }
    if (-not (Test-Path -LiteralPath $requiredPaths.VcpkgRoot -PathType Container)) {
        throw "Visual Studio discovery returned '$installationPath', but required path '$($requiredPaths.VcpkgRoot)' is missing. Repair the Desktop development with C++ workload in Visual Studio Installer."
    }

    [pscustomobject]@{
        InstallationPath = $installationPath
        VcvarsallPath    = $requiredPaths.VcvarsallPath
        VcpkgRoot        = $requiredPaths.VcpkgRoot
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
