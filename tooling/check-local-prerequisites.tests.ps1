$ErrorActionPreference = "Stop"
$InformationPreference = "SilentlyContinue"
. (Join-Path $PSScriptRoot "check-local-prerequisites.ps1")

$script:assertionCount = 0

<#
.SYNOPSIS
Fails the test harness when a condition is false.

.PARAMETER Condition
The condition that must be true.

.PARAMETER Message
The failure message displayed when the condition is false.
#>
function Assert-True {
    param([bool]$Condition, [string]$Message)

    $script:assertionCount++
    if (-not $Condition) {
        throw $Message
    }
}

$definitions = @(Get-LocalCiPrerequisiteDefinitions)
$expectedIds = @(
    "visual-studio", "git", "cmake", "ninja", "python", "dotnet",
    "flutter", "dart", "clang-format", "ruff", "psscriptanalyzer"
)
Assert-True ($definitions.Count -eq $expectedIds.Count) "The prerequisite inventory has an unexpected number of entries."
Assert-True ((@($definitions | ForEach-Object { $_.Id }) -join ",") -eq ($expectedIds -join ",")) "The prerequisite inventory is missing or reorders a required tool."
foreach ($definition in $definitions) {
    Assert-True (-not [string]::IsNullOrWhiteSpace($definition.InstallCommand)) "$($definition.Name) has no install guidance."
    Assert-True (-not [string]::IsNullOrWhiteSpace($definition.VerifyCommand)) "$($definition.Name) has no verification command."
    Assert-True ([Uri]::IsWellFormedUriString($definition.InstallUrl, [UriKind]::Absolute)) "$($definition.Name) has no absolute installation guide URL."
}
$ruffRequirement = $definitions | Where-Object { $_.Id -eq "ruff" } | Select-Object -First 1
Assert-True ($ruffRequirement.VerifyCommand -eq "python -m ruff --version") "Ruff verification must use the Python module command."

$ruffAvailableProbe = {
    param([string]$PythonPath)
    return [pscustomobject]@{ ExitCode = 0; Version = "ruff 0.16.8" }
}
$ruffAvailable = Test-LocalCiRuffModule -PythonPath "C:\test\python.exe" -VersionProbe $ruffAvailableProbe
Assert-True $ruffAvailable.Available "The checker rejected Ruff installed as a Python module."
Assert-True ($ruffAvailable.Version -eq "ruff 0.16.8") "The checker omitted the module version."
Assert-True ($ruffAvailable.Path -eq "C:\test\python.exe") "The checker did not associate Ruff with the selected Python interpreter."

$ruffMissingProbe = {
    param([string]$PythonPath)
    return [pscustomobject]@{ ExitCode = 1; Version = ""; Details = "No module named ruff" }
}
$ruffMissing = Test-LocalCiRuffModule -PythonPath "C:\test\python.exe" -VersionProbe $ruffMissingProbe
Assert-True (-not $ruffMissing.Available) "The checker accepted an unavailable Ruff module."
Assert-True ($ruffMissing.Details -like "*No module named ruff*") "The missing-module diagnostic omitted Python's reason."
Assert-True ($ruffMissing.Details -like "*python -m pip install ruff*") "The missing-module diagnostic omitted its install command."

$vsClangPaths = @(Get-VisualStudioClangFormatCandidatePaths -InstallationPath "C:\Visual Studio")
Assert-True ($vsClangPaths -contains "C:\Visual Studio\VC\Tools\Llvm\x64\bin\clang-format.exe") "The checker did not include Visual Studio's x64 clang-format location."
Assert-True ($vsClangPaths.Count -eq 1) "The checker included clang-format binaries that cannot run on the x64 build host."

$clangVersions = @{
    "C:\tools\clang-format-19.exe"         = "clang-format version 19.1.5 (test build)"
    "C:\Visual Studio\clang-format-22.exe" = "clang-format version 22.1.3 (test build)"
}
$clangVersionProbe = {
    param([string]$ExecutablePath)
    return [pscustomobject]@{ ExitCode = 0; Version = $clangVersions[$ExecutablePath] }
}.GetNewClosure()
$pinnedClangFormat = Test-LocalCiClangFormat -PathCandidates @("C:\tools\clang-format-19.exe") -VersionProbe $clangVersionProbe
Assert-True $pinnedClangFormat.Available "The checker rejected pinned clang-format available on PATH."
Assert-True ($pinnedClangFormat.Version -eq "19.1.5") "The checker reported the wrong pinned clang-format version."

$shadowedPinnedClangFormat = Test-LocalCiClangFormat `
    -PathCandidates @("C:\Visual Studio\clang-format-22.exe", "C:\tools\clang-format-19.exe") `
    -VersionProbe $clangVersionProbe
Assert-True (-not $shadowedPinnedClangFormat.Available) "The checker accepted a pinned clang-format shadowed by an earlier PATH entry."
Assert-True ($shadowedPinnedClangFormat.Details -like "*clang-format-22.exe*resolves first*") "The checker did not identify the clang-format executable that resolves first from PATH."

$newerVisualStudioClangFormat = Test-LocalCiClangFormat `
    -VisualStudioCandidates @("C:\Visual Studio\clang-format-22.exe") `
    -VersionProbe $clangVersionProbe
Assert-True (-not $newerVisualStudioClangFormat.Available) "The checker accepted Visual Studio's unpinned clang-format."
Assert-True ($newerVisualStudioClangFormat.Details -like "*22.1.3*") "The checker did not report Visual Studio's installed clang-format version."
Assert-True ($newerVisualStudioClangFormat.Details -like "*19.1.5 on PATH*") "The checker omitted the required clang-format version and PATH requirement."

$pinnedVisualStudioClangFormat = Test-LocalCiClangFormat `
    -VisualStudioCandidates @("C:\tools\clang-format-19.exe") `
    -VersionProbe $clangVersionProbe
Assert-True (-not $pinnedVisualStudioClangFormat.Available) "The checker accepted a pinned clang-format that the formatter cannot resolve from PATH."
Assert-True ($pinnedVisualStudioClangFormat.Details -like "*not on PATH*") "The checker did not explain that a Visual Studio candidate must be added to PATH."

$missingClangFormat = Test-LocalCiClangFormat
Assert-True (-not $missingClangFormat.Available) "The checker accepted clang-format when no candidate exists."
Assert-True ($missingClangFormat.Details -like "*Install clang-format 19.1.5*") "The missing clang-format diagnostic omitted its pinned install guidance."

$probeCalls = [System.Collections.Generic.List[string]]::new()
$allAvailableProbe = {
    param([psobject]$Requirement)
    $probeCalls.Add($Requirement.Id)
    return [pscustomobject]@{
        Available = $true
        Version   = "test version"
        Details   = "test details"
        Path      = "C:\test\$($Requirement.Id).exe"
        Value     = if ($Requirement.Id -eq "visual-studio") {
            [pscustomobject]@{ InstallationPath = "C:\test\Visual Studio" }
        }
    }
}.GetNewClosure()
$readyReport = Invoke-LocalCiPrerequisiteCheck -Probe $allAvailableProbe 6>$null
Assert-True $readyReport.IsReady "The checker rejected a complete prerequisite set."
Assert-True ($readyReport.ExitCode -eq 0) "The complete prerequisite report returned a failure exit code."
Assert-True ($readyReport.Results.Count -eq $definitions.Count) "The checker did not evaluate every prerequisite."
Assert-True ($probeCalls.Count -eq $definitions.Count) "The checker stopped before probing every available prerequisite."
Assert-True ($readyReport.Output -contains "All local CI prerequisites are available. Run tooling/run-local-ci.ps1 to execute the checks.") "The success report omitted the next command."

$probeCalls.Clear()
$missingIds = @("flutter", "ruff", "psscriptanalyzer")
$missingProbe = {
    param([psobject]$Requirement)
    $probeCalls.Add($Requirement.Id)
    if ($missingIds -contains $Requirement.Id) {
        return [pscustomobject]@{
            Available = $false
            Version   = ""
            Details   = "test installation not found"
            Path      = ""
            Value     = $null
        }
    }
    return [pscustomobject]@{
        Available = $true
        Version   = "test version"
        Details   = "test details"
        Path      = "C:\test\$($Requirement.Id).exe"
        Value     = $null
    }
}.GetNewClosure()
$missingReport = Invoke-LocalCiPrerequisiteCheck -Probe $missingProbe 6>$null
Assert-True (-not $missingReport.IsReady) "The checker accepted missing prerequisites."
Assert-True ($missingReport.ExitCode -eq 1) "The missing-prerequisite report returned the wrong exit code."
Assert-True ($probeCalls.Count -eq $definitions.Count) "The checker stopped after the first missing prerequisite."
foreach ($missingId in $missingIds) {
    $requirement = $definitions | Where-Object { $_.Id -eq $missingId } | Select-Object -First 1
    Assert-True ($missingReport.Output -contains "[MISSING] $($requirement.Name) — test installation not found") "The report omitted missing status for $missingId."
    Assert-True ($missingReport.Output -contains "    Install: $($requirement.InstallCommand)") "The report omitted install guidance for $missingId."
    Assert-True ($missingReport.Output -contains "    Verify:  $($requirement.VerifyCommand)") "The report omitted verification guidance for $missingId."
    Assert-True ($missingReport.Output -contains "    Guide:   $($requirement.InstallUrl)") "The report omitted the official installation link for $missingId."
}

$throwingProbe = {
    param([psobject]$Requirement)
    if ($Requirement.Id -eq "python") {
        throw "test probe failed"
    }
    return [pscustomobject]@{
        Available = $true
        Version   = "test version"
        Details   = "test details"
        Path      = "C:\test\$($Requirement.Id).exe"
        Value     = $null
    }
}.GetNewClosure()
$throwingReport = Invoke-LocalCiPrerequisiteCheck -Probe $throwingProbe 6>$null
$pythonResult = $throwingReport.Results | Where-Object { $_.Id -eq "python" } | Select-Object -First 1
Assert-True (-not $pythonResult.Available) "A failed probe was not reported as missing."
Assert-True ($pythonResult.Details -like "*test probe failed*") "The report discarded the failed probe's diagnostic."
Assert-True ($throwingReport.Results.Count -eq $definitions.Count) "A failed probe prevented the remaining checks from running."

$checkerPath = (Resolve-Path (Join-Path $PSScriptRoot "check-local-prerequisites.ps1")).Path
$shellPath = (Get-Process -Id $PID).Path
$childOutput = @(& $shellPath -NoProfile -NonInteractive -File $checkerPath 2>&1 | ForEach-Object { $_.ToString() })
$childExitCode = $LASTEXITCODE
$childHasMissing = @($childOutput | Where-Object { $_ -match "^\[MISSING\]" }).Count -gt 0
$expectedChildExitCode = if ($childHasMissing) { 1 } else { 0 }
Assert-True ($childExitCode -eq $expectedChildExitCode) "Direct checker invocation returned $childExitCode, but its report implies $expectedChildExitCode."

Write-Host "check-local-prerequisites.ps1 validation passed ($script:assertionCount assertions)."
