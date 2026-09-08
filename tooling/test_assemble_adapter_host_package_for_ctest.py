"""Tests for assemble_adapter_host_package_for_ctest.py."""

from __future__ import annotations

import tempfile
import unittest
from pathlib import Path
from unittest import mock

from adapter_host_packager import (
    ADAPTER_PLUGIN_NAME,
    ADAPTER_RUNTIME_DLL_NAMES,
    HOST_EXECUTABLE_NAME,
)
from assemble_adapter_host_package_for_ctest import (
    MISSING_RELEASE_RUNTIME_DLLS_SKIP_CODE,
    main,
    parse_args,
)


def _write_file(path: Path, content: str = "") -> None:
    """Creates `path`'s parent directories and writes `content` into it."""
    path.parent.mkdir(parents=True, exist_ok=True)
    path.write_text(content, encoding="utf-8")


def _write_release_adapter_build_dir(adapter_build_dir: Path) -> None:
    """Writes a stand-in adapter build directory with every production runtime DLL name present."""
    _write_file(adapter_build_dir / ADAPTER_PLUGIN_NAME, "plugin")
    for dll_name in ADAPTER_RUNTIME_DLL_NAMES:
        _write_file(adapter_build_dir / dll_name, "dll")


class ParseArgsTests(unittest.TestCase):
    """Tests for parse_args."""

    def test_parse_args_requires_every_argument(self) -> None:
        """Verifies all three required arguments are enforced."""
        with self.assertRaises(SystemExit):
            parse_args([])

    def test_parse_args_parses_every_supplied_argument(self) -> None:
        """Verifies every argument parses into its typed Path value."""
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

    def test_main_missing_release_runtime_dlls_skips_without_assembling(self) -> None:
        """Verifies a Debug-shaped adapter build (missing Release-named DLLs) exits with the skip code."""
        with tempfile.TemporaryDirectory() as temp_dir_str:
            temp_dir = Path(temp_dir_str)
            adapter_build_dir = temp_dir / "adapter_build"
            _write_file(adapter_build_dir / ADAPTER_PLUGIN_NAME, "plugin")
            # Debug-suffixed names instead of the real production ADAPTER_RUNTIME_DLL_NAMES.
            for dll_name in ADAPTER_RUNTIME_DLL_NAMES:
                _write_file(
                    adapter_build_dir / f"{dll_name.removesuffix('.dll')}d.dll", "dll"
                )
            package_dir = temp_dir / "package"

            with mock.patch(
                "assemble_adapter_host_package_for_ctest.AdapterHostPackager.assemble_package"
            ) as assemble_package:
                exit_code = main(
                    [
                        "--adapter-build-dir",
                        str(adapter_build_dir),
                        "--host-publish-dir",
                        str(temp_dir / "host_publish"),
                        "--package-dir",
                        str(package_dir),
                    ]
                )

            self.assertEqual(exit_code, MISSING_RELEASE_RUNTIME_DLLS_SKIP_CODE)
            assemble_package.assert_not_called()
            self.assertFalse(package_dir.exists())

    def test_main_partial_release_runtime_dlls_skips_without_assembling(self) -> None:
        """Verifies even one missing production-named runtime DLL still skips, not just all of them."""
        with tempfile.TemporaryDirectory() as temp_dir_str:
            temp_dir = Path(temp_dir_str)
            adapter_build_dir = temp_dir / "adapter_build"
            _write_file(adapter_build_dir / ADAPTER_PLUGIN_NAME, "plugin")
            # Only the first production-named DLL is present; the rest are missing.
            _write_file(adapter_build_dir / ADAPTER_RUNTIME_DLL_NAMES[0], "dll")
            package_dir = temp_dir / "package"

            with mock.patch(
                "assemble_adapter_host_package_for_ctest.AdapterHostPackager.assemble_package"
            ) as assemble_package:
                exit_code = main(
                    [
                        "--adapter-build-dir",
                        str(adapter_build_dir),
                        "--host-publish-dir",
                        str(temp_dir / "host_publish"),
                        "--package-dir",
                        str(package_dir),
                    ]
                )

            self.assertEqual(exit_code, MISSING_RELEASE_RUNTIME_DLLS_SKIP_CODE)
            assemble_package.assert_not_called()

    def test_main_release_runtime_dlls_present_assembles_the_real_package(self) -> None:
        """Verifies a Release-shaped adapter build assembles the real package and returns success."""
        with tempfile.TemporaryDirectory() as temp_dir_str:
            temp_dir = Path(temp_dir_str)
            adapter_build_dir = temp_dir / "adapter_build"
            _write_release_adapter_build_dir(adapter_build_dir)
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

    def test_main_never_publishes_the_host(self) -> None:
        """Verifies main() never invokes dotnet publish, always reusing the given host_publish_dir."""
        with tempfile.TemporaryDirectory() as temp_dir_str:
            temp_dir = Path(temp_dir_str)
            adapter_build_dir = temp_dir / "adapter_build"
            _write_release_adapter_build_dir(adapter_build_dir)
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


if __name__ == "__main__":
    unittest.main()
