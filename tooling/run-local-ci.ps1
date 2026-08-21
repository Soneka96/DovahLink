# Runs the repository's CI command payloads locally on Windows before a push.
# Usage: powershell -File tooling/run-local-ci.ps1

$ErrorActionPreference = "Stop"
$repoRoot = Split-Path -Parent $PSScriptRoot
$integrationScript = Join-Path $repoRoot "integration\run-scenarios.ps1"
$vcpkgBaseline = "2f1d605400c8727cc00c15797aba796c88ccd523"
$vcpkgRoot = Join-Path ([System.IO.Path]::GetTempPath()) "DovahLink\vcpkg"

. $integrationScript

$toolchain = Find-VisualStudioToolchain -LocatorPath (Join-Path ${env:ProgramFiles(x86)} "Microsoft Visual Studio\Installer\vswhere.exe")
Import-VisualStudioEnvironment -Toolchain $toolchain

$cacheRoot = Join-Path ([System.IO.Path]::GetTempPath()) "DovahLink\vcpkg-binary-cache"
New-Item -ItemType Directory -Force -Path $cacheRoot | Out-Null
$env:VCPKG_DEFAULT_BINARY_CACHE = $cacheRoot

$registriesCacheRoot = Join-Path ([System.IO.Path]::GetTempPath()) "DovahLink\vcpkg-registries-cache"
New-Item -ItemType Directory -Force -Path $registriesCacheRoot | Out-Null
$env:X_VCPKG_REGISTRIES_CACHE = $registriesCacheRoot

if (-not (Test-Path -LiteralPath $vcpkgRoot -PathType Container)) {
    New-Item -ItemType Directory -Force -Path (Split-Path -Parent $vcpkgRoot) | Out-Null
    Invoke-CheckedNativeCommand -FilePath "git" -ArgumentList @(
        "clone", "https://github.com/microsoft/vcpkg.git", $vcpkgRoot
    )
}
elseif (-not (Test-Path -LiteralPath (Join-Path $vcpkgRoot ".git") -PathType Container)) {
    throw "The local vcpkg path '$vcpkgRoot' exists but is not a Git checkout. Remove it and rerun the preflight."
}

# bootstrap-vcpkg.bat always recompiles vcpkg.exe from source, regardless of whether anything
# changed, so skip both it and the checkout when the working copy is already at the pinned commit
# and vcpkg.exe actually runs (not just present -- a prior bootstrap interrupted mid-build could
# leave a truncated exe that "exists" but doesn't work).
$vcpkgExePath = Join-Path $vcpkgRoot "vcpkg.exe"
$currentVcpkgCommit = (& git -C $vcpkgRoot rev-parse HEAD)
if ($LASTEXITCODE -ne 0) {
    throw "Reading the current vcpkg checkout commit failed with exit code $LASTEXITCODE."
}
$vcpkgAlreadyBootstrapped = $false
if ((Test-Path -LiteralPath $vcpkgExePath -PathType Leaf) -and ($currentVcpkgCommit.Trim() -eq $vcpkgBaseline)) {
    & $vcpkgExePath version | Out-Null
    $vcpkgAlreadyBootstrapped = ($LASTEXITCODE -eq 0)
}

if (-not $vcpkgAlreadyBootstrapped) {
    Invoke-CheckedNativeCommand -FilePath "git" -ArgumentList @(
        "-C", $vcpkgRoot, "checkout", $vcpkgBaseline
    )
    Invoke-CheckedNativeCommand -FilePath (Join-Path $vcpkgRoot "bootstrap-vcpkg.bat") -ArgumentList @("-disableMetrics")
}
$env:VCPKG_ROOT = $vcpkgRoot

$cmakeCandidates = @(
    (Join-Path $env:ChocolateyInstall "bin\cmake.exe"),
    "C:\Program Files\CMake\bin\cmake.exe"
)
$cmakePath = $cmakeCandidates | Where-Object { Test-Path -LiteralPath $_ -PathType Leaf } | Select-Object -First 1
if ($null -eq $cmakePath) {
    throw "Pinned CMake 4.4.2 was not found. Install it before running the local preflight."
}

$ninjaCandidates = @(
    (Join-Path $env:ChocolateyInstall "bin\ninja.exe"),
    "C:\ProgramData\chocolatey\bin\ninja.exe",
    "C:\Program Files\Ninja\ninja.exe"
)
$ninjaPath = $ninjaCandidates | Where-Object { Test-Path -LiteralPath $_ -PathType Leaf } | Select-Object -First 1
if ($null -eq $ninjaPath) {
    throw "Pinned Ninja 1.13.2 was not found. Install it before running the local preflight."
}

$env:PATH = "$(Split-Path -Parent $cmakePath);$(Split-Path -Parent $ninjaPath);$env:PATH"

$cmakeOutput = @(& cmake --version)
if ($LASTEXITCODE -ne 0) {
    throw "CMake version check failed with exit code $LASTEXITCODE."
}
$cmakeVersion = ($cmakeOutput | Select-Object -First 1).Trim()
if ($cmakeVersion -ne "cmake version 4.4.2") {
    throw "Expected CMake 4.4.2, but found '$cmakeVersion'."
}

$ninjaOutput = @(& ninja --version)
if ($LASTEXITCODE -ne 0) {
    throw "Ninja version check failed with exit code $LASTEXITCODE."
}
$ninjaVersion = ($ninjaOutput | Select-Object -First 1).Trim()
if ($ninjaVersion -ne "1.13.2") {
    throw "Expected Ninja 1.13.2, but found '$ninjaVersion'."
}

$pythonOutput = @(& python --version 2>&1)
if ($LASTEXITCODE -ne 0) {
    throw "Python version check failed with exit code $LASTEXITCODE."
}
$pythonVersion = ($pythonOutput | Select-Object -First 1).Trim()
if ($pythonVersion -notmatch "^Python 3\.13\.") {
    throw "Expected Python 3.13, but found '$pythonVersion'."
}

$dotnetSdks = @(& dotnet --list-sdks)
if ($LASTEXITCODE -ne 0) {
    throw "The .NET SDK version check failed with exit code $LASTEXITCODE."
}
if (-not ($dotnetSdks -match "^9\.")) {
    throw "A .NET 9 SDK is required for the integration scenarios."
}

if ($null -eq (Get-Command flutter -ErrorAction SilentlyContinue)) {
    throw "Flutter is required for app-ci checks but was not found on PATH."
}

function Invoke-LocalCommand {
    <#
    .SYNOPSIS
    Runs a repository CI command in the requested working directory.

    .PARAMETER WorkingDirectory
    The repository directory in which to run the command.

    .PARAMETER FilePath
    The executable to invoke.

    .PARAMETER ArgumentList
    The arguments passed to the executable.
    #>
    param(
        [Parameter(Mandatory = $true)][string]$WorkingDirectory,
        [Parameter(Mandatory = $true)][string]$FilePath,
        [string[]]$ArgumentList = @()
    )

    Push-Location $WorkingDirectory
    try {
        Invoke-CheckedNativeCommand -FilePath $FilePath -ArgumentList $ArgumentList
    }
    finally {
        Pop-Location
    }
}

Write-Host "=== tooling-ci ==="
Invoke-LocalCommand -WorkingDirectory $repoRoot -FilePath "python" -ArgumentList @(
    "-m", "unittest", "discover", "-s", "tooling", "-p", "test_*.py"
)

Write-Host "=== app-ci ==="
$sdkDirectory = Join-Path $repoRoot "sdk\dart\dovahlink_client"
Invoke-LocalCommand -WorkingDirectory $sdkDirectory -FilePath "dart" -ArgumentList @("pub", "get")
Invoke-LocalCommand -WorkingDirectory $sdkDirectory -FilePath "dart" -ArgumentList @("run", "build_runner", "build")

$appDirectory = Join-Path $repoRoot "app"
Invoke-LocalCommand -WorkingDirectory $appDirectory -FilePath "flutter" -ArgumentList @("pub", "get")

# build_runner's incremental cache in .dart_tool/build can go stale across branch switches and
# refactors (for example a moved/renamed source file it still remembers under the old path). The
# hosted CI runner never hits this since it always starts from a fresh checkout; clearing it here
# keeps this local preflight from failing on stale local state instead of a real problem.
$appBuildCache = Join-Path $appDirectory ".dart_tool\build"
if (Test-Path -LiteralPath $appBuildCache) {
    Remove-Item -LiteralPath $appBuildCache -Recurse -Force
}

Invoke-LocalCommand -WorkingDirectory $appDirectory -FilePath "dart" -ArgumentList @("run", "build_runner", "build")
Invoke-LocalCommand -WorkingDirectory $appDirectory -FilePath "flutter" -ArgumentList @("analyze")
Invoke-LocalCommand -WorkingDirectory $appDirectory -FilePath "flutter" -ArgumentList @("test")
Invoke-LocalCommand -WorkingDirectory $appDirectory -FilePath "flutter" -ArgumentList @("build", "windows", "--debug")

Write-Host "=== bridge-ci ==="
$bridgeDirectory = Join-Path $repoRoot "bridge"
Invoke-LocalCommand -WorkingDirectory $bridgeDirectory -FilePath "cmake" -ArgumentList @("--preset", "windows-x64-debug")
Invoke-LocalCommand -WorkingDirectory $bridgeDirectory -FilePath "cmake" -ArgumentList @("--build", "--preset", "windows-x64-debug")
Invoke-LocalCommand -WorkingDirectory $bridgeDirectory -FilePath "ctest" -ArgumentList @("--preset", "windows-x64-debug")
Invoke-LocalCommand -WorkingDirectory $bridgeDirectory -FilePath "cmake" -ArgumentList @("--preset", "windows-x64-release")
Invoke-LocalCommand -WorkingDirectory $bridgeDirectory -FilePath "cmake" -ArgumentList @("--build", "--preset", "windows-x64-release")
Invoke-LocalCommand -WorkingDirectory $bridgeDirectory -FilePath "ctest" -ArgumentList @(
    "--test-dir", "build/windows-x64-release", "--output-on-failure"
)

Write-Host "=== integration-ci ==="
# dovahlink_bridge_harness has no EXCLUDE_FROM_ALL, so bridge-ci's plain Debug build above already
# built it into build/windows-x64-debug; nothing has changed on disk since, so reconfiguring and
# rebuilding it here would just be a duplicate no-op. This only holds because this script runs
# both sections in one sequential process -- integration-ci.yml runs as its own job on a fresh
# runner with no bridge-ci build tree to reuse, so it must configure and build independently there.
Invoke-LocalCommand -WorkingDirectory $repoRoot -FilePath "dotnet" -ArgumentList @(
    "restore", "integration/DovahLinkValidation.sln"
)
Invoke-LocalCommand -WorkingDirectory $repoRoot -FilePath "dotnet" -ArgumentList @(
    "test", "integration/DovahLinkValidation.sln", "--configuration", "Release", "--no-restore",
    "--logger", "trx;LogFileName=integration.trx", "--results-directory", "integration/TestResults"
)

Write-Host "All local CI command payloads passed."
