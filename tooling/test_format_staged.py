"""Test staged-file selection and formatter command construction."""

import subprocess
import tempfile
import unittest
from contextlib import redirect_stderr
from io import StringIO
from pathlib import Path
from unittest.mock import patch

from tooling import format_staged


class FormatStagedTests(unittest.TestCase):
    """Verify the formatter hook's safety rules and command mapping."""

    def test_partial_staged_paths_returns_only_overlapping_paths(self) -> None:
        """Reject only files that have both index and worktree changes."""
        self.assertEqual(
            format_staged.partial_staged_paths(
                ["app/main.dart", "adapter/main.cpp"],
                ["adapter/main.cpp", "README.md"],
            ),
            ["adapter/main.cpp"],
        )

    def test_comparison_paths_combines_branch_and_local_paths(self) -> None:
        """Select committed, staged, unstaged, and untracked paths without losing duplicates."""
        completed = lambda output: subprocess.CompletedProcess(
            [], 0, stdout=output, stderr=b""
        )
        with patch.object(
            format_staged,
            "run_git",
            side_effect=[
                completed(b"base-sha\n"),
                completed(b"app/main.dart\0"),
                completed(b"tooling/new.py\0"),
                completed(b"adapter/main.cpp\0"),
                completed(b"app/main.dart\0"),
            ],
        ):
            paths = format_staged.comparison_paths(Path("."), "main")

        self.assertEqual(
            paths,
            ["adapter/main.cpp", "app/main.dart", "tooling/new.py"],
        )

    def test_comparison_paths_fails_closed_when_git_selection_fails(self) -> None:
        """Reject a base-ref check when any Git path-selection command fails."""
        successful = subprocess.CompletedProcess(
            [], 0, stdout=b"base-sha\n", stderr=b""
        )
        diff_successful = subprocess.CompletedProcess(
            [], 0, stdout=b"app/main.dart\0", stderr=b""
        )
        failed = subprocess.CompletedProcess([], 1, stdout=b"", stderr=b"git failed")
        scenarios = (
            [failed],
            [successful, failed],
            [successful, diff_successful, failed],
        )

        for responses in scenarios:
            with (
                self.subTest(responses=responses),
                patch.object(format_staged, "run_git", side_effect=responses),
            ):
                with self.assertRaisesRegex(RuntimeError, "git failed"):
                    format_staged.comparison_paths(Path("."), "main")

    def test_formatter_group_selects_supported_extensions(self) -> None:
        """Map the selected core-language extensions and leave other files unsupported."""
        self.assertEqual(format_staged.formatter_group("app/main.dart"), "dart")
        self.assertEqual(format_staged.formatter_group("adapter/main.cpp"), "cpp")
        self.assertEqual(format_staged.formatter_group("tooling/check.py"), "python")
        self.assertEqual(
            format_staged.formatter_group("tooling/check.ps1"), "powershell"
        )
        self.assertIsNone(format_staged.formatter_group("protocol/schema.json"))

    def test_parse_arguments_accepts_option_looking_paths(self) -> None:
        """Capture a repository path beginning with a hyphen after `--paths`."""
        options = format_staged.parse_arguments(["--check", "--paths", "--fixture.py"])

        self.assertTrue(options.check)
        self.assertEqual(options.paths, ["--fixture.py"])

    def test_parse_arguments_accepts_a_base_ref(self) -> None:
        """Capture a base ref used to select local pre-push formatter paths."""
        options = format_staged.parse_arguments(["--check", "--base-ref", "main"])

        self.assertTrue(options.check)
        self.assertEqual(options.base_ref, "main")

    def test_parse_git_paths_preserves_non_utf8_bytes(self) -> None:
        """Decode Git paths without crashing on bytes outside UTF-8."""
        self.assertEqual(
            format_staged.parse_git_paths(b"tooling/\xff.py\0"), ["tooling/\udcff.py"]
        )

    def test_execute_commands_preflights_all_formatters(self) -> None:
        """Find a missing later formatter before an earlier formatter can modify files."""
        resolved_python = r"C:\Python313\python.exe"
        with (
            patch.object(
                format_staged.shutil,
                "which",
                side_effect=lambda name: (
                    None if name == "clang-format" else resolved_python
                ),
            ),
            patch.object(
                format_staged.subprocess,
                "run",
                return_value=subprocess.CompletedProcess([], 0),
            ) as run,
        ):
            result = format_staged.execute_commands(
                Path("."),
                [
                    (Path("."), ["clang-format", "--dry-run", "file.cpp"]),
                    (
                        Path("."),
                        [
                            format_staged.sys.executable,
                            "-m",
                            "ruff",
                            "format",
                            "file.py",
                        ],
                    ),
                ],
            )

        self.assertEqual(result, 127)
        run.assert_called_once_with(
            [resolved_python, "-m", "ruff", "--version"],
            cwd=Path("."),
            check=False,
            stdout=subprocess.PIPE,
            stderr=subprocess.PIPE,
        )

    def test_execute_commands_rejects_missing_ruff_module_before_formatting(
        self,
    ) -> None:
        """Do not run any formatter if the selected Python cannot import Ruff."""
        resolved_python = r"C:\Python313\python.exe"
        with (
            patch.object(format_staged.shutil, "which", return_value=resolved_python),
            patch.object(
                format_staged.subprocess,
                "run",
                return_value=subprocess.CompletedProcess(
                    [], 1, stderr=b"No module named ruff"
                ),
            ) as run,
        ):
            result = format_staged.execute_commands(
                Path("."),
                [
                    (
                        Path("."),
                        [
                            format_staged.sys.executable,
                            "-m",
                            "ruff",
                            "format",
                            "file.py",
                        ],
                    )
                ],
            )

        self.assertEqual(result, 127)
        run.assert_called_once_with(
            [resolved_python, "-m", "ruff", "--version"],
            cwd=Path("."),
            check=False,
            stdout=subprocess.PIPE,
            stderr=subprocess.PIPE,
        )

    def test_execute_commands_runs_ruff_with_the_active_python_module(self) -> None:
        """Use the formatter module from the Python interpreter running local CI."""
        resolved_python = r"C:\Python313\python.exe"
        with (
            patch.object(format_staged.shutil, "which", return_value=resolved_python),
            patch.object(
                format_staged.subprocess,
                "run",
                side_effect=[
                    subprocess.CompletedProcess([], 0, stdout=b"ruff 0.16.8"),
                    subprocess.CompletedProcess([], 0),
                ],
            ) as run,
        ):
            result = format_staged.execute_commands(
                Path("."),
                [
                    (
                        Path("."),
                        [
                            format_staged.sys.executable,
                            "-m",
                            "ruff",
                            "format",
                            "--check",
                            "file.py",
                        ],
                    )
                ],
            )

        self.assertEqual(result, 0)
        self.assertEqual(
            [call.args[0] for call in run.call_args_list],
            [
                [resolved_python, "-m", "ruff", "--version"],
                [resolved_python, "-m", "ruff", "format", "--check", "file.py"],
            ],
        )

    def test_execute_commands_uses_shell_quoting_for_batch_wrapper_paths(self) -> None:
        """Quote staged metacharacters when launching a Windows batch formatter."""
        resolved_dart = r"C:\Dart\flutter\bin\dart.BAT"
        with (
            patch.object(format_staged, "is_windows_batch_wrapper", return_value=True),
            patch.object(format_staged.shutil, "which", return_value=resolved_dart),
            patch.object(
                format_staged.subprocess,
                "run",
                return_value=subprocess.CompletedProcess([], 0),
            ) as run,
        ):
            result = format_staged.execute_commands(
                Path("."),
                [(Path("."), ["dart", "format", "tooling/format&safe.py"])],
            )

        self.assertEqual(result, 0)
        run.assert_called_once_with(
            [resolved_dart, "format", "tooling/format&safe.py"],
            cwd=Path("."),
            check=False,
            shell=True,
        )

    def test_execute_commands_keeps_normal_executables_shell_free(self) -> None:
        """Run ordinary formatter executables without introducing a shell."""
        resolved_dart = r"C:\Dart\flutter\bin\dart.exe"
        with (
            patch.object(format_staged.os, "name", "posix"),
            patch.object(format_staged.shutil, "which", return_value=resolved_dart),
            patch.object(
                format_staged.subprocess,
                "run",
                return_value=subprocess.CompletedProcess([], 0),
            ) as run,
        ):
            repository_root = Path(".")
            result = format_staged.execute_commands(
                repository_root,
                [(repository_root, ["dart", "format", "file.dart"])],
            )

        self.assertEqual(result, 0)
        run.assert_called_once_with(
            [resolved_dart, "format", "file.dart"],
            cwd=repository_root,
            check=False,
        )

    def test_is_windows_batch_wrapper_recognizes_bat_and_cmd_only_on_windows(
        self,
    ) -> None:
        """Recognize both Windows wrapper extensions without affecting other hosts."""
        with patch.object(format_staged.os, "name", "nt"):
            self.assertTrue(format_staged.is_windows_batch_wrapper("dart.BAT"))
            self.assertTrue(format_staged.is_windows_batch_wrapper("dart.cmd"))
        with patch.object(format_staged.os, "name", "posix"):
            self.assertFalse(format_staged.is_windows_batch_wrapper("dart.BAT"))

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
                "app/lib/main.dart",
                "sdk/dart/dovahlink_client/lib/src/client.dart",
                "adapter/main.cpp",
                "tooling/check.py",
                "tooling/check.ps1",
            ]

            commands = format_staged.formatter_commands(root, paths, check=True)

        self.assertEqual(commands[0][0], root / "app")
        self.assertEqual(
            commands[0][1],
            [
                "dart",
                "run",
                "tidy_imports",
                "--exit-if-changed",
                r"lib[/\\]main\.dart",
            ],
        )
        self.assertEqual(commands[1][0], root / "sdk/dart/dovahlink_client")
        self.assertEqual(
            commands[1][1],
            [
                "dart",
                "run",
                "tidy_imports",
                "--exit-if-changed",
                r"lib[/\\]src[/\\]client\.dart",
            ],
        )
        self.assertEqual(
            commands[2][1][:4],
            ["dart", "format", "--output=none", "--set-exit-if-changed"],
        )
        clang_format_command = (
            "clang-format.exe" if format_staged.os.name == "nt" else "clang-format"
        )
        self.assertEqual(
            commands[3][1][:3], [clang_format_command, "--dry-run", "--Werror"]
        )
        self.assertEqual(
            commands[4][1][:4],
            [format_staged.sys.executable, "-m", "ruff", "format"],
        )
        self.assertEqual(commands[4][1][4:5], ["--check"])
        self.assertIn("Invoke-Formatter", commands[5][1][4])

    def test_formatter_commands_sort_only_selected_dart_files(self) -> None:
        """Build package-local import commands from selected Dart paths only."""
        with tempfile.TemporaryDirectory() as temporary_directory:
            root = Path(temporary_directory)
            commands = format_staged.formatter_commands(
                root,
                [
                    "app/lib/features/main.screen.dart",
                    "app/test/features/[main].dart",
                    "sdk/dart/dovahlink_client/lib/src/client.dart",
                    "sdk/dart/dovahlink_client/test/client_test.dart",
                    "protocol/schema.json",
                ],
                check=False,
            )

        self.assertEqual(
            commands[0][0],
            root / "app",
        )
        self.assertEqual(
            commands[0][1],
            [
                "dart",
                "run",
                "tidy_imports",
                r"lib[/\\]features[/\\]main\.screen\.dart",
                r"test[/\\]features[/\\]\[main\]\.dart",
            ],
        )
        self.assertEqual(
            commands[1][0],
            root / "sdk/dart/dovahlink_client",
        )
        self.assertEqual(
            commands[1][1],
            [
                "dart",
                "run",
                "tidy_imports",
                r"lib[/\\]src[/\\]client\.dart",
                r"test[/\\]client_test\.dart",
            ],
        )
        self.assertEqual(
            commands[2],
            (
                root,
                [
                    "dart",
                    "format",
                    "app/lib/features/main.screen.dart",
                    "app/test/features/[main].dart",
                    "sdk/dart/dovahlink_client/lib/src/client.dart",
                    "sdk/dart/dovahlink_client/test/client_test.dart",
                ],
            ),
        )

    def test_formatter_commands_split_long_import_lists(self) -> None:
        """Keep package-local import commands under the batch wrapper limit."""
        with tempfile.TemporaryDirectory() as temporary_directory:
            root = Path(temporary_directory)
            paths = [
                f"app/lib/{'nested/' * 25}screen-{index}.dart" for index in range(35)
            ]
            commands = format_staged.formatter_commands(root, paths, check=True)

        sorter_commands = [
            (working_directory, command)
            for working_directory, command in commands
            if command[:3] == ["dart", "run", "tidy_imports"]
        ]
        dart_format_commands = [
            command for _, command in commands if command[:2] == ["dart", "format"]
        ]
        patterns = [
            pattern for _, command in sorter_commands for pattern in command[4:]
        ]
        formatted_paths = [
            path for command in dart_format_commands for path in command[4:]
        ]

        self.assertGreater(len(sorter_commands), 1)
        self.assertGreater(len(dart_format_commands), 1)
        self.assertEqual(len(patterns), len(paths))
        self.assertEqual(formatted_paths, paths)
        self.assertEqual(
            {pattern.rsplit("screen", 1)[1] for pattern in patterns},
            {f"\\-{index}\\.dart" for index in range(len(paths))},
        )
        for command in [
            *(command for _, command in sorter_commands),
            *dart_format_commands,
        ]:
            self.assertLessEqual(
                len(
                    subprocess.list2cmdline(
                        [r"C:\Dart\flutter\bin\dart.BAT", *command[1:]]
                    )
                ),
                format_staged.MAX_DART_COMMAND_LENGTH,
            )

    def test_formatter_commands_reject_a_single_oversized_dart_path(self) -> None:
        """Reject a Dart path that cannot fit in a bounded formatter command."""
        path = f"app/lib/{'nested/' * 1000}screen.dart"
        with self.assertRaisesRegex(
            RuntimeError, "path exceeds the command-line limit"
        ):
            format_staged.formatter_commands(Path("."), [path], check=False)

    def test_formatter_commands_reject_oversized_second_path_after_valid_first_path(
        self,
    ) -> None:
        """Reject an oversized Dart path supplied after a valid path."""
        paths = [
            "app/lib/main.dart",
            f"app/lib/{'nested/' * 1000}screen.dart",
        ]
        with self.assertRaisesRegex(
            RuntimeError, "path exceeds the command-line limit"
        ):
            format_staged.formatter_commands(Path("."), paths, check=False)

    def test_execute_commands_stops_after_import_sorting_fails(self) -> None:
        """Do not run Dart formatting when import sorting fails."""
        resolved_dart = r"C:\Dart\flutter\bin\dart.exe"
        package_root = Path("app")
        with (
            patch.object(format_staged.shutil, "which", return_value=resolved_dart),
            patch.object(
                format_staged.subprocess,
                "run",
                return_value=subprocess.CompletedProcess([], 1),
            ) as run,
        ):
            result = format_staged.execute_commands(
                Path("."),
                [
                    (
                        package_root,
                        ["dart", "run", "tidy_imports", r"lib[/\\]main\.dart"],
                    ),
                    (
                        Path("."),
                        ["dart", "format", "app/lib/main.dart"],
                    ),
                ],
            )

        self.assertEqual(result, 1)
        run.assert_called_once_with(
            [
                resolved_dart,
                "run",
                "tidy_imports",
                r"lib[/\\]main\.dart",
            ],
            cwd=package_root,
            check=False,
        )

    def test_execute_commands_preflights_missing_dart_for_import_sorting(self) -> None:
        """Reject missing Dart before invoking any formatter command."""
        with (
            patch.object(format_staged.shutil, "which", return_value=None),
            patch.object(format_staged.subprocess, "run") as run,
        ):
            result = format_staged.execute_commands(
                Path("."),
                [
                    (
                        Path("app"),
                        ["dart", "run", "tidy_imports", r"lib[/\\]main\.dart"],
                    ),
                    (Path("."), ["dart", "format", "app/lib/main.dart"]),
                ],
            )

        self.assertEqual(result, 127)
        run.assert_not_called()

    def test_execute_commands_uses_package_root_for_dart_import_sorting(self) -> None:
        """Run import sorting from the owning Dart package root."""
        package_root = Path("app")
        resolved_dart = r"C:\Dart\flutter\bin\dart.BAT"
        with (
            patch.object(format_staged, "is_windows_batch_wrapper", return_value=True),
            patch.object(format_staged.shutil, "which", return_value=resolved_dart),
            patch.object(
                format_staged.subprocess,
                "run",
                return_value=subprocess.CompletedProcess([], 0),
            ) as run,
        ):
            result = format_staged.execute_commands(
                Path("."),
                [
                    (
                        package_root,
                        ["dart", "run", "tidy_imports", r"lib[/\\]main\.dart"],
                    )
                ],
            )

        self.assertEqual(result, 0)
        run.assert_called_once_with(
            [
                resolved_dart,
                "run",
                "tidy_imports",
                r"lib[/\\]main\.dart",
            ],
            cwd=package_root,
            check=False,
            shell=True,
        )

    def test_windows_cpp_formatter_uses_explicit_executable_name(self) -> None:
        """Avoid PATHEXT wrappers that the Windows prerequisite checker does not validate."""
        repository_root = Path(".")
        with patch.object(format_staged.os, "name", "nt"):
            commands = format_staged.formatter_commands(
                repository_root, ["adapter/main.cpp"], check=True
            )

        self.assertEqual(
            commands,
            [
                (
                    repository_root,
                    ["clang-format.exe", "--dry-run", "--Werror", "adapter/main.cpp"],
                )
            ],
        )

    def test_csharp_command_is_limited_to_the_nearest_project(self) -> None:
        """Pass only the staged C# file to its owning project formatter."""
        with tempfile.TemporaryDirectory() as temporary_directory:
            root = Path(temporary_directory)
            project_directory = root / "tooling" / "DovahLinkBuilder"
            project_directory.mkdir(parents=True)
            (project_directory / "DovahLinkBuilder.csproj").touch()
            source = project_directory / "Program.cs"
            source.write_text("class Program {}", encoding="utf-8")

            commands = format_staged.formatter_commands(
                root,
                ["tooling/DovahLinkBuilder/Program.cs"],
                check=True,
            )

        self.assertEqual(
            commands,
            [
                (
                    root,
                    [
                        "dotnet",
                        "format",
                        "whitespace",
                        str(project_directory / "DovahLinkBuilder.csproj"),
                        "--no-restore",
                        "--verify-no-changes",
                        "--include",
                        "Program.cs",
                    ],
                )
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

    def test_main_uses_base_ref_paths_for_an_explicit_check(self) -> None:
        """Format paths selected from a base ref without applying staged-file restrictions."""
        with (
            patch.object(
                format_staged,
                "comparison_paths",
                return_value=["app/lib/main.dart"],
            ) as comparison_paths,
            patch.object(format_staged, "format_paths", return_value=0) as format_paths,
        ):
            result = format_staged.main(["--check", "--base-ref", "main"])

        self.assertEqual(result, 0)
        comparison_paths.assert_called_once_with(format_staged.REPOSITORY_ROOT, "main")
        format_paths.assert_called_once_with(
            format_staged.REPOSITORY_ROOT,
            ["app/lib/main.dart"],
            True,
        )

    def test_main_rejects_paths_and_a_base_ref_together(self) -> None:
        """Reject ambiguous selection between explicit paths and a base ref."""
        stderr = StringIO()
        with redirect_stderr(stderr):
            result = format_staged.main(
                ["--check", "--base-ref", "main", "--paths", "app/lib/main.dart"]
            )

        self.assertEqual(result, 1)
        self.assertIn("Use either --paths or --base-ref", stderr.getvalue())

    def test_main_rejects_a_base_ref_swallowed_into_paths_by_remainder(self) -> None:
        """Reject --base-ref even when --paths' REMAINDER capture swallows it first."""
        stderr = StringIO()
        with redirect_stderr(stderr):
            result = format_staged.main(
                ["--check", "--paths", "app/lib/main.dart", "--base-ref", "main"]
            )

        self.assertEqual(result, 1)
        self.assertIn("Use either --paths or --base-ref", stderr.getvalue())

    def test_main_rejects_an_attached_base_ref_swallowed_into_paths_by_remainder(
        self,
    ) -> None:
        """Reject the attached --base-ref=value spelling swallowed by REMAINDER too."""
        stderr = StringIO()
        with redirect_stderr(stderr):
            result = format_staged.main(
                ["--check", "--paths", "app/lib/main.dart", "--base-ref=main"]
            )

        self.assertEqual(result, 1)
        self.assertIn("Use either --paths or --base-ref", stderr.getvalue())

    def test_format_paths_reports_changes_without_restaging(self) -> None:
        """Leave formatted files for review instead of adding them to the index."""
        repository_root = Path(".")
        selected = ["app/lib/main.dart"]
        stderr = StringIO()
        with (
            patch.object(format_staged, "supported_paths", return_value=selected),
            patch.object(
                format_staged, "index_snapshot", side_effect=[b"same", b"same"]
            ),
            patch.object(format_staged, "execute_commands", return_value=0),
            patch.object(format_staged, "unstaged_paths", return_value=selected),
            patch.object(format_staged, "run_git") as run_git,
            redirect_stderr(stderr),
        ):
            result = format_staged.format_paths(repository_root, selected, check=False)

        self.assertEqual(result, 1)
        self.assertIn("app/lib/main.dart", stderr.getvalue())
        run_git.assert_not_called()

    def test_format_paths_succeeds_when_formatter_makes_no_changes(self) -> None:
        """Allow the commit to continue when selected files are already formatted."""
        repository_root = Path(".")
        selected = ["app/lib/main.dart"]
        with (
            patch.object(format_staged, "supported_paths", return_value=selected),
            patch.object(
                format_staged, "index_snapshot", side_effect=[b"same", b"same"]
            ),
            patch.object(format_staged, "execute_commands", return_value=0),
            patch.object(format_staged, "unstaged_paths", return_value=[]),
            patch.object(format_staged, "run_git") as run_git,
        ):
            result = format_staged.format_paths(repository_root, selected, check=False)

        self.assertEqual(result, 0)
        run_git.assert_not_called()


if __name__ == "__main__":
    unittest.main()
