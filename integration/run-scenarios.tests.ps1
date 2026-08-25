# Validates Visual Studio discovery and environment import without building the bridge.

$ErrorActionPreference = "Stop"
. (Join-Path $PSScriptRoot "run-scenarios.ps1")

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

.OUTPUTS
The fake installation path.
#>
function New-TestVisualStudioInstallation {
    param([string]$Root, [string]$Edition)

    $installationPath = Join-Path $Root "Microsoft Visual Studio\2022\$Edition"
    $vcvarsDirectory = Join-Path $installationPath "VC\Auxiliary\Build"
    New-Item -ItemType Directory -Path $vcvarsDirectory -Force | Out-Null
    New-Item -ItemType Directory -Path (Join-Path $installationPath "VC\vcpkg") -Force | Out-Null
    $cmakeDirectory = New-Item -ItemType Directory -Path (Join-Path $installationPath "Common7\IDE\CommonExtensions\Microsoft\CMake\CMake\bin") -Force
    $ninjaDirectory = New-Item -ItemType Directory -Path (Join-Path $installationPath "Common7\IDE\CommonExtensions\Microsoft\CMake\Ninja") -Force
    New-Item -ItemType File -Path (Join-Path $cmakeDirectory.FullName "cmake.exe") -Force | Out-Null
    New-Item -ItemType File -Path (Join-Path $ninjaDirectory.FullName "ninja.exe") -Force | Out-Null
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
    $capturePath = Join-Path $testRoot "vswhere arguments.txt"
    $locatorPath = New-TestVsWhere -Root (Join-Path $testRoot "Visual Studio Installer") -CapturePath $capturePath

    foreach ($edition in @("Community", "Professional", "Enterprise")) {
        $installationPath = New-TestVisualStudioInstallation -Root (Join-Path $testRoot $edition) -Edition $edition
        $env:DOVAHLINK_TEST_VSWHERE_RESULT = $installationPath

        $toolchain = Find-VisualStudioToolchain -LocatorPath $locatorPath

        Assert-True ($toolchain.InstallationPath -eq $installationPath) "$edition discovery returned the wrong installation."
        Assert-True ($toolchain.VcvarsallPath -eq (Join-Path $installationPath "VC\Auxiliary\Build\vcvarsall.bat")) "$edition discovery selected the wrong vcvarsall script."
    }

    $locatorArguments = Get-Content -LiteralPath $capturePath -Raw
    Assert-True ($locatorArguments -like "*-products * *") "Discovery did not search every Visual Studio product edition."
    Assert-True ($locatorArguments -like "*Microsoft.VisualStudio.Workload.NativeDesktop*") "Discovery did not require the Desktop development with C++ workload."
    Assert-True ($locatorArguments -like "*Microsoft.VisualStudio.Component.VC.Tools.x86.x64*") "Discovery did not require the MSVC x64/x86 tools."
    Assert-True ($locatorArguments -like "*Microsoft.VisualStudio.Component.VC.CMake.Project*") "Discovery did not require Visual Studio CMake tools."
    Assert-True ($locatorArguments.Contains("-latest")) "Discovery did not select the latest matching installation."
    Assert-True ($locatorArguments.Contains("-version [17.0,18.0)")) "Discovery did not restrict selection to Visual Studio 2022."
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
        CMake     = "Common7\IDE\CommonExtensions\Microsoft\CMake\CMake\bin\cmake.exe"
        Ninja     = "Common7\IDE\CommonExtensions\Microsoft\CMake\Ninja\ninja.exe"
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
    Assert-True ($env:PATH.StartsWith("$($toolchain.CMakeDirectory);$($toolchain.NinjaDirectory);")) "The selected installation's CMake and Ninja directories were not applied."

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

    Write-Host "run-scenarios.ps1 validation passed ($script:assertionCount assertions)."
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
