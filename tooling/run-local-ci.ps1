# Runs the repository's CI command payloads locally on Windows before a push.
# Usage: powershell -File tooling/run-local-ci.ps1

$ErrorActionPreference = "Stop"
$repoRoot = Split-Path -Parent $PSScriptRoot
$toolchainScript = Join-Path $PSScriptRoot "local-ci-toolchain.ps1"
$vcpkgBaseline = "2f1d605400c8727cc00c15797aba796c88ccd523"
$vcpkgRoot = Join-Path ([System.IO.Path]::GetTempPath()) "DovahLink\vcpkg"

. $toolchainScript

$toolchain = Find-VisualStudioToolchain -LocatorPath (Join-Path ${env:ProgramFiles(x86)} "Microsoft Visual Studio\Installer\vswhere.exe")
Import-VisualStudioEnvironment -Toolchain $toolchain

$cacheRoot = Join-Path ([System.IO.Path]::GetTempPath()) "DovahLink\vcpkg-binary-cache"
New-Item -ItemType Directory -Force -Path $cacheRoot | Out-Null
$env:VCPKG_DEFAULT_BINARY_CACHE = $cacheRoot

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

<#
.SYNOPSIS
Detects and repairs a corrupt vcpkg per-version port cache (buildtrees/versioning).

.DESCRIPTION
vcpkg checks a specific historical revision of a port out into
buildtrees/versioning_/versions/<port>/<tree-sha>/ the first time that pinned version is needed.
This checkout is disposable -- vcpkg regenerates it on demand from its own pinned git history --
but an interruption partway through one checkout (a killed build, a full disk) can leave an empty
directory present with neither a vcpkg.json manifest nor a legacy CONTROL file. vcpkg does not
detect that on its own; it reports "port manifest missing" for every port that happens to need
that corrupt entry in the same run, which looks like a mass unrelated-port failure rather than one
disposable-cache bug. Detect that signature narrowly and remove only this cache, never any other
vcpkg or tool state, so a genuine build failure elsewhere still surfaces normally.

.PARAMETER VcpkgRoot
The local vcpkg checkout root.
#>
function Repair-CorruptVcpkgVersioningCache {
    param(
        [Parameter(Mandatory = $true)][string]$VcpkgRoot
    )

    $versioningRoot = Join-Path $VcpkgRoot "buildtrees\versioning_"
    $versionsRoot = Join-Path $versioningRoot "versions"
    if (-not (Test-Path -LiteralPath $versionsRoot -PathType Container)) {
        return
    }

    # Exactly two levels deep from $versionsRoot (<port>\<tree-sha>), never recursing into a
    # checkout's own contents: a valid checkout can itself contain nested subdirectories (source
    # trees, generated build files) that have no manifest of their own and would otherwise be
    # mistaken for corrupt checkout roots by a full recursive leaf scan.
    $portVersionCheckouts = Get-ChildItem -LiteralPath $versionsRoot -Directory -ErrorAction SilentlyContinue |
        ForEach-Object { Get-ChildItem -LiteralPath $_.FullName -Directory -ErrorAction SilentlyContinue }
    $isCorrupt = $false
    foreach ($checkout in $portVersionCheckouts) {
        $hasManifest = (Test-Path -LiteralPath (Join-Path $checkout.FullName "vcpkg.json") -PathType Leaf) -or
        (Test-Path -LiteralPath (Join-Path $checkout.FullName "CONTROL") -PathType Leaf)
        if (-not $hasManifest) {
            $isCorrupt = $true
            break
        }
    }

    if ($isCorrupt) {
        Write-Host "Detected a corrupt vcpkg per-version port cache at '$versioningRoot' (a port checkout is missing its manifest); removing this disposable cache so vcpkg regenerates it."
        Remove-Item -LiteralPath $versioningRoot -Recurse -Force
    }
}
Repair-CorruptVcpkgVersioningCache -VcpkgRoot $vcpkgRoot

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
    throw "A .NET 9 SDK is required for the Host build and test steps below."
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
# Mirror tooling-ci's changed-file formatter check, including committed and local branch changes.
Invoke-LocalCommand -WorkingDirectory $repoRoot -FilePath "python" -ArgumentList @(
    "tooling/format_staged.py", "--check", "--base-ref", "main"
)

Write-Host "=== host-ci ==="
Invoke-LocalCommand -WorkingDirectory $repoRoot -FilePath "dotnet" -ArgumentList @(
    "restore", "host/DovahLink.Host.Tests/DovahLink.Host.Tests.csproj"
)
Invoke-LocalCommand -WorkingDirectory $repoRoot -FilePath "dotnet" -ArgumentList @(
    "build", "host/DovahLink.Host.Tests/DovahLink.Host.Tests.csproj", "--configuration", "Release",
    "--no-restore", "--no-incremental"
)
$hostExecutablePath = Join-Path $repoRoot "host\DovahLink.Host\bin\Release\net9.0-windows\DovahLink.Host.exe"
if (-not (Test-Path -LiteralPath $hostExecutablePath -PathType Leaf)) {
    throw "Expected headless host executable was not built: $hostExecutablePath"
}
Invoke-LocalCommand -WorkingDirectory $repoRoot -FilePath "dotnet" -ArgumentList @(
    "test", "host/DovahLink.Host.Tests/DovahLink.Host.Tests.csproj", "--configuration", "Release",
    "--no-restore", "--no-build"
)
Invoke-LocalCommand -WorkingDirectory $repoRoot -FilePath "dotnet" -ArgumentList @(
    "build", "host/DovahLink.Host.Tests/DovahLink.Host.Tests.csproj", "--configuration", "Release",
    "--no-restore", "--no-incremental", "-p:GenerateDocumentationFile=true", "-p:TreatWarningsAsErrors=true"
)

Write-Host "=== builder-ci ==="
# -p:Configuration=Release: DovahLinkBuilder.csproj only sets RuntimeIdentifier under the Release
# condition, so a Debug-implied restore leaves the Release build without a net9.0-windows/win-x64
# target in project.assets.json (NETSDK1047).
Invoke-LocalCommand -WorkingDirectory $repoRoot -FilePath "dotnet" -ArgumentList @(
    "restore", "tooling/DovahLinkBuilder/DovahLinkBuilder.slnx", "-p:Configuration=Release"
)
Invoke-LocalCommand -WorkingDirectory $repoRoot -FilePath "dotnet" -ArgumentList @(
    "build", "tooling/DovahLinkBuilder/DovahLinkBuilder.slnx", "--configuration", "Release", "--no-restore"
)
Invoke-LocalCommand -WorkingDirectory $repoRoot -FilePath "dotnet" -ArgumentList @(
    "test", "tooling/DovahLinkBuilder/DovahLinkBuilder.slnx", "--configuration", "Release",
    "--no-restore", "--no-build"
)
Invoke-LocalCommand -WorkingDirectory $repoRoot -FilePath "dotnet" -ArgumentList @(
    "publish", "tooling/DovahLinkBuilder/DovahLinkBuilder/DovahLinkBuilder.csproj",
    "-p:PublishProfile=FolderProfile", "--no-restore"
)
$builderExecutablePath = Join-Path $repoRoot "tooling\out\DovahLinkBuilder\DovahLinkBuilder.exe"
if (-not (Test-Path -LiteralPath $builderExecutablePath -PathType Leaf)) {
    throw "Expected published DovahLinkBuilder executable was not built: $builderExecutablePath"
}

Write-Host "=== app-ci ==="
$sdkDirectory = Join-Path $repoRoot "sdk\dart\dovahlink_client"
Invoke-LocalCommand -WorkingDirectory $sdkDirectory -FilePath "dart" -ArgumentList @("pub", "get")
Invoke-LocalCommand -WorkingDirectory $sdkDirectory -FilePath "dart" -ArgumentList @("run", "build_runner", "build")
Invoke-LocalCommand -WorkingDirectory $sdkDirectory -FilePath "dart" -ArgumentList @("analyze")
Invoke-LocalCommand -WorkingDirectory $sdkDirectory -FilePath "dart" -ArgumentList @("test")

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

Write-Host "=== adapter-ci ==="
# The real Host<->Adapter process test launches the built headless host executable; it must exist
# before the Debug native test run below. Only the buildable project, not DovahLink.Host.Tests:
# host-ci's section above already covers the host's own test suite.
# -p:Platform=AnyCPU is required here: this script's top-of-file Import-VisualStudioEnvironment call
# already imported the MSVC developer environment, which exports a Platform=x64 environment variable
# (from vcvarsall.bat) that MSBuild otherwise silently adopts, redirecting the build to
# bin\x64\Debug\... instead of the bin\Debug\... path adapter/CMakeLists.txt's
# DOVAHLINK_HOST_EXECUTABLE and the real-process test both expect.
Invoke-LocalCommand -WorkingDirectory $repoRoot -FilePath "dotnet" -ArgumentList @(
    "build", "host/DovahLink.Host/DovahLink.Host.csproj", "--configuration", "Debug", "-p:Platform=AnyCPU"
)
# The real production publishing strategy (tooling/package_adapter_host.py uses the same flags):
# proves the adapter's real launch/supervise path against the actual packaged artifact shape, and
# is reused by AssembleRealAdapterHostPackage's CTest fixture below instead of publishing twice.
Invoke-LocalCommand -WorkingDirectory $repoRoot -FilePath "dotnet" -ArgumentList @(
    "publish", "host/DovahLink.Host/DovahLink.Host.csproj",
    "--configuration", "Release", "--runtime", "win-x64", "--self-contained", "true",
    "-p:PublishSingleFile=true", "-p:IncludeNativeLibrariesForSelfExtract=true", "-p:DebugType=None",
    "--output", "host/DovahLink.Host/bin/publish/win-x64"
)
$adapterDirectory = Join-Path $repoRoot "adapter"
Invoke-LocalCommand -WorkingDirectory $adapterDirectory -FilePath "cmake" -ArgumentList @("--preset", "windows-x64-debug", "-DCMAKE_MAKE_PROGRAM=$ninjaPath")
Invoke-LocalCommand -WorkingDirectory $adapterDirectory -FilePath "cmake" -ArgumentList @("--build", "--preset", "windows-x64-debug")
# Runs the complete native Adapter suite -- IPC, pairing notification, trust-admin, Papyrus
# registration, plugin, and the real Host<->Adapter process integration tests -- in one pass.
# AssembleRealAdapterHostPackage and its dependent [package]-labeled test are discovered here too,
# but self-skip against this Debug build; the ctest -L package invocation below is where they
# actually run.
Invoke-LocalCommand -WorkingDirectory $adapterDirectory -FilePath "ctest" -ArgumentList @("--preset", "windows-x64-debug")
# Otherwise compile-only here too, mirroring adapter-ci.yml: adapter's own CMakePresets.json
# defines a ctest testPreset only for windows-x64-debug, matching bridge's identical convention.
# Release also provides the Release-named runtime DLLs (fmt.dll/spdlog.dll, unlike Debug's
# debug-suffixed names) the real-package-layout test below requires.
Invoke-LocalCommand -WorkingDirectory $adapterDirectory -FilePath "cmake" -ArgumentList @("--preset", "windows-x64-release", "-DCMAKE_MAKE_PROGRAM=$ninjaPath")
Invoke-LocalCommand -WorkingDirectory $adapterDirectory -FilePath "cmake" -ArgumentList @("--build", "--preset", "windows-x64-release")
# The one test genuinely tied to Release: AssembleRealAdapterHostPackage's CTest fixture requires
# Release-named runtime DLLs, so it self-skips against Debug's build instead of failing there.
# -L package runs only the tests adapter/CMakeLists.txt labeled "package", not the full suite the
# windows-x64-debug ctest run above already ran.
Invoke-LocalCommand -WorkingDirectory $adapterDirectory -FilePath "ctest" -ArgumentList @(
    "--test-dir", "build/windows-x64-release", "-L", "package", "--output-on-failure"
)

Write-Host "All local CI command payloads passed."
