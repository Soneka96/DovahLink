"""Test staged-file selection and formatter command construction."""

import tempfile
import unittest
from pathlib import Path
from unittest.mock import patch

from tooling import format_staged


class FormatStagedTests(unittest.TestCase):
    """Verify the formatter hook's safety rules and command mapping."""

    def test_partial_staged_paths_returns_only_overlapping_paths(self) -> None:
        """Reject only files that have both index and worktree changes."""
        self.assertEqual(
            format_staged.partial_staged_paths(
                ["app/main.dart", "bridge/main.cpp"],
                ["bridge/main.cpp", "README.md"],
            ),
            ["bridge/main.cpp"],
        )

    def test_formatter_group_selects_supported_extensions(self) -> None:
        """Map the selected core-language extensions and leave other files unsupported."""
        self.assertEqual(format_staged.formatter_group("app/main.dart"), "dart")
        self.assertEqual(format_staged.formatter_group("bridge/main.cpp"), "cpp")
        self.assertEqual(format_staged.formatter_group("tooling/check.py"), "python")
        self.assertEqual(
            format_staged.formatter_group("tooling/check.ps1"), "powershell"
        )
        self.assertIsNone(format_staged.formatter_group("protocol/schema.json"))

    def test_parse_git_paths_preserves_non_utf8_bytes(self) -> None:
        """Decode Git paths without crashing on bytes outside UTF-8."""
        self.assertEqual(
            format_staged.parse_git_paths(b"tooling/\xff.py\0"), ["tooling/\udcff.py"]
        )

    def test_execute_commands_preflights_all_formatters(self) -> None:
        """Find a missing later formatter before an earlier formatter can modify files."""
        with (
            patch.object(
                format_staged.shutil,
                "which",
                side_effect=lambda name: None if name == "ruff" else name,
            ),
            patch.object(format_staged.subprocess, "run") as run,
        ):
            result = format_staged.execute_commands(
                Path("."),
                [["dart", "format", "file.dart"], ["ruff", "format", "file.py"]],
            )

        self.assertEqual(result, 127)
        run.assert_not_called()

    def test_powershell_command_fails_closed_when_no_shell_exists(self) -> None:
        """Reject PowerShell formatting when neither supported shell executable exists."""
        with patch.object(format_staged.shutil, "which", return_value=None):
            with self.assertRaisesRegex(RuntimeError, "pwsh or powershell"):
                format_staged.powershell_command(["tooling/check.ps1"], check=False)

    def test_formatter_commands_use_check_modes(self) -> None:
        """Build fail-closed check commands for Dart, C++, Python, and PowerShell."""
        with tempfile.TemporaryDirectory() as temporary_directory:
            root = Path(temporary_directory)
            paths = [
                "app/main.dart",
                "bridge/main.cpp",
                "tooling/check.py",
                "tooling/check.ps1",
            ]

            commands = format_staged.formatter_commands(root, paths, check=True)

        self.assertEqual(
            commands[0][:4],
            ["dart", "format", "--output=none", "--set-exit-if-changed"],
        )
        self.assertEqual(commands[1][:3], ["clang-format", "--dry-run", "--Werror"])
        self.assertEqual(commands[2][:3], ["ruff", "format", "--check"])
        self.assertIn("Invoke-Formatter", commands[3][4])

    def test_csharp_command_is_limited_to_the_nearest_project(self) -> None:
        """Pass only the staged C# file to its owning project formatter."""
        with tempfile.TemporaryDirectory() as temporary_directory:
            root = Path(temporary_directory)
            project_directory = root / "tooling" / "BridgeBuilder"
            project_directory.mkdir(parents=True)
            (project_directory / "BridgeBuilder.csproj").touch()
            source = project_directory / "Program.cs"
            source.write_text("class Program {}", encoding="utf-8")

            commands = format_staged.formatter_commands(
                root,
                ["tooling/BridgeBuilder/Program.cs"],
                check=True,
            )

        self.assertEqual(
            commands,
            [
                [
                    "dotnet",
                    "format",
                    "whitespace",
                    str(project_directory / "BridgeBuilder.csproj"),
                    "--no-restore",
                    "--verify-no-changes",
                    "--include",
                    "Program.cs",
                ]
            ],
        )

    def test_main_rejects_partially_staged_files_before_formatting(self) -> None:
        """Stop before invoking any formatter when a supported file is partially staged."""
        with (
            patch.object(
                format_staged,
                "staged_paths",
                return_value=["app/lib/main.dart"],
            ),
            patch.object(
                format_staged,
                "unstaged_paths",
                return_value=["app/lib/main.dart"],
            ),
            patch.object(format_staged, "format_paths") as format_paths,
        ):
            result = format_staged.main([])

        self.assertEqual(result, 1)
        format_paths.assert_not_called()

    def test_main_ignores_partially_staged_unsupported_files(self) -> None:
        """Allow a partial unsupported file without invoking a formatter for it."""
        with (
            patch.object(format_staged, "staged_paths", return_value=["README.md"]),
            patch.object(format_staged, "unstaged_paths", return_value=["README.md"]),
            patch.object(format_staged, "format_paths", return_value=0) as format_paths,
        ):
            result = format_staged.main([])

        self.assertEqual(result, 0)
        format_paths.assert_called_once_with(
            format_staged.REPOSITORY_ROOT,
            ["README.md"],
            False,
        )


if __name__ == "__main__":
    unittest.main()
