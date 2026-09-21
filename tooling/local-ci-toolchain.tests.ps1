# Validates Visual Studio discovery and environment import.

$ErrorActionPreference = "Stop"
. (Join-Path $PSScriptRoot "local-ci-toolchain.ps1")

$script:assertionCount = 0

<#
.SYNOPSIS
Fails when a tested condition is false.

.PARAMETER Condition
The condition that must be true.

.PARAMETER Message
The failure message shown to the caller.
#>
function Assert-True {
    param([bool]$Condition, [string]$Message)

    $script:assertionCount++
    if (-not $Condition) {
        throw $Message
    }
}

<#
.SYNOPSIS
Fails when an action does not throw an exception containing the expected text.

.PARAMETER Action
The action expected to fail.

.PARAMETER ExpectedMessage
Text that must appear in the exception message.
#>
function Assert-ThrowsLike {
    param([scriptblock]$Action, [string]$ExpectedMessage)

    $script:assertionCount++
    try {
        & $Action
    }
    catch {
        if ($_.Exception.Message -notlike "*$ExpectedMessage*") {
            throw "Expected an error containing '$ExpectedMessage', but got '$($_.Exception.Message)'."
        }
        return
    }
    throw "Expected an error containing '$ExpectedMessage', but no error was thrown."
}

<#
.SYNOPSIS
Creates a representative Visual Studio installation layout for discovery tests.

.PARAMETER Root
The parent directory for the fake installation.

.PARAMETER Edition
The Visual Studio edition directory to create.

.PARAMETER VersionDirectory
The Visual Studio version directory to create ("2022" or "18").

.OUTPUTS
The fake installation path.
#>
function New-TestVisualStudioInstallation {
    param([string]$Root, [string]$Edition, [string]$VersionDirectory = "2022")

    $installationPath = Join-Path $Root "Microsoft Visual Studio\$VersionDirectory\$Edition"
    $vcvarsDirectory = Join-Path $installationPath "VC\Auxiliary\Build"
    New-Item -ItemType Directory -Path $vcvarsDirectory -Force | Out-Null
    New-Item -ItemType Directory -Path (Join-Path $installationPath "VC\vcpkg") -Force | Out-Null
    Set-Content -LiteralPath (Join-Path $vcvarsDirectory "vcvarsall.bat") -Value @(
        "@echo off",
        "set `"DOVAHLINK_SCENARIO_TEST=environment imported`""
    )
    return $installationPath
}

<#
.SYNOPSIS
Creates a controllable vswhere substitute that records its arguments.

.PARAMETER Root
The directory in which to create the substitute.

.PARAMETER CapturePath
The file that receives the locator arguments.

.OUTPUTS
The substitute executable path.
#>
function New-TestVsWhere {
    param([string]$Root, [string]$CapturePath)

    New-Item -ItemType Directory -Path $Root -Force | Out-Null
    $locatorPath = Join-Path $Root "vswhere.cmd"
    Set-Content -LiteralPath $locatorPath -Value @(
        "@echo off",
        "> `"$CapturePath`" echo %*",
        "if `"%DOVAHLINK_TEST_VSWHERE_BLANK_FIRST%`"==`"1`" echo.",
        "if not `"%DOVAHLINK_TEST_VSWHERE_RESULT%`"==`"`" echo %DOVAHLINK_TEST_VSWHERE_RESULT%",
        "if not `"%DOVAHLINK_TEST_VSWHERE_EXIT_CODE%`"==`"`" exit /b %DOVAHLINK_TEST_VSWHERE_EXIT_CODE%",
        "exit /b 0"
    )
    return $locatorPath
}

$testRoot = Join-Path ([System.IO.Path]::GetTempPath()) "DovahLink Visual Studio discovery $([Guid]::NewGuid().ToString('N'))"
$originalResult = $env:DOVAHLINK_TEST_VSWHERE_RESULT
$originalBlankFirst = $env:DOVAHLINK_TEST_VSWHERE_BLANK_FIRST
$originalExitCode = $env:DOVAHLINK_TEST_VSWHERE_EXIT_CODE
$originalImported = $env:DOVAHLINK_SCENARIO_TEST
    $originalVcpkgRoot = $env:VCPKG_ROOT
    $originalPath = $env:PATH
try {
    New-Item -ItemType Directory -Path $testRoot | Out-Null
    $candidateRoot = Join-Path $testRoot "Tool candidates"
    $configuredExecutable = Join-Path $candidateRoot "configured\tool.exe"
    $standardExecutable = Join-Path $candidateRoot "standard\tool.exe"
    $pathExecutable = Join-Path $candidateRoot "path\tool.exe"
    foreach ($candidate in @($configuredExecutable, $standardExecutable, $pathExecutable)) {
        New-Item -ItemType Directory -Path (Split-Path -Parent $candidate) -Force | Out-Null
        New-Item -ItemType File -Path $candidate -Force | Out-Null
    }

    $resolvedConfiguredExecutable = Resolve-ExistingExecutablePath `
        -ToolName "Test tool" `
        -CandidatePaths @((Join-Path $candidateRoot "missing\tool.exe"), $configuredExecutable, $standardExecutable, $pathExecutable) `
        -OverrideVariable "DOVAHLINK_TEST_TOOL_PATH"
    Assert-True ($resolvedConfiguredExecutable -eq $configuredExecutable) "Executable discovery did not prefer the configured path."

    $resolvedStandardExecutable = Resolve-ExistingExecutablePath `
        -ToolName "Test tool" `
        -CandidatePaths @((Join-Path $candidateRoot "missing\tool.exe"), $standardExecutable, $pathExecutable) `
        -OverrideVariable "DOVAHLINK_TEST_TOOL_PATH"
    Assert-True ($resolvedStandardExecutable -eq $standardExecutable) "Executable discovery did not fall back to the first existing standard path."

    $resolvedPathExecutable = Resolve-ExistingExecutablePath `
        -ToolName "Test tool" `
        -CandidatePaths @((Join-Path $candidateRoot "missing\tool.exe"), $pathExecutable) `
        -OverrideVariable "DOVAHLINK_TEST_TOOL_PATH"
    Assert-True ($resolvedPathExecutable -eq $pathExecutable) "Executable discovery did not accept a PATH-derived path."

    $resolvedAfterEmptyCandidate = Resolve-ExistingExecutablePath `
        -ToolName "Test tool" `
        -CandidatePaths @("", $standardExecutable) `
        -OverrideVariable "DOVAHLINK_TEST_TOOL_PATH"
    Assert-True ($resolvedAfterEmptyCandidate -eq $standardExecutable) "Executable discovery did not skip an empty candidate before a valid fallback."
    Assert-ThrowsLike {
        Resolve-ExistingExecutablePath `
            -ToolName "Test tool" `
            -CandidatePaths @("") `
            -OverrideVariable "DOVAHLINK_TEST_TOOL_PATH"
    } "DOVAHLINK_TEST_TOOL_PATH"
    $resolvedAfterWhitespaceCandidate = Resolve-ExistingExecutablePath `
        -ToolName "Test tool" `
        -CandidatePaths @("   ", $standardExecutable) `
        -OverrideVariable "DOVAHLINK_TEST_TOOL_PATH"
    Assert-True ($resolvedAfterWhitespaceCandidate -eq $standardExecutable) "Executable discovery did not skip a whitespace-only candidate before a valid fallback."
    Assert-ThrowsLike {
        Resolve-ExistingExecutablePath `
            -ToolName "Test tool" `
            -CandidatePaths @("   ") `
            -OverrideVariable "DOVAHLINK_TEST_TOOL_PATH"
    } "DOVAHLINK_TEST_TOOL_PATH"

    $pathSearchDirectory = Join-Path $candidateRoot "PATH search"
    $pathSearchExecutable = Join-Path $pathSearchDirectory "cmake.exe"
    New-Item -ItemType Directory -Path $pathSearchDirectory -Force | Out-Null
    New-Item -ItemType File -Path $pathSearchExecutable -Force | Out-Null
    $env:PATH = $pathSearchDirectory
    $pathSearchCandidates = @(Get-ExecutablePathsFromPath -Name "cmake.exe")
    Assert-True ($pathSearchCandidates -contains $pathSearchExecutable) "Executable discovery did not enumerate applications from PATH."
    $env:PATH = $originalPath

    $wrongCmakePath = Join-Path $candidateRoot "CMake 4.3.1\cmake.cmd"
    $pinnedCmakePath = Join-Path $candidateRoot "custom install\cmake.cmd"
    foreach ($candidate in @($wrongCmakePath, $pinnedCmakePath)) {
        New-Item -ItemType Directory -Path (Split-Path -Parent $candidate) -Force | Out-Null
    }
    Set-Content -LiteralPath $wrongCmakePath -Value @("@echo off", "echo cmake version 4.3.1", "exit /b 0")
    Set-Content -LiteralPath $pinnedCmakePath -Value @("@echo off", "echo cmake version 4.4.2", "exit /b 0")
    $resolvedCmakePath = Resolve-PinnedExecutablePath `
        -ToolName "CMake" `
        -CandidatePaths @($wrongCmakePath, $pinnedCmakePath) `
        -ExpectedVersion "cmake version 4.4.2" `
        -OverrideVariable "DOVAHLINK_CMAKE_PATH"
    Assert-True ($resolvedCmakePath -eq $pinnedCmakePath) "Pinned CMake discovery did not skip an earlier wrong-version installation."

    $resolvedCmakeAfterEmptyCandidate = Resolve-PinnedExecutablePath `
        -ToolName "CMake" `
        -CandidatePaths @("", $pinnedCmakePath) `
        -ExpectedVersion "cmake version 4.4.2" `
        -OverrideVariable "DOVAHLINK_CMAKE_PATH"
    Assert-True ($resolvedCmakeAfterEmptyCandidate -eq $pinnedCmakePath) "Pinned executable discovery did not skip an empty candidate before a valid fallback."
    Assert-ThrowsLike {
        Resolve-PinnedExecutablePath `
            -ToolName "CMake" `
            -CandidatePaths @("") `
            -ExpectedVersion "cmake version 4.4.2" `
            -OverrideVariable "DOVAHLINK_CMAKE_PATH"
    } "DOVAHLINK_CMAKE_PATH"
    $resolvedCmakeAfterWhitespaceCandidate = Resolve-PinnedExecutablePath `
        -ToolName "CMake" `
        -CandidatePaths @("   ", $pinnedCmakePath) `
        -ExpectedVersion "cmake version 4.4.2" `
        -OverrideVariable "DOVAHLINK_CMAKE_PATH"
    Assert-True ($resolvedCmakeAfterWhitespaceCandidate -eq $pinnedCmakePath) "Pinned executable discovery did not skip a whitespace-only candidate before a valid fallback."
    Assert-ThrowsLike {
        Resolve-PinnedExecutablePath `
            -ToolName "CMake" `
            -CandidatePaths @("   ") `
            -ExpectedVersion "cmake version 4.4.2" `
            -OverrideVariable "DOVAHLINK_CMAKE_PATH"
    } "DOVAHLINK_CMAKE_PATH"

    $pinnedNinjaPath = Join-Path $candidateRoot "Ninja 1.13.2\ninja.cmd"
    New-Item -ItemType Directory -Path (Split-Path -Parent $pinnedNinjaPath) -Force | Out-Null
    Set-Content -LiteralPath $pinnedNinjaPath -Value @("@echo off", "echo 1.13.2", "exit /b 0")
    $resolvedNinjaPath = Resolve-PinnedExecutablePath `
        -ToolName "Ninja" `
        -CandidatePaths @($pinnedNinjaPath) `
        -ExpectedVersion "1.13.2" `
        -OverrideVariable "DOVAHLINK_NINJA_PATH"
    Assert-True ($resolvedNinjaPath -eq $pinnedNinjaPath) "Pinned Ninja discovery did not validate the expected version."

    $wrongVersionPath = Join-Path $candidateRoot "Wrong version\cmake.cmd"
    New-Item -ItemType Directory -Path (Split-Path -Parent $wrongVersionPath) -Force | Out-Null
    Set-Content -LiteralPath $wrongVersionPath -Value @("@echo off", "echo cmake version 4.3.1", "exit /b 0")
    Assert-ThrowsLike {
        Resolve-PinnedExecutablePath `
            -ToolName "CMake" `
            -CandidatePaths @($wrongVersionPath) `
            -ExpectedVersion "cmake version 4.4.2" `
            -OverrideVariable "DOVAHLINK_CMAKE_PATH"
    } "DOVAHLINK_CMAKE_PATH"

    $nonzeroVersionProbe = {
        param([string]$CandidatePath)
        [pscustomobject]@{ ExitCode = 7; Version = "cmake version 4.4.2" }
    }
    Assert-ThrowsLike {
        Resolve-PinnedExecutablePath `
            -ToolName "CMake" `
            -CandidatePaths @($pinnedCmakePath) `
            -ExpectedVersion "cmake version 4.4.2" `
            -OverrideVariable "DOVAHLINK_CMAKE_PATH" `
            -VersionProbe $nonzeroVersionProbe
    } "DOVAHLINK_CMAKE_PATH"

    $throwingVersionProbe = {
        param([string]$CandidatePath)
        if ($CandidatePath -eq $wrongCmakePath) {
            throw "version probe failed"
        }
        [pscustomobject]@{ ExitCode = 0; Version = "cmake version 4.4.2" }
    }.GetNewClosure()
    $resolvedAfterProbeFailure = Resolve-PinnedExecutablePath `
        -ToolName "CMake" `
        -CandidatePaths @($wrongCmakePath, $pinnedCmakePath) `
        -ExpectedVersion "cmake version 4.4.2" `
        -OverrideVariable "DOVAHLINK_CMAKE_PATH" `
        -VersionProbe $throwingVersionProbe
    Assert-True ($resolvedAfterProbeFailure -eq $pinnedCmakePath) "Pinned executable discovery did not continue after a candidate's version probe failed."

    $missingExecutable = Join-Path $candidateRoot "missing\tool.exe"
    Assert-ThrowsLike {
        Resolve-ExistingExecutablePath `
            -ToolName "Test tool" `
            -CandidatePaths @($missingExecutable) `
            -OverrideVariable "DOVAHLINK_TEST_TOOL_PATH"
    } "DOVAHLINK_TEST_TOOL_PATH"

    $capturePath = Join-Path $testRoot "vswhere arguments.txt"
    $locatorPath = New-TestVsWhere -Root (Join-Path $testRoot "Visual Studio Installer") -CapturePath $capturePath

    foreach ($edition in @("Community", "Professional", "Enterprise")) {
        $installationPath = New-TestVisualStudioInstallation -Root (Join-Path $testRoot $edition) -Edition $edition
        $env:DOVAHLINK_TEST_VSWHERE_RESULT = $installationPath

        $toolchain = Find-VisualStudioToolchain -LocatorPath $locatorPath

        Assert-True ($toolchain.InstallationPath -eq $installationPath) "$edition discovery returned the wrong installation."
        Assert-True ($toolchain.VcvarsallPath -eq (Join-Path $installationPath "VC\Auxiliary\Build\vcvarsall.bat")) "$edition discovery selected the wrong vcvarsall script."
    }

    $vs2026Installation = New-TestVisualStudioInstallation -Root (Join-Path $testRoot "VS 2026") -Edition "Community" -VersionDirectory "18"
    $env:DOVAHLINK_TEST_VSWHERE_RESULT = $vs2026Installation
    $vs2026Toolchain = Find-VisualStudioToolchain -LocatorPath $locatorPath
    Assert-True ($vs2026Toolchain.InstallationPath -eq $vs2026Installation) "Visual Studio 2026 discovery returned the wrong installation."
    Assert-True ($vs2026Toolchain.VcvarsallPath -eq (Join-Path $vs2026Installation "VC\Auxiliary\Build\vcvarsall.bat")) "Visual Studio 2026 discovery selected the wrong vcvarsall script."

    $locatorArguments = Get-Content -LiteralPath $capturePath -Raw
    Assert-True ($locatorArguments -like "*-products * *") "Discovery did not search every Visual Studio product edition."
    Assert-True ($locatorArguments -like "*Microsoft.VisualStudio.Workload.NativeDesktop*") "Discovery did not require the Desktop development with C++ workload."
    Assert-True ($locatorArguments -like "*Microsoft.VisualStudio.Component.VC.Tools.x86.x64*") "Discovery did not require the MSVC x64/x86 tools."
    Assert-True (-not $locatorArguments.Contains("Microsoft.VisualStudio.Component.VC.CMake.Project")) "Discovery unnecessarily required Visual Studio's bundled CMake tools."
    Assert-True ($locatorArguments.Contains("-latest")) "Discovery did not select the latest matching installation."
    Assert-True ($locatorArguments.Contains("-version [17.0,19.0)")) "Discovery did not include Visual Studio 2022 and Visual Studio 2026."
    Assert-True ($locatorArguments.Contains("-property installationPath")) "Discovery did not request the installation path."

    Assert-ThrowsLike {
        Find-VisualStudioToolchain -LocatorPath (Join-Path $testRoot "missing\vswhere.exe")
    } "Install Visual Studio Installer"

    $directoryLocatorPath = Join-Path $testRoot "Directory Locator\vswhere.exe"
    New-Item -ItemType Directory -Path $directoryLocatorPath -Force | Out-Null
    Assert-ThrowsLike {
        Find-VisualStudioToolchain -LocatorPath $directoryLocatorPath
    } "Install Visual Studio Installer"

    $env:DOVAHLINK_TEST_VSWHERE_EXIT_CODE = "9"
    Assert-ThrowsLike {
        Find-VisualStudioToolchain -LocatorPath $locatorPath
    } "vswhere failed with exit code 9"
    $env:DOVAHLINK_TEST_VSWHERE_EXIT_CODE = $null

    $env:DOVAHLINK_TEST_VSWHERE_RESULT = $null
    Assert-ThrowsLike {
        Find-VisualStudioToolchain -LocatorPath $locatorPath
    } "Add them in Visual Studio Installer"

    $missingPathCases = [ordered]@{
        Vcvarsall = "VC\Auxiliary\Build\vcvarsall.bat"
        Vcpkg     = "VC\vcpkg"
    }
    foreach ($missingPathCase in $missingPathCases.GetEnumerator()) {
        $incompleteInstallation = New-TestVisualStudioInstallation -Root (Join-Path $testRoot "Incomplete $($missingPathCase.Key)") -Edition "Professional"
        Remove-Item -LiteralPath (Join-Path $incompleteInstallation $missingPathCase.Value) -Recurse -Force
        $env:DOVAHLINK_TEST_VSWHERE_RESULT = $incompleteInstallation
        Assert-ThrowsLike {
            Find-VisualStudioToolchain -LocatorPath $locatorPath
        } "required path"
    }

    foreach ($wrongFileTypeCase in $missingPathCases.GetEnumerator()) {
        $wrongTypeInstallation = New-TestVisualStudioInstallation -Root (Join-Path $testRoot "Wrong Type $($wrongFileTypeCase.Key)") -Edition "Enterprise"
        $wrongTypePath = Join-Path $wrongTypeInstallation $wrongFileTypeCase.Value
        Remove-Item -LiteralPath $wrongTypePath -Recurse -Force
        if ($wrongFileTypeCase.Key -eq "Vcpkg") {
            New-Item -ItemType File -Path $wrongTypePath | Out-Null
        }
        else {
            New-Item -ItemType Directory -Path $wrongTypePath | Out-Null
        }
        $env:DOVAHLINK_TEST_VSWHERE_RESULT = $wrongTypeInstallation
        Assert-ThrowsLike {
            Find-VisualStudioToolchain -LocatorPath $locatorPath
        } "required path"
    }

    $trimmedInstallation = New-TestVisualStudioInstallation -Root (Join-Path $testRoot "Trimmed Output") -Edition "Community"
    $env:DOVAHLINK_TEST_VSWHERE_BLANK_FIRST = "1"
    $env:DOVAHLINK_TEST_VSWHERE_RESULT = "   $trimmedInstallation   "
    $trimmedToolchain = Find-VisualStudioToolchain -LocatorPath $locatorPath
    Assert-True ($trimmedToolchain.InstallationPath -eq $trimmedInstallation) "Discovery did not ignore blank output and trim the selected installation path."
    $env:DOVAHLINK_TEST_VSWHERE_BLANK_FIRST = $null

    $installationWithSpaces = New-TestVisualStudioInstallation -Root (Join-Path $testRoot "Path With Spaces") -Edition "Enterprise"
    $env:DOVAHLINK_TEST_VSWHERE_RESULT = $installationWithSpaces
    $toolchain = Find-VisualStudioToolchain -LocatorPath $locatorPath
    Import-VisualStudioEnvironment -Toolchain $toolchain

    Assert-True ($env:DOVAHLINK_SCENARIO_TEST -eq "environment imported") "The selected vcvarsall script was not imported."
    Assert-True ($env:VCPKG_ROOT -eq (Join-Path $installationWithSpaces "VC\vcpkg")) "The selected installation's vcpkg root was not applied."
    Assert-True ($null -eq $toolchain.PSObject.Properties["CMakeDirectory"]) "Visual Studio discovery returned a redundant bundled CMake path."
    Assert-True ($null -eq $toolchain.PSObject.Properties["NinjaDirectory"]) "Visual Studio discovery returned a redundant bundled Ninja path."

    $failedEnvironmentInstallation = New-TestVisualStudioInstallation -Root (Join-Path $testRoot "Failed Environment") -Edition "Community"
    Set-Content -LiteralPath (Join-Path $failedEnvironmentInstallation "VC\Auxiliary\Build\vcvarsall.bat") -Value @(
        "@echo off",
        "set `"DOVAHLINK_SCENARIO_TEST=must not import`"",
        "exit /b 23"
    )
    $env:DOVAHLINK_TEST_VSWHERE_RESULT = $failedEnvironmentInstallation
    $failedToolchain = Find-VisualStudioToolchain -LocatorPath $locatorPath
    $env:DOVAHLINK_SCENARIO_TEST = "unchanged"
    Assert-ThrowsLike {
        Import-VisualStudioEnvironment -Toolchain $failedToolchain
    } "exit code 23"
    Assert-True ($env:DOVAHLINK_SCENARIO_TEST -eq "unchanged") "A failed vcvarsall invocation partially imported its environment."

    $failedNativeCommandPath = Join-Path $testRoot "failed-native-command.cmd"
    Set-Content -LiteralPath $failedNativeCommandPath -Value @(
        "@echo off",
        "exit /b 17"
    )
    Assert-ThrowsLike {
        Invoke-CheckedNativeCommand -FilePath $failedNativeCommandPath
    } "failed-native-command.cmd failed with exit code 17"

    $successfulNativeCommandPath = Join-Path $testRoot "successful-native-command.cmd"
    Set-Content -LiteralPath $successfulNativeCommandPath -Value @(
        "@echo off",
        "echo native-output",
        "echo argument=%~1",
        "exit /b 0"
    )
    $nativeOutput = @(Invoke-CheckedNativeCommand -FilePath $successfulNativeCommandPath -ArgumentList @("argument with spaces"))
    Assert-True ($nativeOutput -contains "native-output") "Checked native command suppressed standard output."
    Assert-True ($nativeOutput -contains "argument=argument with spaces") "Checked native command did not preserve spaced arguments."

    Write-Host "local-ci-toolchain.ps1 validation passed ($script:assertionCount assertions)."
}
finally {
    $env:DOVAHLINK_TEST_VSWHERE_RESULT = $originalResult
    $env:DOVAHLINK_TEST_VSWHERE_BLANK_FIRST = $originalBlankFirst
    $env:DOVAHLINK_TEST_VSWHERE_EXIT_CODE = $originalExitCode
    $env:DOVAHLINK_SCENARIO_TEST = $originalImported
    $env:VCPKG_ROOT = $originalVcpkgRoot
    $env:PATH = $originalPath
    if (Test-Path -LiteralPath $testRoot) {
        Remove-Item -LiteralPath $testRoot -Recurse -Force
    }
}
