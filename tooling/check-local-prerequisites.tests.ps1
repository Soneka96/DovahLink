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
    return New-TestPrerequisiteResult -Requirement $Requirement
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
