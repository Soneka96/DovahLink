"""Publishes the Host and assembles the Vortex-installable Adapter+Host mod package.

Publishes the Host self-contained, single-file, win-x64 (this repository's production .NET
publishing strategy -- a self-contained Host never depends on an end user having a matching .NET
runtime installed), then copies it alongside an already-built Adapter plugin DLL and its runtime
dependencies into the Vortex `Data/SKSE/Plugins/...` layout `adapter/process/adapter_host_constants.hpp`'s
`kAdapterHostExecutableRelativePath` expects, and zips the result.
"""

from __future__ import annotations

import shutil
from pathlib import Path

from adapter_host_process_runner import IProcessRunner

# ---- Publish strategy ----

# Production .NET publishing strategy: self-contained, single-file, win-x64.
PUBLISH_ARGS = (
    "--configuration",
    "Release",
    "--runtime",
    "win-x64",
    "--self-contained",
    "true",
    "-p:PublishSingleFile=true",
    "-p:IncludeNativeLibrariesForSelfExtract=true",
    "-p:DebugType=None",
)

# ---- Package layout ----

ADAPTER_PLUGIN_NAME = "dovahlink_adapter_plugin.dll"
ADAPTER_RUNTIME_DLL_NAMES = ("fmt.dll", "spdlog.dll")
HOST_EXECUTABLE_NAME = "DovahLink.Host.exe"
HOST_EXECUTABLE_RELATIVE_DIR = "DovahLink.Host"


class AdapterHostPackager:
    """Publishes the Host and assembles the Vortex-installable Adapter+Host package."""

    def __init__(self, process_runner: IProcessRunner) -> None:
        """Stores the injected process runner used to publish the Host.

        Args:
            process_runner: Runs the `dotnet publish` command.
        """
        self._process_runner = process_runner

    def publish_host(self, host_project: Path, publish_output_dir: Path) -> None:
        """Publishes the Host self-contained, single-file, win-x64 to `publish_output_dir`.

        Args:
            host_project: Path to `DovahLink.Host.csproj`.
            publish_output_dir: Directory `dotnet publish` writes the published executable into.
        """
        publish_output_dir.mkdir(parents=True, exist_ok=True)
        self._process_runner.run(
            [
                "dotnet",
                "publish",
                str(host_project),
                *PUBLISH_ARGS,
                "--output",
                str(publish_output_dir),
            ]
        )

    def assemble_package(
        self,
        *,
        adapter_build_dir: Path,
        host_publish_dir: Path,
        package_dir: Path,
        console_admin_pex: Path | None = None,
        console_admin_yaml: Path | None = None,
    ) -> None:
        """Assembles the Vortex `Data/` layout under `package_dir` from already-built artifacts.

        The optional trust-administration console adapter files are included only when supplied;
        the package works completely normally without them, per `console-admin/README.md`.

        Args:
            adapter_build_dir: Directory containing the built adapter plugin DLL and its runtime
                dependency DLLs (for example `adapter/build/windows-x64-release`).
            host_publish_dir: Directory `publish_host` wrote the published Host executable into.
            package_dir: Directory the `Data/` layout is assembled under. Any pre-existing content
                is discarded first, so a file from a previous, differently-configured run never
                survives into this one.
            console_admin_pex: Path to an already-compiled `DovahLinkAdmin.pex`, or `None` to omit
                the optional console-admin surface entirely.
            console_admin_yaml: Path to `dovahlink.yaml`, or `None` to omit it.

        Raises:
            FileNotFoundError: The adapter plugin DLL, one of its runtime dependency DLLs, the
                published Host executable, or a supplied console-admin file is missing. Every
                source is validated before any existing `package_dir` content is removed, so a
                valid previous package is never destroyed by a run that then fails.
        """
        adapter_plugin = adapter_build_dir / ADAPTER_PLUGIN_NAME
        host_executable = host_publish_dir / HOST_EXECUTABLE_NAME
        if not adapter_plugin.is_file():
            raise FileNotFoundError(f"Adapter plugin not found: {adapter_plugin}")
        if not host_executable.is_file():
            raise FileNotFoundError(
                f"Published Host executable not found: {host_executable}"
            )
        runtime_dll_sources = []
        for dll_name in ADAPTER_RUNTIME_DLL_NAMES:
            source = adapter_build_dir / dll_name
            if not source.is_file():
                raise FileNotFoundError(
                    f"Adapter runtime dependency not found: {source}"
                )
            runtime_dll_sources.append(source)
        if console_admin_pex is not None and not console_admin_pex.is_file():
            raise FileNotFoundError(f"Console-admin PEX not found: {console_admin_pex}")
        if console_admin_yaml is not None and not console_admin_yaml.is_file():
            raise FileNotFoundError(
                f"Console-admin YAML not found: {console_admin_yaml}"
            )

        # A stale package_dir from a previous run could otherwise leave behind a file this run
        # never wrote -- for example a console-admin file omitted this time -- so every run starts
        # from a clean directory rather than accreting on top of whatever is already there. Every
        # source above is validated first, so this never destroys a valid previous package only to
        # fail partway through reassembling it.
        if package_dir.exists():
            shutil.rmtree(package_dir)

        plugins_dir = package_dir / "Data" / "SKSE" / "Plugins"
        plugins_dir.mkdir(parents=True, exist_ok=True)
        shutil.copy2(adapter_plugin, plugins_dir / ADAPTER_PLUGIN_NAME)
        for source in runtime_dll_sources:
            shutil.copy2(source, plugins_dir / source.name)

        host_dir = plugins_dir / HOST_EXECUTABLE_RELATIVE_DIR
        host_dir.mkdir(parents=True, exist_ok=True)
        shutil.copy2(host_executable, host_dir / HOST_EXECUTABLE_NAME)

        if console_admin_pex is not None:
            scripts_dir = package_dir / "Data" / "Scripts"
            scripts_dir.mkdir(parents=True, exist_ok=True)
            shutil.copy2(console_admin_pex, scripts_dir / console_admin_pex.name)
        if console_admin_yaml is not None:
            custom_console_dir = package_dir / "Data" / "SKSE" / "CustomConsole"
            custom_console_dir.mkdir(parents=True, exist_ok=True)
            shutil.copy2(
                console_admin_yaml, custom_console_dir / console_admin_yaml.name
            )

    def zip_package(
        self, package_dir: Path, output_zip_path_without_extension: Path
    ) -> Path:
        """Zips `package_dir`'s contents and returns the resulting archive's path.

        Args:
            package_dir: The assembled `Data/`-rooted package directory.
            output_zip_path_without_extension: The desired archive path, without a `.zip` suffix.

        Returns:
            The path to the written `.zip` archive.
        """
        archive_path = shutil.make_archive(
            str(output_zip_path_without_extension), "zip", root_dir=package_dir
        )
        return Path(archive_path)
