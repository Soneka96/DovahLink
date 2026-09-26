$ErrorActionPreference = 'Stop'

$runnerPath = Join-Path $PSScriptRoot 'run-host-poc.ps1'
$hostDll = Join-Path $PSScriptRoot 'host_poc\bin\Release\net9.0-windows\host_poc.dll'
$realDotnetPath = (Get-Command dotnet -CommandType Application).Source
$realDartPath = (Get-Command dart -CommandType Application | Select-Object -First 1).Source
$originalLastExitCode = $global:LASTEXITCODE
$originalDeleteExitCode = $env:DOVAHLINK_TEST_DELETE_EXIT_CODE
$originalDartExitCode = $env:DOVAHLINK_TEST_DART_EXIT_CODE
$temporaryPath = [System.IO.Path]::GetTempPath()
$temporaryFilesBefore = @(Get-ChildItem -LiteralPath $temporaryPath -Filter 'dovahlink-s2-*' -Name)

if (-not (Test-Path -LiteralPath $hostDll -PathType Leaf)) {
    throw 'Build host_poc.csproj in Release before running the Host POC runner tests.'
}

<#
.SYNOPSIS
Runs the actual Host command while substituting a requested key-deletion exit code.
#>
function dotnet {
    & $script:realDotnetPath @args
    $nativeExitCode = $global:LASTEXITCODE
    if ($args -contains '--delete-key' -and $env:DOVAHLINK_TEST_DELETE_EXIT_CODE) {
        $global:LASTEXITCODE = [int]$env:DOVAHLINK_TEST_DELETE_EXIT_CODE
    }
    else {
        $global:LASTEXITCODE = $nativeExitCode
    }
}

<#
.SYNOPSIS
Runs the actual Client command or returns a test-selected failure code.
#>
function dart {
    if ($env:DOVAHLINK_TEST_DART_EXIT_CODE -and $env:DOVAHLINK_TEST_DART_EXIT_CODE -ne '0') {
        $global:LASTEXITCODE = [int]$env:DOVAHLINK_TEST_DART_EXIT_CODE
    }
    else {
        & $script:realDartPath @args
    }
}

<#
.SYNOPSIS
Collects warnings emitted during the runner test.

.PARAMETER Message
The cleanup warning to retain for assertion.
#>
function Write-Warning {
    param([string]$Message)
    $global:DOVAHLINK_TEST_WARNINGS += $Message
}
$global:DOVAHLINK_TEST_WARNINGS = @()

try {
    $env:DOVAHLINK_TEST_DELETE_EXIT_CODE = '0'
    $env:DOVAHLINK_TEST_DART_EXIT_CODE = '0'
    . $runnerPath

    $env:DOVAHLINK_TEST_DELETE_EXIT_CODE = '17'
    $cleanupFailure = $null
    try {
        . $runnerPath
    }
    catch {
        $cleanupFailure = $_.Exception.Message
    }
    if ($cleanupFailure -notlike '*POC key cleanup*exited with code 17*') {
        throw "The runner did not report failed key cleanup: $cleanupFailure"
    }

    $env:DOVAHLINK_TEST_DART_EXIT_CODE = '23'
    $primaryFailure = $null
    try {
        . $runnerPath
    }
    catch {
        $primaryFailure = $_.Exception.Message
    }
    if ($primaryFailure -notlike '*Dart POC exited with code 23*' -or
        ($global:DOVAHLINK_TEST_WARNINGS -join ' ') -notlike '*POC key cleanup*exited with code 17*') {
        throw "The runner did not preserve its primary and cleanup failures. Primary: $primaryFailure. Cleanup warnings: $($global:DOVAHLINK_TEST_WARNINGS -join ' ')"
    }

    $temporaryFilesAfter = @(Get-ChildItem -LiteralPath $temporaryPath -Filter 'dovahlink-s2-*' -Name)
    $leftoverFiles = @($temporaryFilesAfter | Where-Object { $_ -notin $temporaryFilesBefore })
    if ($leftoverFiles.Count -gt 0) {
        throw "The runner left temporary output files after cleanup failure: $($leftoverFiles -join ', ')"
    }

    Write-Output 'run-host-poc cleanup validation passed.'
}
finally {
    Remove-Item Function:dotnet, Function:dart, Function:Write-Warning
    Remove-Variable DOVAHLINK_TEST_WARNINGS -Scope Global -ErrorAction SilentlyContinue
    $global:LASTEXITCODE = $originalLastExitCode
    $env:DOVAHLINK_TEST_DELETE_EXIT_CODE = $originalDeleteExitCode
    $env:DOVAHLINK_TEST_DART_EXIT_CODE = $originalDartExitCode
}
