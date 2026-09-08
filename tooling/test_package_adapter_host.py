"""Tests for package_adapter_host.py."""

from __future__ import annotations

import json
import tempfile
import unittest
from pathlib import Path
from unittest import mock

from adapter_host_packager import (
    ADAPTER_PLUGIN_NAME,
    ADAPTER_RUNTIME_DLL_NAMES,
    HOST_EXECUTABLE_NAME,
)
from package_adapter_host import main, parse_args, read_adapter_version


def _write_file(path: Path, content: str = "") -> None:
    """Creates `path`'s parent directories and writes `content` into it."""
    path.parent.mkdir(parents=True, exist_ok=True)
    path.write_text(content, encoding="utf-8")


class ReadAdapterVersionTests(unittest.TestCase):
    """Tests for read_adapter_version."""

    def test_read_adapter_version_returns_the_version_string_field(self) -> None:
        """Verifies the manifest's version-string field is returned as-is."""
        with tempfile.TemporaryDirectory() as temp_dir_str:
            manifest_path = Path(temp_dir_str) / "vcpkg.json"
            manifest_path.write_text(
                json.dumps({"name": "dovahlink-adapter", "version-string": "0.3.3"}),
                encoding="utf-8",
            )

            self.assertEqual(read_adapter_version(manifest_path), "0.3.3")


class ParseArgsTests(unittest.TestCase):
    """Tests for parse_args."""

    def test_parse_args_requires_adapter_build_dir_and_output_dir(self) -> None:
        """Verifies the two required arguments are enforced."""
        with self.assertRaises(SystemExit):
            parse_args([])

    def test_parse_args_defaults_console_admin_paths_to_none(self) -> None:
        """Verifies the optional console-admin arguments default to None when omitted."""
        args = parse_args(["--adapter-build-dir", "build", "--output-dir", "out"])

        self.assertEqual(args.adapter_build_dir, Path("build"))
        self.assertEqual(args.output_dir, Path("out"))
        self.assertIsNone(args.console_admin_pex)
        self.assertIsNone(args.console_admin_yaml)

    def test_parse_args_parses_every_supplied_argument(self) -> None:
        """Verifies every argument, including the optional ones, parses into its typed value."""
        args = parse_args(
            [
                "--adapter-build-dir",
                "build",
                "--output-dir",
                "out",
                "--console-admin-pex",
                "a.pex",
                "--console-admin-yaml",
                "a.yaml",
            ]
        )

        self.assertEqual(args.console_admin_pex, Path("a.pex"))
        self.assertEqual(args.console_admin_yaml, Path("a.yaml"))


class MainTests(unittest.TestCase):
    """Tests for main, proving the production pipeline is wired in the correct order."""

    def test_main_publishes_assembles_and_zips_in_order(self) -> None:
        """Verifies main() publishes the Host, assembles the package, then zips it, in that order."""
        with tempfile.TemporaryDirectory() as temp_dir_str:
            temp_dir = Path(temp_dir_str)
            adapter_build_dir = temp_dir / "adapter_build"
            _write_file(adapter_build_dir / ADAPTER_PLUGIN_NAME, "plugin")
            for dll_name in ADAPTER_RUNTIME_DLL_NAMES:
                _write_file(adapter_build_dir / dll_name, "dll")
            output_dir = temp_dir / "out"

            # SubprocessProcessRunner.run is the only real external side effect main() performs
            # directly (dotnet publish); it is faked here so this test never invokes dotnet, and
            # instead writes the published exe itself, standing in for a real publish.
            def fake_run(_self: object, args: list[str]) -> None:
                output_flag_index = args.index("--output")
                publish_dir = Path(args[output_flag_index + 1])
                _write_file(publish_dir / HOST_EXECUTABLE_NAME, "host")

            with mock.patch(
                "package_adapter_host.SubprocessProcessRunner.run", fake_run
            ):
                exit_code = main(
                    [
                        "--adapter-build-dir",
                        str(adapter_build_dir),
                        "--output-dir",
                        str(output_dir),
                    ]
                )

            self.assertEqual(exit_code, 0)
            plugins_dir = output_dir / "package" / "Data" / "SKSE" / "Plugins"
            self.assertTrue((plugins_dir / ADAPTER_PLUGIN_NAME).is_file())
            self.assertTrue(
                (plugins_dir / "DovahLink.Host" / HOST_EXECUTABLE_NAME).is_file()
            )
            zips = list(output_dir.glob("DovahLink-Adapter-*.zip"))
            self.assertEqual(len(zips), 1)


if __name__ == "__main__":
    unittest.main()
