# Builds the Skyrim-independent C++ bridge harness and runs every
# DovahLinkValidationClient.Tests scenario against it. Each test launches
# and tears down its own harness instance (see HarnessProcess.cs); this
# script's job is only to make sure the harness executable exists first and
# to give a human a single command to run every validation scenario.
#
# Usage: powershell -File integration/run-scenarios.ps1

$ErrorActionPreference = "Stop"
$repoRoot = Split-Path -Parent $PSScriptRoot

Write-Host "=== Building the bridge harness ==="
$vsPath = "C:\Program Files\Microsoft Visual Studio\2022\Community"
$tmpFile = New-TemporaryFile
cmd /c "`"$vsPath\VC\Auxiliary\Build\vcvarsall.bat`" x64 && set > `"$($tmpFile.FullName)`""
Get-Content $tmpFile.FullName | ForEach-Object {
    if ($_ -match '^([^=]+)=(.*)$') {
        [System.Environment]::SetEnvironmentVariable($matches[1], $matches[2], 'Process')
    }
}
$env:VCPKG_ROOT = "$vsPath\VC\vcpkg"
$env:PATH = "$vsPath\Common7\IDE\CommonExtensions\Microsoft\CMake\CMake\bin;$vsPath\Common7\IDE\CommonExtensions\Microsoft\CMake\Ninja;$env:PATH"

Push-Location (Join-Path $repoRoot "bridge")
try {
    cmake --preset windows-x64-debug
    cmake --build --preset windows-x64-debug --target dovahlink_bridge_harness
}
finally {
    Pop-Location
}

Write-Host "=== Running validation-client scenarios ==="
Push-Location (Join-Path $repoRoot "integration")
try {
    dotnet test DovahLinkValidation.sln
}
finally {
    Pop-Location
}
