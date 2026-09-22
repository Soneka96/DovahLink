"""Tests for assemble_adapter_host_package_for_ctest.py."""

from __future__ import annotations

import tempfile
import unittest
from pathlib import Path
from unittest import mock

from adapter_host_packager import (
    ADAPTER_PLUGIN_NAME,
    HOST_EXECUTABLE_NAME,
)
from assemble_adapter_host_package_for_ctest import (
    main,
    parse_args,
)
from build_output_ownership import MARKER_FILE_NAME
from test_adapter_import_validator import _build_pe


def _write_file(path: Path, content: str = "") -> None:
    """Creates `path`'s parent directories and writes `content` into it."""
    path.parent.mkdir(parents=True, exist_ok=True)
    path.write_text(content, encoding="utf-8")


def _write_adapter_build_dir(adapter_build_dir: Path) -> None:
    """Writes a stand-in adapter build containing only the plugin."""
    plugin_path = adapter_build_dir / ADAPTER_PLUGIN_NAME
    plugin_path.parent.mkdir(parents=True, exist_ok=True)
    plugin_path.write_bytes(_build_pe(["kernel32.dll"]))


class ParseArgsTests(unittest.TestCase):
    """Tests for parse_args."""

    def test_parse_args_requires_every_argument(self) -> None:
        """Verifies all three required arguments are enforced."""
        with self.assertRaises(SystemExit):
            parse_args([])

    def test_parse_args_parses_every_supplied_argument(self) -> None:
        """Verifies every argument parses into its typed value."""
        args = parse_args(
            [
                "--adapter-build-dir",
                "build",
                "--host-publish-dir",
                "publish",
                "--package-dir",
                "package",
            ]
        )

        self.assertEqual(args.adapter_build_dir, Path("build"))
        self.assertEqual(args.host_publish_dir, Path("publish"))
        self.assertEqual(args.package_dir, Path("package"))


class MainTests(unittest.TestCase):
    """Tests for main."""

    def test_main_plugin_without_dependency_dlls_assembles_the_real_package(
        self,
    ) -> None:
        """Verifies an adapter build without dependency DLLs assembles the real package and returns success."""
        with tempfile.TemporaryDirectory() as temp_dir_str:
            temp_dir = Path(temp_dir_str)
            adapter_build_dir = temp_dir / "adapter_build"
            _write_adapter_build_dir(adapter_build_dir)
            host_publish_dir = temp_dir / "host_publish"
            _write_file(host_publish_dir / HOST_EXECUTABLE_NAME, "host")
            package_dir = temp_dir / "package"

            exit_code = main(
                [
                    "--adapter-build-dir",
                    str(adapter_build_dir),
                    "--host-publish-dir",
                    str(host_publish_dir),
                    "--package-dir",
                    str(package_dir),
                ]
            )

            self.assertEqual(exit_code, 0)
            plugins_dir = package_dir / "Data" / "SKSE" / "Plugins"
            self.assertTrue((plugins_dir / ADAPTER_PLUGIN_NAME).is_file())
            self.assertTrue(
                (plugins_dir / "DovahLink.Host" / HOST_EXECUTABLE_NAME).is_file()
            )

    def test_main_missing_required_input_fails_and_preserves_existing_package(
        self,
    ) -> None:
        """A missing plugin or Host fails assembly rather than skipping either build profile."""
        for missing in (ADAPTER_PLUGIN_NAME, HOST_EXECUTABLE_NAME):
            with self.subTest(missing=missing), tempfile.TemporaryDirectory() as temp:
                root = Path(temp)
                build = root / "build"
                publish = root / "publish"
                _write_adapter_build_dir(build)
                _write_file(publish / HOST_EXECUTABLE_NAME, "host")
                (
                    build / missing
                    if missing == ADAPTER_PLUGIN_NAME
                    else publish / missing
                ).unlink()
                package = root / "package"
                _write_file(package / MARKER_FILE_NAME, "owned")
                sentinel = package / "previous.txt"
                _write_file(sentinel, "preserve")
                with self.assertRaises(FileNotFoundError):
                    main(
                        [
                            "--adapter-build-dir",
                            str(build),
                            "--host-publish-dir",
                            str(publish),
                            "--package-dir",
                            str(package),
                        ]
                    )
                self.assertEqual(sentinel.read_text(), "preserve")

    def test_main_never_publishes_the_host(self) -> None:
        """Verifies main() never invokes dotnet publish, always reusing the given host_publish_dir."""
        with tempfile.TemporaryDirectory() as temp_dir_str:
            temp_dir = Path(temp_dir_str)
            adapter_build_dir = temp_dir / "adapter_build"
            _write_adapter_build_dir(adapter_build_dir)
            host_publish_dir = temp_dir / "host_publish"
            _write_file(host_publish_dir / HOST_EXECUTABLE_NAME, "host")

            with mock.patch(
                "assemble_adapter_host_package_for_ctest.SubprocessProcessRunner.run"
            ) as run:
                exit_code = main(
                    [
                        "--adapter-build-dir",
                        str(adapter_build_dir),
                        "--host-publish-dir",
                        str(host_publish_dir),
                        "--package-dir",
                        str(temp_dir / "package"),
                    ]
                )

            self.assertEqual(exit_code, 0)
            run.assert_not_called()

    def test_main_refuses_an_unrelated_non_empty_package_dir(self) -> None:
        """Verifies an unmarked, unrelated, non-empty --package-dir is refused before assembly ever runs."""
        with tempfile.TemporaryDirectory() as temp_dir_str:
            temp_dir = Path(temp_dir_str)
            adapter_build_dir = temp_dir / "adapter_build"
            _write_adapter_build_dir(adapter_build_dir)
            host_publish_dir = temp_dir / "host_publish"
            _write_file(host_publish_dir / HOST_EXECUTABLE_NAME, "host")
            package_dir = temp_dir / "unrelated-user-folder"
            unrelated_file = package_dir / "some-real-file.txt"
            _write_file(unrelated_file, "the user's own real data, not DovahLink's")

            with mock.patch(
                "assemble_adapter_host_package_for_ctest.AdapterHostPackager.assemble_package"
            ) as assemble_package:
                with self.assertRaises(RuntimeError):
                    main(
                        [
                            "--adapter-build-dir",
                            str(adapter_build_dir),
                            "--host-publish-dir",
                            str(host_publish_dir),
                            "--package-dir",
                            str(package_dir),
                        ]
                    )

            assemble_package.assert_not_called()
            self.assertFalse((package_dir / MARKER_FILE_NAME).is_file())
            self.assertEqual(
                "the user's own real data, not DovahLink's",
                unrelated_file.read_text(encoding="utf-8"),
            )

    def test_main_keeps_a_freshly_adopted_package_dir_owned_across_repeated_runs(
        self,
    ) -> None:
        """
        Regression test: assemble_package replaces package_dir itself -- the exact directory
        main() just marked as owned -- so a naive implementation would destroy that mark on every
        run, permanently locking a second, ordinary incremental CTest rebuild out of its own
        previous output. Proves three consecutive runs against the same nonexistent-at-first
        package_dir all succeed.
        """
        with tempfile.TemporaryDirectory() as temp_dir_str:
            temp_dir = Path(temp_dir_str)
            adapter_build_dir = temp_dir / "adapter_build"
            _write_adapter_build_dir(adapter_build_dir)
            host_publish_dir = temp_dir / "host_publish"
            _write_file(host_publish_dir / HOST_EXECUTABLE_NAME, "host")
            package_dir = temp_dir / "package"

            for _ in range(3):
                exit_code = main(
                    [
                        "--adapter-build-dir",
                        str(adapter_build_dir),
                        "--host-publish-dir",
                        str(host_publish_dir),
                        "--package-dir",
                        str(package_dir),
                    ]
                )
                self.assertEqual(exit_code, 0)

            plugins_dir = package_dir / "Data" / "SKSE" / "Plugins"
            self.assertTrue((plugins_dir / ADAPTER_PLUGIN_NAME).is_file())
            self.assertTrue((package_dir / MARKER_FILE_NAME).is_file())


if __name__ == "__main__":
    unittest.main()
