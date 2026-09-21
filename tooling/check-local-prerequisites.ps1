# Checks the local prerequisites used by the full DovahLink CI run. Dot-source this file to use
# Invoke-LocalCiPrerequisiteCheck from another script; direct invocation prints the report and exits.

if (-not (Get-Command Resolve-PinnedExecutablePath -ErrorAction SilentlyContinue)) {
    . (Join-Path $PSScriptRoot "local-ci-toolchain.ps1")
}

<#
.SYNOPSIS
Returns the documented local CI prerequisites and their setup guidance.
#>
function Get-LocalCiPrerequisiteDefinitions {
    return @(
        [pscustomobject]@{
            Id             = "visual-studio"
            Name           = "Visual Studio 2022 or 2026 with Desktop development with C++ and MSVC x64/x86"
            InstallCommand = "In Visual Studio Installer, choose Modify and add Desktop development with C++ plus the MSVC x64/x86 tools."
            VerifyCommand  = "The checker queries vswhere for the workload and MSVC component."
            InstallUrl     = "https://learn.microsoft.com/cpp/build/vscpp-step-0-installation"
        }
        [pscustomobject]@{
            Id             = "git"
            Name           = "Git for Windows"
            InstallCommand = "Install Git for Windows and reopen PowerShell."
            VerifyCommand  = "git --version"
            InstallUrl     = "https://git-scm.com/install/windows"
        }
        [pscustomobject]@{
            Id             = "cmake"
            Name           = "CMake 4.4.2"
            InstallCommand = "Download the official 4.4.2 Windows x64 ZIP, extract it, then add its bin directory to PATH or set DOVAHLINK_CMAKE_PATH to cmake.exe."
            VerifyCommand  = "cmake --version (must report 4.4.2)"
            InstallUrl     = "https://github.com/Kitware/CMake/releases/tag/v4.4.2"
        }
        [pscustomobject]@{
            Id             = "ninja"
            Name           = "Ninja 1.13.2"
            InstallCommand = "Download the official 1.13.2 Windows binary and add its directory to PATH or set DOVAHLINK_NINJA_PATH to ninja.exe."
            VerifyCommand  = "ninja --version (must report 1.13.2)"
            InstallUrl     = "https://github.com/ninja-build/ninja/releases/tag/v1.13.2"
        }
        [pscustomobject]@{
            Id             = "python"
            Name           = "Python 3.13.x"
            InstallCommand = "Install Python 3.13 for Windows and enable its command-line launcher/PATH option."
            VerifyCommand  = "python --version"
            InstallUrl     = "https://www.python.org/downloads/windows/"
        }
        [pscustomobject]@{
            Id             = "dotnet"
            Name           = ".NET 9 SDK"
            InstallCommand = "Install the .NET 9 SDK. The .NET Runtime alone cannot build or test the Host."
            VerifyCommand  = "dotnet --list-sdks (must include a 9.x SDK); dotnet format --version"
            InstallUrl     = "https://dotnet.microsoft.com/en-us/download/dotnet/9.0"
        }
        [pscustomobject]@{
            Id             = "flutter"
            Name           = "Flutter stable SDK"
            InstallCommand = "Install the Flutter stable SDK and add its bin directory to PATH."
            VerifyCommand  = "flutter --version --machine (channel must be stable)"
            InstallUrl     = "https://docs.flutter.dev/install"
        }
        [pscustomobject]@{
            Id             = "dart"
            Name           = "Dart SDK from Flutter"
            InstallCommand = "Install Flutter stable; it includes the Dart SDK used by the app and SDK checks."
            VerifyCommand  = "dart --version"
            InstallUrl     = "https://docs.flutter.dev/install"
        }
        [pscustomobject]@{
            Id             = "clang-format"
            Name           = "clang-format 19.1.5"
            InstallCommand = "Install clang-format from the official LLVM 19.1.5 release and add its bin directory to PATH."
            VerifyCommand  = "clang-format --version (must report 19.1.5)"
            InstallUrl     = "https://github.com/llvm/llvm-project/releases/tag/llvmorg-19.1.5"
        }
        [pscustomobject]@{
            Id             = "ruff"
            Name           = "Ruff"
            InstallCommand = "python -m pip install ruff"
            VerifyCommand  = "ruff --version"
            InstallUrl     = "https://docs.astral.sh/ruff/installation/"
        }
        [pscustomobject]@{
            Id             = "psscriptanalyzer"
            Name           = "PSScriptAnalyzer"
            InstallCommand = 'pwsh -NoProfile -Command "Install-Module PSScriptAnalyzer -Scope CurrentUser -Force"'
            VerifyCommand  = 'pwsh -NoProfile -Command "Get-Command Invoke-Formatter"'
            InstallUrl     = "https://learn.microsoft.com/en-us/powershell/utility-modules/psscriptanalyzer/overview"
        }
    )
}

<#
.SYNOPSIS
Creates a consistent result record for one prerequisite probe.

.PARAMETER Requirement
The prerequisite definition being checked.

.PARAMETER Available
Whether the installed tool satisfies the requirement.

.PARAMETER Version
The detected version or workload description.

.PARAMETER Details
Additional evidence or a reason the requirement is unavailable.

.PARAMETER Path
The resolved executable or installation path, when applicable.

.PARAMETER Value
The validated object needed by the local CI bootstrap, when applicable.
#>
function New-LocalCiPrerequisiteResult {
    param(
        [Parameter(Mandatory = $true)][psobject]$Requirement,
        [Parameter(Mandatory = $true)][bool]$Available,
        [string]$Version = "",
        [string]$Details = "",
        [string]$Path = "",
        [object]$Value
    )

    return [pscustomobject]@{
        Id             = $Requirement.Id
        Name           = $Requirement.Name
        Available      = $Available
        Version        = $Version
        Details        = $Details
        Path           = $Path
        Value          = $Value
        InstallCommand = $Requirement.InstallCommand
        VerifyCommand  = $Requirement.VerifyCommand
        InstallUrl     = $Requirement.InstallUrl
    }
}

<#
.SYNOPSIS
Runs one local CI prerequisite probe and records its result without stopping other probes.

.PARAMETER Requirement
The prerequisite definition to check.

.PARAMETER RepositoryRoot
The repository root used to locate setup files.
#>
function Test-LocalCiPrerequisite {
    param([Parameter(Mandatory = $true)][psobject]$Requirement)

    try {
        switch ($Requirement.Id) {
            "visual-studio" {
                $vswhereCandidates = @($env:DOVAHLINK_VSWHERE_PATH)
                $programFilesX86 = [System.Environment]::GetEnvironmentVariable("ProgramFiles(x86)")
                if (-not [string]::IsNullOrWhiteSpace($programFilesX86)) {
                    $vswhereCandidates += Join-Path $programFilesX86 "Microsoft Visual Studio\Installer\vswhere.exe"
                }
                $vswhereCandidates += Get-ExecutablePathsFromPath -Name "vswhere.exe"
                $vswherePath = Resolve-ExistingExecutablePath `
                    -ToolName "Visual Studio Installer's vswhere.exe" `
                    -CandidatePaths $vswhereCandidates `
                    -OverrideVariable "DOVAHLINK_VSWHERE_PATH"
                $toolchain = Find-VisualStudioToolchain -LocatorPath $vswherePath
                return New-LocalCiPrerequisiteResult `
                    -Requirement $Requirement `
                    -Available $true `
                    -Version "Supported Visual Studio C++ workload" `
                    -Details $toolchain.InstallationPath `
                    -Path $vswherePath `
                    -Value $toolchain
            }
            "git" {
                $command = Get-Command -Name "git.exe" -CommandType Application -ErrorAction SilentlyContinue | Select-Object -First 1
                if ($null -eq $command) {
                    throw "git.exe was not found on PATH."
                }
                $output = @(& $command.Source --version 2>&1)
                if ($LASTEXITCODE -ne 0) {
                    throw "git --version exited with code $LASTEXITCODE."
                }
                return New-LocalCiPrerequisiteResult -Requirement $Requirement -Available $true -Version (($output | Select-Object -First 1).ToString().Trim()) -Path $command.Source
            }
            "cmake" {
                $candidates = @($env:DOVAHLINK_CMAKE_PATH)
                if (-not [string]::IsNullOrWhiteSpace($env:ChocolateyInstall)) {
                    $candidates += Join-Path $env:ChocolateyInstall "bin\cmake.exe"
                }
                $programFiles = [System.Environment]::GetFolderPath([System.Environment+SpecialFolder]::ProgramFiles)
                if (-not [string]::IsNullOrWhiteSpace($programFiles)) {
                    $candidates += Join-Path $programFiles "CMake\bin\cmake.exe"
                }
                $candidates += Get-ExecutablePathsFromPath -Name "cmake.exe"
                $path = Resolve-PinnedExecutablePath `
                    -ToolName "CMake" `
                    -CandidatePaths $candidates `
                    -ExpectedVersion "cmake version 4.4.2" `
                    -OverrideVariable "DOVAHLINK_CMAKE_PATH"
                return New-LocalCiPrerequisiteResult -Requirement $Requirement -Available $true -Version "4.4.2" -Path $path
            }
            "ninja" {
                $candidates = @($env:DOVAHLINK_NINJA_PATH)
                if (-not [string]::IsNullOrWhiteSpace($env:ChocolateyInstall)) {
                    $candidates += Join-Path $env:ChocolateyInstall "bin\ninja.exe"
                }
                $candidates += @(
                    "C:\ProgramData\chocolatey\bin\ninja.exe",
                    "C:\Program Files\Ninja\ninja.exe"
                )
                $candidates += Get-ExecutablePathsFromPath -Name "ninja.exe"
                $path = Resolve-PinnedExecutablePath `
                    -ToolName "Ninja" `
                    -CandidatePaths $candidates `
                    -ExpectedVersion "1.13.2" `
                    -OverrideVariable "DOVAHLINK_NINJA_PATH"
                return New-LocalCiPrerequisiteResult -Requirement $Requirement -Available $true -Version "1.13.2" -Path $path
            }
            "python" {
                $command = Get-Command -Name "python" -CommandType Application -ErrorAction SilentlyContinue | Select-Object -First 1
                if ($null -eq $command) {
                    throw "python was not found on PATH."
                }
                $output = @(& $command.Source --version 2>&1)
                $version = ($output | Select-Object -First 1).ToString().Trim()
                if ($LASTEXITCODE -ne 0 -or $version -notmatch "^Python 3\.13\.") {
                    throw "Expected Python 3.13.x, but found '$version'."
                }
                return New-LocalCiPrerequisiteResult -Requirement $Requirement -Available $true -Version $version -Path $command.Source
            }
            "dotnet" {
                $command = Get-Command -Name "dotnet.exe" -CommandType Application -ErrorAction SilentlyContinue | Select-Object -First 1
                if ($null -eq $command) {
                    throw "dotnet.exe was not found on PATH."
                }
                $sdks = @(& $command.Source --list-sdks 2>&1)
                if ($LASTEXITCODE -ne 0) {
                    throw "dotnet --list-sdks exited with code $LASTEXITCODE."
                }
                $sdk = $sdks | Where-Object { $_ -match "^9\." } | Select-Object -First 1
                if ($null -eq $sdk) {
                    throw "No .NET 9 SDK was found. A .NET Runtime alone does not include the SDK."
                }
                $null = @(& $command.Source format --version 2>&1)
                if ($LASTEXITCODE -ne 0) {
                    throw "The installed SDK does not provide dotnet format."
                }
                return New-LocalCiPrerequisiteResult -Requirement $Requirement -Available $true -Version (($sdk -split "\s+")[0]) -Details "dotnet format available" -Path $command.Source
            }
            "flutter" {
                $command = Get-Command -Name "flutter" -CommandType Application -ErrorAction SilentlyContinue | Select-Object -First 1
                if ($null -eq $command) {
                    throw "flutter was not found on PATH."
                }
                $output = @(& $command.Source --version --machine 2>&1)
                if ($LASTEXITCODE -ne 0) {
                    throw "flutter --version --machine exited with code $LASTEXITCODE."
                }
                $metadata = ($output -join [Environment]::NewLine) | ConvertFrom-Json
                if ($metadata.channel -ne "stable") {
                    throw "Expected the Flutter stable channel, but found '$($metadata.channel)'."
                }
                return New-LocalCiPrerequisiteResult -Requirement $Requirement -Available $true -Version "Flutter $($metadata.frameworkVersion) ($($metadata.channel))" -Path $command.Source
            }
            "dart" {
                $command = Get-Command -Name "dart" -CommandType Application -ErrorAction SilentlyContinue | Select-Object -First 1
                if ($null -eq $command) {
                    throw "dart was not found on PATH; Flutter's bin directory should provide it."
                }
                $output = @(& $command.Source --version 2>&1)
                if ($LASTEXITCODE -ne 0) {
                    throw "dart --version exited with code $LASTEXITCODE."
                }
                return New-LocalCiPrerequisiteResult -Requirement $Requirement -Available $true -Version (($output | Select-Object -First 1).ToString().Trim()) -Path $command.Source
            }
            "clang-format" {
                $command = Get-Command -Name "clang-format" -CommandType Application -ErrorAction SilentlyContinue | Select-Object -First 1
                if ($null -eq $command) {
                    throw "clang-format was not found on PATH."
                }
                $output = @(& $command.Source --version 2>&1)
                $version = ($output | Select-Object -First 1).ToString().Trim()
                if ($LASTEXITCODE -ne 0 -or $version -notmatch "19\.1\.5") {
                    throw "Expected clang-format 19.1.5, but found '$version'."
                }
                return New-LocalCiPrerequisiteResult -Requirement $Requirement -Available $true -Version $version -Path $command.Source
            }
            "ruff" {
                $command = Get-Command -Name "ruff" -CommandType Application -ErrorAction SilentlyContinue | Select-Object -First 1
                if ($null -eq $command) {
                    throw "ruff was not found on PATH."
                }
                $output = @(& $command.Source --version 2>&1)
                if ($LASTEXITCODE -ne 0) {
                    throw "ruff --version exited with code $LASTEXITCODE."
                }
                return New-LocalCiPrerequisiteResult -Requirement $Requirement -Available $true -Version (($output | Select-Object -First 1).ToString().Trim()) -Path $command.Source
            }
            "psscriptanalyzer" {
                $shell = Get-Command -Name "pwsh.exe" -CommandType Application -ErrorAction SilentlyContinue | Select-Object -First 1
                if ($null -eq $shell) {
                    $shell = Get-Command -Name "powershell.exe" -CommandType Application -ErrorAction SilentlyContinue | Select-Object -First 1
                }
                if ($null -eq $shell) {
                    throw "Neither pwsh nor powershell was found to run Invoke-Formatter."
                }
                $probe = @'
$formatter = Get-Command Invoke-Formatter -ErrorAction SilentlyContinue
if ($null -eq $formatter) { exit 1 }
$module = Get-Module -ListAvailable PSScriptAnalyzer | Sort-Object Version -Descending | Select-Object -First 1
if ($null -ne $module) { Write-Output $module.Version.ToString() } else { Write-Output "Invoke-Formatter available" }
exit 0
'@
                $output = @(& $shell.Source -NoProfile -NonInteractive -Command $probe 2>&1)
                if ($LASTEXITCODE -ne 0) {
                    throw "PSScriptAnalyzer's Invoke-Formatter command was not found in $($shell.Name)."
                }
                return New-LocalCiPrerequisiteResult -Requirement $Requirement -Available $true -Version (($output | Select-Object -First 1).ToString().Trim()) -Path $shell.Source
            }
            default {
                throw "Unknown prerequisite id '$($Requirement.Id)'."
            }
        }
    }
    catch {
        return New-LocalCiPrerequisiteResult -Requirement $Requirement -Available $false -Details $_.Exception.Message
    }
}

<#
.SYNOPSIS
Builds the human-readable prerequisite report lines.

.PARAMETER Results
The results returned by the prerequisite probes.
#>
function Get-LocalCiPrerequisiteReportLines {
    param([Parameter(Mandatory = $true)][object[]]$Results)

    $lines = [System.Collections.Generic.List[string]]::new()
    $lines.Add("Local CI prerequisites")
    foreach ($result in $Results) {
        if ($result.Available) {
            $description = if (-not [string]::IsNullOrWhiteSpace($result.Version)) { $result.Version } else { $result.Details }
            $lines.Add("[OK] $($result.Name) — $description")
        }
        else {
            $lines.Add("[MISSING] $($result.Name) — $($result.Details)")
        }
    }

    $missing = @($Results | Where-Object { -not $_.Available })
    if ($missing.Count -eq 0) {
        $lines.Add("All local CI prerequisites are available. Run tooling/run-local-ci.ps1 to execute the checks.")
    }
    else {
        $lines.Add("Install or repair the items below, then run this check again:")
        foreach ($result in $missing) {
            $lines.Add("  $($result.Name)")
            $lines.Add("    Install: $($result.InstallCommand)")
            $lines.Add("    Verify:  $($result.VerifyCommand)")
            $lines.Add("    Guide:   $($result.InstallUrl)")
        }
    }
    return $lines.ToArray()
}

<#
.SYNOPSIS
Checks every local CI prerequisite and reports all missing tools together.

.PARAMETER RepositoryRoot
The repository root used by prerequisite discovery.

.PARAMETER Probe
Optional test seam that returns Available, Version, Details, Path, and Value properties for a requirement.

.OUTPUTS
A report containing all checks, rendered output lines, and an exit code.
#>
function Invoke-LocalCiPrerequisiteCheck {
    param([scriptblock]$Probe)

    $results = [System.Collections.Generic.List[object]]::new()
    foreach ($requirement in Get-LocalCiPrerequisiteDefinitions) {
        try {
            $probeResult = if ($null -ne $Probe) {
                & $Probe $requirement
            }
            else {
                Test-LocalCiPrerequisite -Requirement $requirement
            }
            if ($null -eq $probeResult) {
                throw "The prerequisite probe returned no result."
            }
            $results.Add((New-LocalCiPrerequisiteResult `
                        -Requirement $requirement `
                        -Available ([bool]$probeResult.Available) `
                        -Version ([string]$probeResult.Version) `
                        -Details ([string]$probeResult.Details) `
                        -Path ([string]$probeResult.Path) `
                        -Value $probeResult.Value))
        }
        catch {
            $results.Add((New-LocalCiPrerequisiteResult -Requirement $requirement -Available $false -Details $_.Exception.Message))
        }
    }

    $missing = @($results | Where-Object { -not $_.Available })
    $lines = @(Get-LocalCiPrerequisiteReportLines -Results $results.ToArray())
    foreach ($line in $lines) {
        Write-Host $line
    }
    return [pscustomobject]@{
        IsReady  = ($missing.Count -eq 0)
        ExitCode = if ($missing.Count -eq 0) { 0 } else { 1 }
        Results  = $results.ToArray()
        Output   = $lines
    }
}

if ($MyInvocation.InvocationName -ne ".") {
    $report = Invoke-LocalCiPrerequisiteCheck
    exit $report.ExitCode
}
