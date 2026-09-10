"""Tests for adapter_host_packager.py."""

from __future__ import annotations

import tempfile
import unittest
import zipfile
from pathlib import Path

from adapter_host_packager import (
    ADAPTER_PLUGIN_NAME,
    ADAPTER_RUNTIME_DLL_NAMES,
    HOST_EXECUTABLE_NAME,
    PUBLISH_ARGS,
    AdapterHostPackager,
)
from build_output_ownership import MARKER_FILE_NAME, BuildOutputOwnershipGuard


class FakeProcessRunner:
    """Records every command it was asked to run, without executing anything."""

    def __init__(self) -> None:
        """Starts with no recorded invocations."""
        self.invocations: list[list[str]] = []

    def run(self, args: list[str]) -> None:
        """Records `args` instead of running them."""
        self.invocations.append(args)


class FakeBuildOutputOwnershipGuard:
    """Treats every output root as already owned, doing nothing.

    The real ownership behavior itself is covered by `test_build_output_ownership.py` and this
    file's own ownership-specific tests, which construct a real `BuildOutputOwnershipGuard`
    explicitly; every other test in this file uses this fake so it never refuses a `package_dir`
    pre-populated with unrelated "stale file from a previous run" content that is not this
    feature's concern.
    """

    def ensure_owned(self, output_root: Path, repository_root: Path) -> None:
        """Does nothing."""


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
            packager = AdapterHostPackager(runner, FakeBuildOutputOwnershipGuard())
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
            packager = AdapterHostPackager(runner, FakeBuildOutputOwnershipGuard())
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
            packager = AdapterHostPackager(
                FakeProcessRunner(), FakeBuildOutputOwnershipGuard()
            )
            publish_dir = Path(temp_dir) / "nested" / "publish"

            packager.publish_host(Path(temp_dir) / "DovahLink.Host.csproj", publish_dir)

            self.assertTrue(publish_dir.is_dir())


class AssemblePackageTests(unittest.TestCase):
    """Tests for AdapterHostPackager.assemble_package."""

    def _build_valid_inputs(self, temp_dir: Path) -> tuple[Path, Path]:
        """Creates a valid adapter build directory and host publish directory under `temp_dir`."""
        adapter_build_dir = temp_dir / "adapter_build"
        _write_file(adapter_build_dir / ADAPTER_PLUGIN_NAME, "plugin")
        for dll_name in ADAPTER_RUNTIME_DLL_NAMES:
            _write_file(adapter_build_dir / dll_name, "dll")

        host_publish_dir = temp_dir / "host_publish"
        _write_file(host_publish_dir / HOST_EXECUTABLE_NAME, "host")

        return adapter_build_dir, host_publish_dir

    def test_assemble_package_produces_the_expected_vortex_layout(self) -> None:
        """Verifies the assembled Data/SKSE/Plugins/... layout, including the Host subdirectory."""
        with tempfile.TemporaryDirectory() as temp_dir_str:
            temp_dir = Path(temp_dir_str)
            adapter_build_dir, host_publish_dir = self._build_valid_inputs(temp_dir)
            package_dir = temp_dir / "package"
            packager = AdapterHostPackager(
                FakeProcessRunner(), FakeBuildOutputOwnershipGuard()
            )

            packager.assemble_package(
                adapter_build_dir=adapter_build_dir,
                host_publish_dir=host_publish_dir,
                package_dir=package_dir,
                repository_root=temp_dir / "repo",
            )

            plugins_dir = package_dir / "Data" / "SKSE" / "Plugins"
            self.assertTrue((plugins_dir / ADAPTER_PLUGIN_NAME).is_file())
            for dll_name in ADAPTER_RUNTIME_DLL_NAMES:
                self.assertTrue((plugins_dir / dll_name).is_file())
            self.assertTrue(
                (plugins_dir / "DovahLink.Host" / HOST_EXECUTABLE_NAME).is_file()
            )

    def test_assemble_package_refuses_an_unrelated_non_empty_package_dir(self) -> None:
        """
        Refuses to assemble, before the existing content is ever touched, when `package_dir`
        resolves to an arbitrary existing folder that already has unrelated content and no
        DovahLink Builder ownership mark -- proving a NORMAL (not just a clean-flagged) run can
        never delete an unrelated folder a caller happened to point it at. Uses a real
        `BuildOutputOwnershipGuard`, unlike this file's other tests, since this is what actually
        enforces the refusal.
        """
        with tempfile.TemporaryDirectory() as temp_dir_str:
            temp_dir = Path(temp_dir_str)
            adapter_build_dir, host_publish_dir = self._build_valid_inputs(temp_dir)
            package_dir = temp_dir / "unrelated-user-folder"
            unrelated_file = package_dir / "some-real-file.txt"
            _write_file(unrelated_file, "the user's own real data, not DovahLink's")
            packager = AdapterHostPackager(
                FakeProcessRunner(), BuildOutputOwnershipGuard()
            )

            with self.assertRaises(RuntimeError):
                packager.assemble_package(
                    adapter_build_dir=adapter_build_dir,
                    host_publish_dir=host_publish_dir,
                    package_dir=package_dir,
                    repository_root=temp_dir / "repo",
                )

            self.assertFalse((package_dir / MARKER_FILE_NAME).is_file())
            self.assertEqual(
                "the user's own real data, not DovahLink's",
                unrelated_file.read_text(encoding="utf-8"),
            )

    def test_assemble_package_succeeds_for_an_already_marked_package_dir(self) -> None:
        """
        Assembles normally into a `package_dir` already marked as DovahLink Builder-owned from a
        previous run, even though it already has real content from that previous run -- proving a
        valid Builder-owned custom output can still be reassembled.
        """
        with tempfile.TemporaryDirectory() as temp_dir_str:
            temp_dir = Path(temp_dir_str)
            adapter_build_dir, host_publish_dir = self._build_valid_inputs(temp_dir)
            package_dir = temp_dir / "previously-owned-output"
            _write_file(package_dir / MARKER_FILE_NAME, "owned")
            _write_file(package_dir / "Data" / "stale-from-last-run.txt", "stale")
            packager = AdapterHostPackager(
                FakeProcessRunner(), BuildOutputOwnershipGuard()
            )

            packager.assemble_package(
                adapter_build_dir=adapter_build_dir,
                host_publish_dir=host_publish_dir,
                package_dir=package_dir,
                repository_root=temp_dir / "repo",
            )

            plugins_dir = package_dir / "Data" / "SKSE" / "Plugins"
            self.assertTrue((plugins_dir / ADAPTER_PLUGIN_NAME).is_file())

    def test_assemble_package_keeps_a_freshly_adopted_package_dir_owned_across_repeated_runs(
        self,
    ) -> None:
        """
        Regression test: `package_dir` itself -- unlike the C# BuildOutputOwnershipGuard's own
        callers, which only ever clean subfolders under a stable marked root -- is entirely replaced
        by this method's own rmtree every run, which would otherwise destroy the ownership mark a
        first run had only just adopted, permanently locking a legitimate second run out of its own
        previous output. Proves three consecutive runs against the same nonexistent-at-first custom
        `package_dir` all succeed.
        """
        with tempfile.TemporaryDirectory() as temp_dir_str:
            temp_dir = Path(temp_dir_str)
            adapter_build_dir, host_publish_dir = self._build_valid_inputs(temp_dir)
            package_dir = temp_dir / "custom-out" / "package"
            repository_root = temp_dir / "repo"
            packager = AdapterHostPackager(
                FakeProcessRunner(), BuildOutputOwnershipGuard()
            )

            for _ in range(3):
                packager.assemble_package(
                    adapter_build_dir=adapter_build_dir,
                    host_publish_dir=host_publish_dir,
                    package_dir=package_dir,
                    repository_root=repository_root,
                )

            plugins_dir = package_dir / "Data" / "SKSE" / "Plugins"
            self.assertTrue((plugins_dir / ADAPTER_PLUGIN_NAME).is_file())
            self.assertTrue((package_dir / MARKER_FILE_NAME).is_file())

    def test_assemble_package_raises_when_the_adapter_plugin_is_missing(self) -> None:
        """Verifies a missing adapter plugin DLL fails clearly rather than assembling a partial package."""
        with tempfile.TemporaryDirectory() as temp_dir_str:
            temp_dir = Path(temp_dir_str)
            _adapter_build_dir, host_publish_dir = self._build_valid_inputs(temp_dir)
            adapter_build_dir = temp_dir / "empty_adapter_build"
            adapter_build_dir.mkdir()
            packager = AdapterHostPackager(
                FakeProcessRunner(), FakeBuildOutputOwnershipGuard()
            )

            with self.assertRaises(FileNotFoundError):
                packager.assemble_package(
                    adapter_build_dir=adapter_build_dir,
                    host_publish_dir=host_publish_dir,
                    package_dir=temp_dir / "package",
                    repository_root=temp_dir / "repo",
                )

    def test_assemble_package_raises_when_a_runtime_dll_is_missing(self) -> None:
        """Verifies a missing adapter runtime dependency DLL fails clearly."""
        with tempfile.TemporaryDirectory() as temp_dir_str:
            temp_dir = Path(temp_dir_str)
            adapter_build_dir, host_publish_dir = self._build_valid_inputs(temp_dir)
            (adapter_build_dir / ADAPTER_RUNTIME_DLL_NAMES[0]).unlink()
            packager = AdapterHostPackager(
                FakeProcessRunner(), FakeBuildOutputOwnershipGuard()
            )

            with self.assertRaises(FileNotFoundError):
                packager.assemble_package(
                    adapter_build_dir=adapter_build_dir,
                    host_publish_dir=host_publish_dir,
                    package_dir=temp_dir / "package",
                    repository_root=temp_dir / "repo",
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
            packager = AdapterHostPackager(
                FakeProcessRunner(), FakeBuildOutputOwnershipGuard()
            )

            with self.assertRaises(FileNotFoundError):
                packager.assemble_package(
                    adapter_build_dir=adapter_build_dir,
                    host_publish_dir=host_publish_dir,
                    package_dir=temp_dir / "package",
                    repository_root=temp_dir / "repo",
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
            packager = AdapterHostPackager(
                FakeProcessRunner(), FakeBuildOutputOwnershipGuard()
            )

            packager.assemble_package(
                adapter_build_dir=adapter_build_dir,
                host_publish_dir=host_publish_dir,
                package_dir=package_dir,
                repository_root=temp_dir / "repo",
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
            packager = AdapterHostPackager(
                FakeProcessRunner(), FakeBuildOutputOwnershipGuard()
            )

            packager.assemble_package(
                adapter_build_dir=adapter_build_dir,
                host_publish_dir=host_publish_dir,
                package_dir=package_dir,
                repository_root=temp_dir / "repo",
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
            packager = AdapterHostPackager(
                FakeProcessRunner(), FakeBuildOutputOwnershipGuard()
            )

            packager.assemble_package(
                adapter_build_dir=adapter_build_dir,
                host_publish_dir=host_publish_dir,
                package_dir=package_dir,
                repository_root=temp_dir / "repo",
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
            packager = AdapterHostPackager(
                FakeProcessRunner(), FakeBuildOutputOwnershipGuard()
            )

            packager.assemble_package(
                adapter_build_dir=adapter_build_dir,
                host_publish_dir=host_publish_dir,
                package_dir=package_dir,
                repository_root=temp_dir / "repo",
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
            packager = AdapterHostPackager(
                FakeProcessRunner(), FakeBuildOutputOwnershipGuard()
            )
            packager.assemble_package(
                adapter_build_dir=adapter_build_dir,
                host_publish_dir=host_publish_dir,
                package_dir=package_dir,
                repository_root=temp_dir / "repo",
                console_admin_pex=pex_path,
            )
            self.assertTrue(
                (package_dir / "Data" / "Scripts" / "DovahLinkAdmin.pex").is_file()
            )

            packager.assemble_package(
                adapter_build_dir=adapter_build_dir,
                host_publish_dir=host_publish_dir,
                package_dir=package_dir,
                repository_root=temp_dir / "repo",
            )

            self.assertFalse((package_dir / "Data" / "Scripts").exists())

    def test_assemble_package_raises_when_a_supplied_console_admin_pex_is_missing(
        self,
    ) -> None:
        """Verifies a supplied but missing console-admin PEX fails clearly."""
        with tempfile.TemporaryDirectory() as temp_dir_str:
            temp_dir = Path(temp_dir_str)
            adapter_build_dir, host_publish_dir = self._build_valid_inputs(temp_dir)
            packager = AdapterHostPackager(
                FakeProcessRunner(), FakeBuildOutputOwnershipGuard()
            )

            with self.assertRaises(FileNotFoundError):
                packager.assemble_package(
                    adapter_build_dir=adapter_build_dir,
                    host_publish_dir=host_publish_dir,
                    package_dir=temp_dir / "package",
                    repository_root=temp_dir / "repo",
                    console_admin_pex=temp_dir / "missing.pex",
                )

    def test_assemble_package_raises_when_a_supplied_console_admin_yaml_is_missing(
        self,
    ) -> None:
        """Verifies a supplied but missing console-admin YAML fails clearly."""
        with tempfile.TemporaryDirectory() as temp_dir_str:
            temp_dir = Path(temp_dir_str)
            adapter_build_dir, host_publish_dir = self._build_valid_inputs(temp_dir)
            packager = AdapterHostPackager(
                FakeProcessRunner(), FakeBuildOutputOwnershipGuard()
            )

            with self.assertRaises(FileNotFoundError):
                packager.assemble_package(
                    adapter_build_dir=adapter_build_dir,
                    host_publish_dir=host_publish_dir,
                    package_dir=temp_dir / "package",
                    repository_root=temp_dir / "repo",
                    console_admin_yaml=temp_dir / "missing.yaml",
                )

    def test_assemble_package_leaves_an_existing_package_untouched_when_a_runtime_dll_is_missing(
        self,
    ) -> None:
        """Verifies a valid existing package survives a failed re-assembly rather than being deleted first."""
        with tempfile.TemporaryDirectory() as temp_dir_str:
            temp_dir = Path(temp_dir_str)
            adapter_build_dir, host_publish_dir = self._build_valid_inputs(temp_dir)
            package_dir = temp_dir / "package"
            packager = AdapterHostPackager(
                FakeProcessRunner(), FakeBuildOutputOwnershipGuard()
            )
            packager.assemble_package(
                adapter_build_dir=adapter_build_dir,
                host_publish_dir=host_publish_dir,
                package_dir=package_dir,
                repository_root=temp_dir / "repo",
            )
            plugins_dir = package_dir / "Data" / "SKSE" / "Plugins"
            self.assertTrue((plugins_dir / ADAPTER_PLUGIN_NAME).is_file())

            (adapter_build_dir / ADAPTER_RUNTIME_DLL_NAMES[0]).unlink()
            with self.assertRaises(FileNotFoundError):
                packager.assemble_package(
                    adapter_build_dir=adapter_build_dir,
                    host_publish_dir=host_publish_dir,
                    package_dir=package_dir,
                    repository_root=temp_dir / "repo",
                )

            self.assertTrue((plugins_dir / ADAPTER_PLUGIN_NAME).is_file())
            for dll_name in ADAPTER_RUNTIME_DLL_NAMES:
                self.assertTrue((plugins_dir / dll_name).is_file())
            self.assertTrue(
                (plugins_dir / "DovahLink.Host" / HOST_EXECUTABLE_NAME).is_file()
            )

    def test_assemble_package_leaves_an_existing_package_untouched_when_the_adapter_plugin_is_missing(
        self,
    ) -> None:
        """Verifies a valid existing package survives a failed re-assembly caused by the first-checked source."""
        with tempfile.TemporaryDirectory() as temp_dir_str:
            temp_dir = Path(temp_dir_str)
            adapter_build_dir, host_publish_dir = self._build_valid_inputs(temp_dir)
            package_dir = temp_dir / "package"
            packager = AdapterHostPackager(
                FakeProcessRunner(), FakeBuildOutputOwnershipGuard()
            )
            packager.assemble_package(
                adapter_build_dir=adapter_build_dir,
                host_publish_dir=host_publish_dir,
                package_dir=package_dir,
                repository_root=temp_dir / "repo",
            )
            plugins_dir = package_dir / "Data" / "SKSE" / "Plugins"
            self.assertTrue((plugins_dir / ADAPTER_PLUGIN_NAME).is_file())

            (adapter_build_dir / ADAPTER_PLUGIN_NAME).unlink()
            with self.assertRaises(FileNotFoundError):
                packager.assemble_package(
                    adapter_build_dir=adapter_build_dir,
                    host_publish_dir=host_publish_dir,
                    package_dir=package_dir,
                    repository_root=temp_dir / "repo",
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
            packager = AdapterHostPackager(
                FakeProcessRunner(), FakeBuildOutputOwnershipGuard()
            )
            packager.assemble_package(
                adapter_build_dir=adapter_build_dir,
                host_publish_dir=host_publish_dir,
                package_dir=package_dir,
                repository_root=temp_dir / "repo",
            )
            plugins_dir = package_dir / "Data" / "SKSE" / "Plugins"

            with self.assertRaises(FileNotFoundError):
                packager.assemble_package(
                    adapter_build_dir=adapter_build_dir,
                    host_publish_dir=host_publish_dir,
                    package_dir=package_dir,
                    repository_root=temp_dir / "repo",
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
            packager = AdapterHostPackager(
                FakeProcessRunner(), FakeBuildOutputOwnershipGuard()
            )

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
        _write_file(adapter_build_dir / ADAPTER_PLUGIN_NAME, "plugin")
        for dll_name in ADAPTER_RUNTIME_DLL_NAMES:
            _write_file(adapter_build_dir / dll_name, "dll")

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
            packager = AdapterHostPackager(
                FakeProcessRunner(), FakeBuildOutputOwnershipGuard()
            )
            packager.assemble_package(
                adapter_build_dir=adapter_build_dir,
                host_publish_dir=host_publish_dir,
                package_dir=package_dir,
                repository_root=temp_dir / "repo",
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
            packager = AdapterHostPackager(
                FakeProcessRunner(), FakeBuildOutputOwnershipGuard()
            )
            packager.assemble_package(
                adapter_build_dir=adapter_build_dir,
                host_publish_dir=host_publish_dir,
                package_dir=package_dir,
                repository_root=temp_dir / "repo",
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
            packager = AdapterHostPackager(
                FakeProcessRunner(), FakeBuildOutputOwnershipGuard()
            )
            packager.assemble_package(
                adapter_build_dir=adapter_build_dir,
                host_publish_dir=host_publish_dir,
                package_dir=package_dir,
                repository_root=temp_dir / "repo",
            )
            (package_dir / "Data" / "SKSE" / "Plugins" / ADAPTER_PLUGIN_NAME).unlink()

            with self.assertRaises(FileNotFoundError):
                packager.validate_package(package_dir)

    def test_validate_package_raises_when_a_runtime_dll_is_missing(self) -> None:
        """Verifies a package missing an assembled runtime dependency DLL fails validation."""
        with tempfile.TemporaryDirectory() as temp_dir_str:
            temp_dir = Path(temp_dir_str)
            adapter_build_dir, host_publish_dir = self._build_valid_inputs(temp_dir)
            package_dir = temp_dir / "package"
            packager = AdapterHostPackager(
                FakeProcessRunner(), FakeBuildOutputOwnershipGuard()
            )
            packager.assemble_package(
                adapter_build_dir=adapter_build_dir,
                host_publish_dir=host_publish_dir,
                package_dir=package_dir,
                repository_root=temp_dir / "repo",
            )
            (
                package_dir / "Data" / "SKSE" / "Plugins" / ADAPTER_RUNTIME_DLL_NAMES[0]
            ).unlink()

            with self.assertRaises(FileNotFoundError):
                packager.validate_package(package_dir)

    def test_validate_package_raises_when_the_host_executable_is_missing(self) -> None:
        """Verifies a package missing its assembled Host executable fails validation."""
        with tempfile.TemporaryDirectory() as temp_dir_str:
            temp_dir = Path(temp_dir_str)
            adapter_build_dir, host_publish_dir = self._build_valid_inputs(temp_dir)
            package_dir = temp_dir / "package"
            packager = AdapterHostPackager(
                FakeProcessRunner(), FakeBuildOutputOwnershipGuard()
            )
            packager.assemble_package(
                adapter_build_dir=adapter_build_dir,
                host_publish_dir=host_publish_dir,
                package_dir=package_dir,
                repository_root=temp_dir / "repo",
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
            packager = AdapterHostPackager(
                FakeProcessRunner(), FakeBuildOutputOwnershipGuard()
            )
            packager.assemble_package(
                adapter_build_dir=adapter_build_dir,
                host_publish_dir=host_publish_dir,
                package_dir=package_dir,
                repository_root=temp_dir / "repo",
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
            packager = AdapterHostPackager(
                FakeProcessRunner(), FakeBuildOutputOwnershipGuard()
            )
            packager.assemble_package(
                adapter_build_dir=adapter_build_dir,
                host_publish_dir=host_publish_dir,
                package_dir=package_dir,
                repository_root=temp_dir / "repo",
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
            packager = AdapterHostPackager(
                FakeProcessRunner(), FakeBuildOutputOwnershipGuard()
            )
            packager.assemble_package(
                adapter_build_dir=adapter_build_dir,
                host_publish_dir=host_publish_dir,
                package_dir=package_dir,
                repository_root=temp_dir / "repo",
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
            packager = AdapterHostPackager(
                FakeProcessRunner(), FakeBuildOutputOwnershipGuard()
            )
            packager.assemble_package(
                adapter_build_dir=adapter_build_dir,
                host_publish_dir=host_publish_dir,
                package_dir=package_dir,
                repository_root=temp_dir / "repo",
                console_admin_yaml=yaml_path,
            )

            packager.validate_package(package_dir, console_admin_yaml=yaml_path)


if __name__ == "__main__":
    unittest.main()
