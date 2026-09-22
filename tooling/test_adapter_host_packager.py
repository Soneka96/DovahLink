"""Tests for adapter_host_packager.py."""

from __future__ import annotations

import tempfile
import unittest
import zipfile
from pathlib import Path
from unittest import mock

from adapter_host_packager import (
    ADAPTER_PLUGIN_NAME,
    HOST_EXECUTABLE_NAME,
    PUBLISH_ARGS,
    AdapterHostPackager,
)
from test_adapter_import_validator import _build_pe


class FakeProcessRunner:
    """Records every command it was asked to run, without executing anything."""

    def __init__(self) -> None:
        """Starts with no recorded invocations."""
        self.invocations: list[list[str]] = []

    def run(self, args: list[str]) -> None:
        """Records `args` instead of running them."""
        self.invocations.append(args)


def _write_file(path: Path, content: str = "") -> None:
    """Creates `path`'s parent directories and writes `content` into it."""
    path.parent.mkdir(parents=True, exist_ok=True)
    path.write_text(content, encoding="utf-8")


class PublishHostTests(unittest.TestCase):
    """Tests for AdapterHostPackager.publish_host."""

    def test_publish_host_invokes_dotnet_publish_with_the_self_contained_flags(
        self,
    ) -> None:
        """Verifies the production .NET publishing strategy's exact flags are passed, defaulting to a Release configuration."""
        with tempfile.TemporaryDirectory() as temp_dir:
            runner = FakeProcessRunner()
            packager = AdapterHostPackager(runner)
            host_project = Path(temp_dir) / "DovahLink.Host.csproj"
            publish_dir = Path(temp_dir) / "publish"

            packager.publish_host(host_project, publish_dir)

            self.assertEqual(len(runner.invocations), 1)
            invocation = runner.invocations[0]
            self.assertEqual(invocation[0], "dotnet")
            self.assertEqual(invocation[1], "publish")
            self.assertEqual(invocation[2], str(host_project))
            for arg in PUBLISH_ARGS:
                self.assertIn(arg, invocation)
            self.assertIn("--configuration", invocation)
            self.assertEqual(
                invocation[invocation.index("--configuration") + 1], "Release"
            )
            self.assertIn("--output", invocation)
            self.assertEqual(
                invocation[invocation.index("--output") + 1], str(publish_dir)
            )

    def test_publish_host_passes_a_custom_configuration(self) -> None:
        """Verifies a non-default configuration (for example a Debug build profile) is forwarded to dotnet publish."""
        with tempfile.TemporaryDirectory() as temp_dir:
            runner = FakeProcessRunner()
            packager = AdapterHostPackager(runner)
            host_project = Path(temp_dir) / "DovahLink.Host.csproj"
            publish_dir = Path(temp_dir) / "publish"

            packager.publish_host(host_project, publish_dir, configuration="Debug")

            invocation = runner.invocations[0]
            self.assertEqual(
                invocation[invocation.index("--configuration") + 1], "Debug"
            )

    def test_publish_host_creates_the_output_directory(self) -> None:
        """Verifies the publish output directory is created before publishing."""
        with tempfile.TemporaryDirectory() as temp_dir:
            packager = AdapterHostPackager(FakeProcessRunner())
            publish_dir = Path(temp_dir) / "nested" / "publish"

            packager.publish_host(Path(temp_dir) / "DovahLink.Host.csproj", publish_dir)

            self.assertTrue(publish_dir.is_dir())


class AssemblePackageTests(unittest.TestCase):
    """Tests for AdapterHostPackager.assemble_package."""

    def _build_valid_inputs(self, temp_dir: Path) -> tuple[Path, Path]:
        """Creates a valid adapter build directory and host publish directory under `temp_dir`."""
        adapter_build_dir = temp_dir / "adapter_build"
        (adapter_build_dir / ADAPTER_PLUGIN_NAME).parent.mkdir(
            parents=True, exist_ok=True
        )
        (adapter_build_dir / ADAPTER_PLUGIN_NAME).write_bytes(
            _build_pe(["kernel32.dll"])
        )

        host_publish_dir = temp_dir / "host_publish"
        _write_file(host_publish_dir / HOST_EXECUTABLE_NAME, "host")

        return adapter_build_dir, host_publish_dir

    def test_assemble_package_produces_the_expected_vortex_layout(self) -> None:
        """Verifies the assembled Data/SKSE/Plugins/... layout, including the Host subdirectory."""
        with tempfile.TemporaryDirectory() as temp_dir_str:
            temp_dir = Path(temp_dir_str)
            adapter_build_dir, host_publish_dir = self._build_valid_inputs(temp_dir)
            package_dir = temp_dir / "package"
            packager = AdapterHostPackager(FakeProcessRunner())

            packager.assemble_package(
                adapter_build_dir=adapter_build_dir,
                host_publish_dir=host_publish_dir,
                package_dir=package_dir,
            )

            plugins_dir = package_dir / "Data" / "SKSE" / "Plugins"
            self.assertTrue((plugins_dir / ADAPTER_PLUGIN_NAME).is_file())
            self.assertTrue(
                (plugins_dir / "DovahLink.Host" / HOST_EXECUTABLE_NAME).is_file()
            )

    def test_assemble_package_rejects_forbidden_import_before_removing_package(
        self,
    ) -> None:
        """Rejects a forbidden Adapter import while preserving the existing package."""
        with tempfile.TemporaryDirectory() as temp_dir_str:
            temp_dir = Path(temp_dir_str)
            adapter_build_dir, host_publish_dir = self._build_valid_inputs(temp_dir)
            (adapter_build_dir / ADAPTER_PLUGIN_NAME).write_bytes(
                _build_pe(["fmt.dll"])
            )
            package_dir = temp_dir / "package"
            package_dir.mkdir()
            sentinel = package_dir / "must-survive.txt"
            sentinel.write_text("previous package", encoding="utf-8")
            packager = AdapterHostPackager(FakeProcessRunner())

            with self.assertRaisesRegex(ValueError, "fmt.dll"):
                packager.assemble_package(
                    adapter_build_dir=adapter_build_dir,
                    host_publish_dir=host_publish_dir,
                    package_dir=package_dir,
                )

            self.assertEqual(sentinel.read_text(encoding="utf-8"), "previous package")

    def test_assemble_package_rejects_malformed_adapter_before_removing_package(
        self,
    ) -> None:
        """Rejects a malformed Adapter while preserving the existing package."""
        with tempfile.TemporaryDirectory() as temp_dir_str:
            temp_dir = Path(temp_dir_str)
            adapter_build_dir, host_publish_dir = self._build_valid_inputs(temp_dir)
            (adapter_build_dir / ADAPTER_PLUGIN_NAME).write_bytes(b"not a PE")
            package_dir = temp_dir / "package"
            package_dir.mkdir()
            sentinel = package_dir / "must-survive.txt"
            sentinel.write_text("previous package", encoding="utf-8")
            packager = AdapterHostPackager(FakeProcessRunner())

            with self.assertRaisesRegex(ValueError, "import table"):
                packager.assemble_package(
                    adapter_build_dir=adapter_build_dir,
                    host_publish_dir=host_publish_dir,
                    package_dir=package_dir,
                )

            self.assertEqual(sentinel.read_text(encoding="utf-8"), "previous package")

    def test_assemble_package_propagates_adapter_read_errors_before_removing_package(
        self,
    ) -> None:
        """Propagates an unreadable Adapter error while preserving the existing package."""
        with tempfile.TemporaryDirectory() as temp_dir_str:
            temp_dir = Path(temp_dir_str)
            adapter_build_dir, host_publish_dir = self._build_valid_inputs(temp_dir)
            package_dir = temp_dir / "package"
            package_dir.mkdir()
            sentinel = package_dir / "must-survive.txt"
            sentinel.write_text("previous package", encoding="utf-8")
            packager = AdapterHostPackager(FakeProcessRunner())

            with (
                mock.patch(
                    "adapter_host_packager.find_forbidden_adapter_dependency",
                    side_effect=OSError("access denied"),
                ),
                self.assertRaisesRegex(OSError, "access denied"),
            ):
                packager.assemble_package(
                    adapter_build_dir=adapter_build_dir,
                    host_publish_dir=host_publish_dir,
                    package_dir=package_dir,
                )

            self.assertEqual(sentinel.read_text(encoding="utf-8"), "previous package")

    def test_assemble_package_excludes_stale_dependency_dlls(self) -> None:
        """Ignores old build DLLs and removes old packaged DLLs before archiving."""
        with tempfile.TemporaryDirectory() as temp_dir_str:
            temp_dir = Path(temp_dir_str)
            adapter_build_dir, host_publish_dir = self._build_valid_inputs(temp_dir)
            package_dir = temp_dir / "package"
            plugins_dir = package_dir / "Data" / "SKSE" / "Plugins"
            for name in ("fmt.dll", "spdlog.dll", "fmtd.dll", "spdlogd.dll"):
                _write_file(adapter_build_dir / name, "stale build")
                _write_file(plugins_dir / name, "stale package")
            packager = AdapterHostPackager(FakeProcessRunner())
            packager.assemble_package(
                adapter_build_dir=adapter_build_dir,
                host_publish_dir=host_publish_dir,
                package_dir=package_dir,
            )
            packager.validate_package(package_dir)
            archive_path = packager.zip_package(package_dir, temp_dir / "adapter")
            with zipfile.ZipFile(archive_path) as archive:
                files = {name for name in archive.namelist() if not name.endswith("/")}
            self.assertEqual(
                files,
                {
                    "Data/SKSE/Plugins/dovahlink_adapter_plugin.dll",
                    "Data/SKSE/Plugins/DovahLink.Host/DovahLink.Host.exe",
                },
            )

    def test_assemble_package_raises_when_the_adapter_plugin_is_missing(self) -> None:
        """Verifies a missing adapter plugin DLL fails clearly rather than assembling a partial package."""
        with tempfile.TemporaryDirectory() as temp_dir_str:
            temp_dir = Path(temp_dir_str)
            _adapter_build_dir, host_publish_dir = self._build_valid_inputs(temp_dir)
            adapter_build_dir = temp_dir / "empty_adapter_build"
            adapter_build_dir.mkdir()
            packager = AdapterHostPackager(FakeProcessRunner())

            with self.assertRaises(FileNotFoundError):
                packager.assemble_package(
                    adapter_build_dir=adapter_build_dir,
                    host_publish_dir=host_publish_dir,
                    package_dir=temp_dir / "package",
                )

    def test_assemble_package_raises_when_the_published_host_executable_is_missing(
        self,
    ) -> None:
        """Verifies a missing published Host executable fails clearly."""
        with tempfile.TemporaryDirectory() as temp_dir_str:
            temp_dir = Path(temp_dir_str)
            adapter_build_dir, _host_publish_dir = self._build_valid_inputs(temp_dir)
            host_publish_dir = temp_dir / "empty_host_publish"
            host_publish_dir.mkdir()
            packager = AdapterHostPackager(FakeProcessRunner())

            with self.assertRaises(FileNotFoundError):
                packager.assemble_package(
                    adapter_build_dir=adapter_build_dir,
                    host_publish_dir=host_publish_dir,
                    package_dir=temp_dir / "package",
                )

    def test_assemble_package_includes_console_admin_files_when_supplied(self) -> None:
        """Verifies the optional console-admin files land at their documented install paths."""
        with tempfile.TemporaryDirectory() as temp_dir_str:
            temp_dir = Path(temp_dir_str)
            adapter_build_dir, host_publish_dir = self._build_valid_inputs(temp_dir)
            pex_path = temp_dir / "DovahLinkAdmin.pex"
            yaml_path = temp_dir / "dovahlink.yaml"
            _write_file(pex_path, "pex")
            _write_file(yaml_path, "yaml")
            package_dir = temp_dir / "package"
            packager = AdapterHostPackager(FakeProcessRunner())

            packager.assemble_package(
                adapter_build_dir=adapter_build_dir,
                host_publish_dir=host_publish_dir,
                package_dir=package_dir,
                console_admin_pex=pex_path,
                console_admin_yaml=yaml_path,
            )

            self.assertTrue(
                (package_dir / "Data" / "Scripts" / "DovahLinkAdmin.pex").is_file()
            )
            self.assertTrue(
                (
                    package_dir / "Data" / "SKSE" / "CustomConsole" / "dovahlink.yaml"
                ).is_file()
            )

    def test_assemble_package_includes_only_the_pex_when_only_the_pex_is_supplied(
        self,
    ) -> None:
        """Verifies supplying only console_admin_pex omits the CustomConsole directory entirely."""
        with tempfile.TemporaryDirectory() as temp_dir_str:
            temp_dir = Path(temp_dir_str)
            adapter_build_dir, host_publish_dir = self._build_valid_inputs(temp_dir)
            pex_path = temp_dir / "DovahLinkAdmin.pex"
            _write_file(pex_path, "pex")
            package_dir = temp_dir / "package"
            packager = AdapterHostPackager(FakeProcessRunner())

            packager.assemble_package(
                adapter_build_dir=adapter_build_dir,
                host_publish_dir=host_publish_dir,
                package_dir=package_dir,
                console_admin_pex=pex_path,
            )

            self.assertTrue(
                (package_dir / "Data" / "Scripts" / "DovahLinkAdmin.pex").is_file()
            )
            self.assertFalse((package_dir / "Data" / "SKSE" / "CustomConsole").exists())

    def test_assemble_package_includes_only_the_yaml_when_only_the_yaml_is_supplied(
        self,
    ) -> None:
        """Verifies supplying only console_admin_yaml omits the Scripts directory entirely."""
        with tempfile.TemporaryDirectory() as temp_dir_str:
            temp_dir = Path(temp_dir_str)
            adapter_build_dir, host_publish_dir = self._build_valid_inputs(temp_dir)
            yaml_path = temp_dir / "dovahlink.yaml"
            _write_file(yaml_path, "yaml")
            package_dir = temp_dir / "package"
            packager = AdapterHostPackager(FakeProcessRunner())

            packager.assemble_package(
                adapter_build_dir=adapter_build_dir,
                host_publish_dir=host_publish_dir,
                package_dir=package_dir,
                console_admin_yaml=yaml_path,
            )

            self.assertTrue(
                (
                    package_dir / "Data" / "SKSE" / "CustomConsole" / "dovahlink.yaml"
                ).is_file()
            )
            self.assertFalse((package_dir / "Data" / "Scripts").exists())

    def test_assemble_package_omits_console_admin_files_when_not_supplied(self) -> None:
        """Verifies the console-admin paths are absent entirely when not supplied, not merely empty."""
        with tempfile.TemporaryDirectory() as temp_dir_str:
            temp_dir = Path(temp_dir_str)
            adapter_build_dir, host_publish_dir = self._build_valid_inputs(temp_dir)
            package_dir = temp_dir / "package"
            packager = AdapterHostPackager(FakeProcessRunner())

            packager.assemble_package(
                adapter_build_dir=adapter_build_dir,
                host_publish_dir=host_publish_dir,
                package_dir=package_dir,
            )

            self.assertFalse((package_dir / "Data" / "Scripts").exists())
            self.assertFalse((package_dir / "Data" / "SKSE" / "CustomConsole").exists())

    def test_assemble_package_does_not_leave_stale_files_from_a_previous_run(
        self,
    ) -> None:
        """Verifies a file from a prior run (for example an omitted console-admin file) does not survive."""
        with tempfile.TemporaryDirectory() as temp_dir_str:
            temp_dir = Path(temp_dir_str)
            adapter_build_dir, host_publish_dir = self._build_valid_inputs(temp_dir)
            pex_path = temp_dir / "DovahLinkAdmin.pex"
            _write_file(pex_path, "pex")
            package_dir = temp_dir / "package"
            packager = AdapterHostPackager(FakeProcessRunner())
            packager.assemble_package(
                adapter_build_dir=adapter_build_dir,
                host_publish_dir=host_publish_dir,
                package_dir=package_dir,
                console_admin_pex=pex_path,
            )
            self.assertTrue(
                (package_dir / "Data" / "Scripts" / "DovahLinkAdmin.pex").is_file()
            )

            packager.assemble_package(
                adapter_build_dir=adapter_build_dir,
                host_publish_dir=host_publish_dir,
                package_dir=package_dir,
            )

            self.assertFalse((package_dir / "Data" / "Scripts").exists())

    def test_assemble_package_raises_when_a_supplied_console_admin_pex_is_missing(
        self,
    ) -> None:
        """Verifies a supplied but missing console-admin PEX fails clearly."""
        with tempfile.TemporaryDirectory() as temp_dir_str:
            temp_dir = Path(temp_dir_str)
            adapter_build_dir, host_publish_dir = self._build_valid_inputs(temp_dir)
            packager = AdapterHostPackager(FakeProcessRunner())

            with self.assertRaises(FileNotFoundError):
                packager.assemble_package(
                    adapter_build_dir=adapter_build_dir,
                    host_publish_dir=host_publish_dir,
                    package_dir=temp_dir / "package",
                    console_admin_pex=temp_dir / "missing.pex",
                )

    def test_assemble_package_raises_when_a_supplied_console_admin_yaml_is_missing(
        self,
    ) -> None:
        """Verifies a supplied but missing console-admin YAML fails clearly."""
        with tempfile.TemporaryDirectory() as temp_dir_str:
            temp_dir = Path(temp_dir_str)
            adapter_build_dir, host_publish_dir = self._build_valid_inputs(temp_dir)
            packager = AdapterHostPackager(FakeProcessRunner())

            with self.assertRaises(FileNotFoundError):
                packager.assemble_package(
                    adapter_build_dir=adapter_build_dir,
                    host_publish_dir=host_publish_dir,
                    package_dir=temp_dir / "package",
                    console_admin_yaml=temp_dir / "missing.yaml",
                )

    def test_assemble_package_leaves_an_existing_package_untouched_when_the_adapter_plugin_is_missing(
        self,
    ) -> None:
        """Verifies a valid existing package survives a failed re-assembly caused by the first-checked source."""
        with tempfile.TemporaryDirectory() as temp_dir_str:
            temp_dir = Path(temp_dir_str)
            adapter_build_dir, host_publish_dir = self._build_valid_inputs(temp_dir)
            package_dir = temp_dir / "package"
            packager = AdapterHostPackager(FakeProcessRunner())
            packager.assemble_package(
                adapter_build_dir=adapter_build_dir,
                host_publish_dir=host_publish_dir,
                package_dir=package_dir,
            )
            plugins_dir = package_dir / "Data" / "SKSE" / "Plugins"
            self.assertTrue((plugins_dir / ADAPTER_PLUGIN_NAME).is_file())

            (adapter_build_dir / ADAPTER_PLUGIN_NAME).unlink()
            with self.assertRaises(FileNotFoundError):
                packager.assemble_package(
                    adapter_build_dir=adapter_build_dir,
                    host_publish_dir=host_publish_dir,
                    package_dir=package_dir,
                )

            self.assertTrue((plugins_dir / ADAPTER_PLUGIN_NAME).is_file())

    def test_assemble_package_leaves_an_existing_package_untouched_when_a_console_admin_file_is_missing(
        self,
    ) -> None:
        """Verifies a valid existing package survives a failed re-assembly caused by a missing optional file."""
        with tempfile.TemporaryDirectory() as temp_dir_str:
            temp_dir = Path(temp_dir_str)
            adapter_build_dir, host_publish_dir = self._build_valid_inputs(temp_dir)
            package_dir = temp_dir / "package"
            packager = AdapterHostPackager(FakeProcessRunner())
            packager.assemble_package(
                adapter_build_dir=adapter_build_dir,
                host_publish_dir=host_publish_dir,
                package_dir=package_dir,
            )
            plugins_dir = package_dir / "Data" / "SKSE" / "Plugins"

            with self.assertRaises(FileNotFoundError):
                packager.assemble_package(
                    adapter_build_dir=adapter_build_dir,
                    host_publish_dir=host_publish_dir,
                    package_dir=package_dir,
                    console_admin_pex=temp_dir / "missing.pex",
                )

            self.assertTrue((plugins_dir / ADAPTER_PLUGIN_NAME).is_file())


class ZipPackageTests(unittest.TestCase):
    """Tests for AdapterHostPackager.zip_package."""

    def test_zip_package_archives_every_assembled_file(self) -> None:
        """Verifies the zip's entries match the assembled folder's files exactly."""
        with tempfile.TemporaryDirectory() as temp_dir_str:
            temp_dir = Path(temp_dir_str)
            package_dir = temp_dir / "package"
            _write_file(
                package_dir / "Data" / "SKSE" / "Plugins" / ADAPTER_PLUGIN_NAME,
                "plugin",
            )
            _write_file(
                package_dir
                / "Data"
                / "SKSE"
                / "Plugins"
                / "DovahLink.Host"
                / HOST_EXECUTABLE_NAME,
                "host",
            )
            packager = AdapterHostPackager(FakeProcessRunner())

            archive_path = packager.zip_package(
                package_dir, temp_dir / "DovahLink-Adapter-0.0.0"
            )

            self.assertEqual(archive_path.suffix, ".zip")
            self.assertTrue(archive_path.is_file())
            with zipfile.ZipFile(archive_path) as archive:
                names = set(archive.namelist())
            self.assertIn("Data/SKSE/Plugins/" + ADAPTER_PLUGIN_NAME, names)
            self.assertIn(
                "Data/SKSE/Plugins/DovahLink.Host/" + HOST_EXECUTABLE_NAME, names
            )


class ValidatePackageTests(unittest.TestCase):
    """Tests for AdapterHostPackager.validate_package."""

    def _build_valid_inputs(self, temp_dir: Path) -> tuple[Path, Path]:
        """Creates a valid adapter build directory and host publish directory under `temp_dir`."""
        adapter_build_dir = temp_dir / "adapter_build"
        (adapter_build_dir / ADAPTER_PLUGIN_NAME).parent.mkdir(
            parents=True, exist_ok=True
        )
        (adapter_build_dir / ADAPTER_PLUGIN_NAME).write_bytes(
            _build_pe(["kernel32.dll"])
        )

        host_publish_dir = temp_dir / "host_publish"
        _write_file(host_publish_dir / HOST_EXECUTABLE_NAME, "host")

        return adapter_build_dir, host_publish_dir

    def test_validate_package_succeeds_for_a_complete_package_without_console_admin_files(
        self,
    ) -> None:
        """Verifies a package assembled without console-admin files validates cleanly."""
        with tempfile.TemporaryDirectory() as temp_dir_str:
            temp_dir = Path(temp_dir_str)
            adapter_build_dir, host_publish_dir = self._build_valid_inputs(temp_dir)
            package_dir = temp_dir / "package"
            packager = AdapterHostPackager(FakeProcessRunner())
            packager.assemble_package(
                adapter_build_dir=adapter_build_dir,
                host_publish_dir=host_publish_dir,
                package_dir=package_dir,
            )

            packager.validate_package(package_dir)

    def test_validate_package_succeeds_for_a_complete_package_with_console_admin_files(
        self,
    ) -> None:
        """Verifies a package assembled with console-admin files validates cleanly."""
        with tempfile.TemporaryDirectory() as temp_dir_str:
            temp_dir = Path(temp_dir_str)
            adapter_build_dir, host_publish_dir = self._build_valid_inputs(temp_dir)
            pex_path = temp_dir / "DovahLinkAdmin.pex"
            yaml_path = temp_dir / "dovahlink.yaml"
            _write_file(pex_path, "pex")
            _write_file(yaml_path, "yaml")
            package_dir = temp_dir / "package"
            packager = AdapterHostPackager(FakeProcessRunner())
            packager.assemble_package(
                adapter_build_dir=adapter_build_dir,
                host_publish_dir=host_publish_dir,
                package_dir=package_dir,
                console_admin_pex=pex_path,
                console_admin_yaml=yaml_path,
            )

            packager.validate_package(
                package_dir, console_admin_pex=pex_path, console_admin_yaml=yaml_path
            )

    def test_validate_package_raises_when_the_adapter_plugin_is_missing(self) -> None:
        """Verifies a package missing its assembled adapter plugin fails validation."""
        with tempfile.TemporaryDirectory() as temp_dir_str:
            temp_dir = Path(temp_dir_str)
            adapter_build_dir, host_publish_dir = self._build_valid_inputs(temp_dir)
            package_dir = temp_dir / "package"
            packager = AdapterHostPackager(FakeProcessRunner())
            packager.assemble_package(
                adapter_build_dir=adapter_build_dir,
                host_publish_dir=host_publish_dir,
                package_dir=package_dir,
            )
            (package_dir / "Data" / "SKSE" / "Plugins" / ADAPTER_PLUGIN_NAME).unlink()

            with self.assertRaises(FileNotFoundError):
                packager.validate_package(package_dir)

    def test_validate_package_raises_when_the_host_executable_is_missing(self) -> None:
        """Verifies a package missing its assembled Host executable fails validation."""
        with tempfile.TemporaryDirectory() as temp_dir_str:
            temp_dir = Path(temp_dir_str)
            adapter_build_dir, host_publish_dir = self._build_valid_inputs(temp_dir)
            package_dir = temp_dir / "package"
            packager = AdapterHostPackager(FakeProcessRunner())
            packager.assemble_package(
                adapter_build_dir=adapter_build_dir,
                host_publish_dir=host_publish_dir,
                package_dir=package_dir,
            )
            (
                package_dir
                / "Data"
                / "SKSE"
                / "Plugins"
                / "DovahLink.Host"
                / HOST_EXECUTABLE_NAME
            ).unlink()

            with self.assertRaises(FileNotFoundError):
                packager.validate_package(package_dir)

    def test_validate_package_raises_when_a_supplied_console_admin_pex_was_not_assembled(
        self,
    ) -> None:
        """Verifies a package expected to include the console-admin PEX fails when it is absent."""
        with tempfile.TemporaryDirectory() as temp_dir_str:
            temp_dir = Path(temp_dir_str)
            adapter_build_dir, host_publish_dir = self._build_valid_inputs(temp_dir)
            package_dir = temp_dir / "package"
            packager = AdapterHostPackager(FakeProcessRunner())
            packager.assemble_package(
                adapter_build_dir=adapter_build_dir,
                host_publish_dir=host_publish_dir,
                package_dir=package_dir,
            )

            with self.assertRaises(FileNotFoundError):
                packager.validate_package(
                    package_dir, console_admin_pex=temp_dir / "DovahLinkAdmin.pex"
                )

    def test_validate_package_raises_when_a_supplied_console_admin_yaml_was_not_assembled(
        self,
    ) -> None:
        """Verifies a package expected to include the console-admin YAML fails when it is absent."""
        with tempfile.TemporaryDirectory() as temp_dir_str:
            temp_dir = Path(temp_dir_str)
            adapter_build_dir, host_publish_dir = self._build_valid_inputs(temp_dir)
            package_dir = temp_dir / "package"
            packager = AdapterHostPackager(FakeProcessRunner())
            packager.assemble_package(
                adapter_build_dir=adapter_build_dir,
                host_publish_dir=host_publish_dir,
                package_dir=package_dir,
            )

            with self.assertRaises(FileNotFoundError):
                packager.validate_package(
                    package_dir, console_admin_yaml=temp_dir / "dovahlink.yaml"
                )

    def test_validate_package_succeeds_when_only_the_pex_was_supplied(self) -> None:
        """Verifies validation does not require a YAML that was never expected."""
        with tempfile.TemporaryDirectory() as temp_dir_str:
            temp_dir = Path(temp_dir_str)
            adapter_build_dir, host_publish_dir = self._build_valid_inputs(temp_dir)
            pex_path = temp_dir / "DovahLinkAdmin.pex"
            _write_file(pex_path, "pex")
            package_dir = temp_dir / "package"
            packager = AdapterHostPackager(FakeProcessRunner())
            packager.assemble_package(
                adapter_build_dir=adapter_build_dir,
                host_publish_dir=host_publish_dir,
                package_dir=package_dir,
                console_admin_pex=pex_path,
            )

            packager.validate_package(package_dir, console_admin_pex=pex_path)

    def test_validate_package_succeeds_when_only_the_yaml_was_supplied(self) -> None:
        """Verifies validation does not require a PEX that was never expected."""
        with tempfile.TemporaryDirectory() as temp_dir_str:
            temp_dir = Path(temp_dir_str)
            adapter_build_dir, host_publish_dir = self._build_valid_inputs(temp_dir)
            yaml_path = temp_dir / "dovahlink.yaml"
            _write_file(yaml_path, "yaml")
            package_dir = temp_dir / "package"
            packager = AdapterHostPackager(FakeProcessRunner())
            packager.assemble_package(
                adapter_build_dir=adapter_build_dir,
                host_publish_dir=host_publish_dir,
                package_dir=package_dir,
                console_admin_yaml=yaml_path,
            )

            packager.validate_package(package_dir, console_admin_yaml=yaml_path)


if __name__ == "__main__":
    unittest.main()
