"""Tests for package_adapter_host.py."""

from __future__ import annotations

import contextlib
import io
import tempfile
import unittest
from pathlib import Path
from unittest import mock

from adapter_host_packager import (
    ADAPTER_PLUGIN_NAME,
    ADAPTER_RUNTIME_DLL_NAMES,
    HOST_EXECUTABLE_NAME,
)
from package_adapter_host import (
    STAGE_ARCHIVE,
    STAGE_HOST_PUBLISH,
    STAGE_PACKAGE_ASSEMBLY,
    STAGE_PACKAGE_VALIDATION,
    main,
    parse_args,
    read_product_version,
)


def _write_file(path: Path, content: str = "") -> None:
    """Creates `path`'s parent directories and writes `content` into it."""
    path.parent.mkdir(parents=True, exist_ok=True)
    path.write_text(content, encoding="utf-8")


class ReadProductVersionTests(unittest.TestCase):
    """Tests for read_product_version."""

    def test_read_product_version_returns_the_stripped_file_contents(self) -> None:
        """Verifies the VERSION file's contents come back with surrounding whitespace stripped."""
        with tempfile.TemporaryDirectory() as temp_dir_str:
            version_path = Path(temp_dir_str) / "VERSION"
            version_path.write_text("0.3.3\n", encoding="utf-8")

            self.assertEqual(read_product_version(version_path), "0.3.3")


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

    def test_parse_args_defaults_configuration_and_profile_label_to_release(
        self,
    ) -> None:
        """Verifies the build-profile arguments default to a Release build when omitted."""
        args = parse_args(["--adapter-build-dir", "build", "--output-dir", "out"])

        self.assertEqual(args.configuration, "Release")
        self.assertEqual(args.profile_label, "release")

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

    def test_main_publishes_assembles_validates_and_zips_in_order(self) -> None:
        """Verifies main() publishes, assembles, validates, then zips, reporting each as a stage."""
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

            captured_stdout = io.StringIO()
            with (
                mock.patch(
                    "package_adapter_host.SubprocessProcessRunner.run", fake_run
                ),
                contextlib.redirect_stdout(captured_stdout),
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
            # DovahLinkBuilder's coordinator locates the archive path by scanning this script's
            # stdout for a line starting with "Wrote " (AdapterHostBuildCoordinator.WrittenArchivePrefix)
            # and parses the "##stage <name> <status>" lines into BuildStageEvents
            # (BuildStageProgressParser); this is the only place that cross-language contract is
            # verified.
            expected_lines = [
                f"##stage {STAGE_HOST_PUBLISH} start",
                f"##stage {STAGE_HOST_PUBLISH} done",
                f"##stage {STAGE_PACKAGE_ASSEMBLY} start",
                f"##stage {STAGE_PACKAGE_ASSEMBLY} done",
                f"##stage {STAGE_PACKAGE_VALIDATION} start",
                f"##stage {STAGE_PACKAGE_VALIDATION} done",
                f"##stage {STAGE_ARCHIVE} start",
                f"##stage {STAGE_ARCHIVE} done",
                f"Wrote {zips[0]}",
            ]
            self.assertEqual(
                captured_stdout.getvalue(), "\n".join(expected_lines) + "\n"
            )

    def test_main_suffixes_the_archive_name_for_a_non_release_profile(self) -> None:
        """Verifies a non-release profile label keeps its output from overwriting the release archive."""
        with tempfile.TemporaryDirectory() as temp_dir_str:
            temp_dir = Path(temp_dir_str)
            adapter_build_dir = temp_dir / "adapter_build"
            _write_file(adapter_build_dir / ADAPTER_PLUGIN_NAME, "plugin")
            for dll_name in ADAPTER_RUNTIME_DLL_NAMES:
                _write_file(adapter_build_dir / dll_name, "dll")
            output_dir = temp_dir / "out"

            def fake_run(_self: object, args: list[str]) -> None:
                output_flag_index = args.index("--output")
                publish_dir = Path(args[output_flag_index + 1])
                _write_file(publish_dir / HOST_EXECUTABLE_NAME, "host")

            with (
                mock.patch(
                    "package_adapter_host.SubprocessProcessRunner.run", fake_run
                ),
                contextlib.redirect_stdout(io.StringIO()),
            ):
                exit_code = main(
                    [
                        "--adapter-build-dir",
                        str(adapter_build_dir),
                        "--output-dir",
                        str(output_dir),
                        "--configuration",
                        "Debug",
                        "--profile-label",
                        "debug",
                    ]
                )

            self.assertEqual(exit_code, 0)
            zips = list(output_dir.glob("DovahLink-Adapter-*-debug.zip"))
            self.assertEqual(len(zips), 1)

    def test_main_stops_after_the_start_marker_when_a_stage_fails(self) -> None:
        """Verifies a failing stage reports its start marker but never its done marker."""
        with tempfile.TemporaryDirectory() as temp_dir_str:
            temp_dir = Path(temp_dir_str)
            adapter_build_dir = temp_dir / "adapter_build"
            _write_file(adapter_build_dir / ADAPTER_PLUGIN_NAME, "plugin")
            for dll_name in ADAPTER_RUNTIME_DLL_NAMES:
                _write_file(adapter_build_dir / dll_name, "dll")
            output_dir = temp_dir / "out"

            # publish_host "succeeds" without writing the Host executable, so the next stage
            # (package_assembly) fails validating its own required source file.
            def fake_run(_self: object, args: list[str]) -> None:
                pass

            captured_stdout = io.StringIO()
            with (
                mock.patch(
                    "package_adapter_host.SubprocessProcessRunner.run", fake_run
                ),
                contextlib.redirect_stdout(captured_stdout),
            ):
                with self.assertRaises(FileNotFoundError):
                    main(
                        [
                            "--adapter-build-dir",
                            str(adapter_build_dir),
                            "--output-dir",
                            str(output_dir),
                        ]
                    )

            expected_lines = [
                f"##stage {STAGE_HOST_PUBLISH} start",
                f"##stage {STAGE_HOST_PUBLISH} done",
                f"##stage {STAGE_PACKAGE_ASSEMBLY} start",
            ]
            self.assertEqual(
                captured_stdout.getvalue(), "\n".join(expected_lines) + "\n"
            )


if __name__ == "__main__":
    unittest.main()
