# DovahLink Bridge Builder

This Windows GUI builds the native bridge with the repository's pinned Release
CMake preset and creates a Vortex-ready ZIP. It never copies files into Skyrim.

## Use the published builder

Run the standalone executable from:

```text
tooling/out/BridgeBuilder/BridgeBuilder.exe
```

Choose one button:

- **Build Beta** creates `tooling/out/DovahLink-Bridge-<version>-beta.zip`.
- **Build Release** creates `tooling/out/DovahLink-Bridge-<version>.zip`.

The version is read from `bridge/vcpkg.json`, so changing the bridge version
automatically changes the archive name.

The builder uses Visual Studio 2022's bundled x64 toolchain and vcpkg. The
first build can take longer while vcpkg verifies or installs pinned packages;
later builds normally reuse them.

## Install the generated bridge

In Vortex, choose **Install From File**, select the generated ZIP, enable the
installed mod, and click **Deploy Mods**. The ZIP contains only:

```text
Data/SKSE/Plugins/dovahlink_bridge_plugin.dll
Data/SKSE/Plugins/boost_json-vc143-mt-x64-1_91.dll
Data/SKSE/Plugins/fmt.dll
Data/SKSE/Plugins/spdlog.dll
```

Address Library remains a separate mod-manager dependency and is not bundled
in this archive. The Vortex “no source assigned” warning is expected for a
locally generated ZIP; choosing source **Other** dismisses it.

## Build or republish the GUI

From the repository root:

```powershell
dotnet test tooling/BridgeBuilder.Tests/BridgeBuilder.Tests.csproj
dotnet publish tooling/BridgeBuilder/BridgeBuilder.csproj `
  --configuration Release `
  --runtime win-x64 `
  --self-contained true `
  -p:PublishSingleFile=true `
  -p:IncludeNativeLibrariesForSelfExtract=true `
  -p:DebugType=None `
  --output tooling/out/BridgeBuilder
```

Close the existing builder window before republishing so Windows does not lock
the executable.
