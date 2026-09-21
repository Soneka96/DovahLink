# DovahLink Builder

This Windows GUI builds the native Adapter, compiles the optional
trust-administration console script, and orchestrates the existing
[`tooling/package_adapter_host.py`](../package_adapter_host.py) script to
publish the Host and assemble a Vortex-ready ZIP. It never copies files into
Skyrim, and it never reimplements the package layout itself --
`package_adapter_host.py` remains the one canonical implementation.

## Use the published builder

Run the standalone executable from:

```text
tooling/out/DovahLinkBuilder/DovahLinkBuilder.exe
```

The **Build** page picks a build profile (**Release** for distribution;
**Debug** or **Beta** for local development and pre-release testing), an
optional clean-build option, and an optional local note, then starts the build
from **Build**. A running build can be cancelled; its pipeline of eight stages
(repository validation, Adapter configure/build, Papyrus compile, Host
publish, package assembly/validation, and archiving) is shown live, along with
a scrolling log. The **Environment** page shows the same required-toolchain
and git-status checks the Build page gates on, with a manual recheck. The
**Settings** page lets you override the repository, build output, and Skyrim /
Creation Kit install paths, and change a few behavior toggles. The configured
Skyrim path is used when locating the Papyrus compiler.

A successful build writes `<output>/DovahLink-Adapter-<version>[-<profile>].zip`,
where the version is read from the repository-root [`VERSION`](../../VERSION)
file, the `-<profile>` suffix is added for Debug and Beta builds (never for
Release, so its archive name is unchanged from before profiles existed), and
`<output>` is `tooling/out` for a Release build (`tooling/out/debug` or
`tooling/out/beta` for the other profiles, or the path configured on the
Settings page when an output override is set -- in which case every profile
shares that same folder, distinguished only by the filename suffix). The
Build page also keeps a local history of recent builds, and can copy a
plain-text diagnostics report (environment checks, git status, and the last
build's outcome) to the clipboard.

The builder supports Visual Studio 2022 and Visual Studio 2026. It checks
`VSINSTALLDIR` first, then discovers installed instances through Visual Studio
Installer's `vswhere.exe`, with standard installation paths as a fallback. The
selected installation supplies the x64 toolchain, bundled vcpkg, CMake, and
Ninja used by Adapter builds. CMake is used for both preflight and builds. Set
`DOVAHLINK_VSWHERE_PATH` if Visual Studio Installer's locator is outside its
standard location and is not on `PATH`. The first build can take longer while
vcpkg verifies or installs pinned packages; later builds normally reuse them.
Packaging also requires `python` to be resolvable on `PATH` (the same interpreter this repository's
other `tooling/*.py` scripts and local CI already depend on).

The builder also compiles `console-admin/DovahLinkAdmin.psc` with Creation
Kit's Papyrus Compiler and passes `console-admin/dovahlink.yaml` through to
packaging (see [`console-admin/README.md`](../../console-admin/README.md)).
This requires a Skyrim Special Edition installation containing
`Papyrus Compiler\PapyrusCompiler.exe`; the builder checks the configured
Settings path first, then `SKYRIM_INSTALL_DIR`, and finally the standard Steam
install path. Clear the Settings path to return to that automatic discovery order.

## Install the generated package

In Vortex, choose **Install From File**, select the generated ZIP, enable the
installed mod, and click **Deploy Mods**. The ZIP contains only:

```text
Data/SKSE/Plugins/dovahlink_adapter_plugin.dll
Data/SKSE/Plugins/fmt.dll
Data/SKSE/Plugins/spdlog.dll
Data/SKSE/Plugins/DovahLink.Host/DovahLink.Host.exe
Data/Scripts/DovahLinkAdmin.pex
Data/SKSE/CustomConsole/dovahlink.yaml
```

Address Library remains a separate mod-manager dependency and is not bundled
in this archive. The Vortex “no source assigned” warning is expected for a
locally generated ZIP; choosing source **Other** dismisses it.

## Build or republish the GUI

From the repository root:

```powershell
dotnet test tooling/DovahLinkBuilder/DovahLinkBuilder.slnx
dotnet publish tooling/DovahLinkBuilder/DovahLinkBuilder/DovahLinkBuilder.csproj `
  --configuration Release `
  --runtime win-x64 `
  --self-contained true `
  -p:PublishSingleFile=true `
  -p:IncludeNativeLibrariesForSelfExtract=true `
  -p:DebugType=None `
  --output tooling/out/DovahLinkBuilder
```

Close the existing builder window before republishing so Windows does not lock
the executable.
