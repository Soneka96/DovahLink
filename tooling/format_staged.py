"""Format staged repository code without touching unrelated working-tree files."""

from __future__ import annotations

import argparse
import json
import shutil
import subprocess
import sys
from collections import defaultdict
from pathlib import Path


REPOSITORY_ROOT = Path(__file__).resolve().parent.parent
SUPPORTED_SUFFIXES = {
    ".cc": "cpp",
    ".cpp": "cpp",
    ".cs": "csharp",
    ".dart": "dart",
    ".h": "cpp",
    ".hpp": "cpp",
    ".ps1": "powershell",
    ".py": "python",
}


def parse_git_paths(output: bytes) -> list[str]:
    """Decode NUL-delimited repository-relative paths from Git output."""
    return [
        path
        for path in output.decode("utf-8", errors="surrogateescape").split("\0")
        if path
    ]


def run_git(
    repository_root: Path, arguments: list[str]
) -> subprocess.CompletedProcess[bytes]:
    """Run a Git command in the repository and return its completed process."""
    return subprocess.run(
        ["git", *arguments],
        cwd=repository_root,
        check=False,
        stdout=subprocess.PIPE,
        stderr=subprocess.PIPE,
    )


def staged_paths(repository_root: Path) -> list[str]:
    """Return added, copied, modified, and renamed paths currently in the index."""
    result = run_git(
        repository_root,
        ["diff", "--cached", "--name-only", "-z", "--diff-filter=ACMR"],
    )
    if result.returncode != 0:
        raise RuntimeError(result.stderr.decode("utf-8").strip())
    return parse_git_paths(result.stdout)


def unstaged_paths(repository_root: Path) -> list[str]:
    """Return tracked paths with worktree changes outside the index."""
    result = run_git(
        repository_root,
        ["diff", "--name-only", "-z", "--diff-filter=ACMR"],
    )
    if result.returncode != 0:
        raise RuntimeError(result.stderr.decode("utf-8").strip())
    return parse_git_paths(result.stdout)


def partial_staged_paths(staged: list[str], unstaged: list[str]) -> list[str]:
    """Return paths that have both staged and unstaged tracked changes."""
    return sorted(set(staged).intersection(unstaged))


def formatter_group(path: str) -> str | None:
    """Return the formatter group for a supported path, or `None` when unsupported."""
    return SUPPORTED_SUFFIXES.get(Path(path).suffix.lower())


def is_ignored(repository_root: Path, path: str) -> bool:
    """Return whether a path matches repository ignore rules."""
    result = run_git(
        repository_root,
        ["check-ignore", "--no-index", "--quiet", "--", path],
    )
    if result.returncode == 0:
        return True
    if result.returncode == 1:
        return False
    raise RuntimeError(result.stderr.decode("utf-8").strip())


def supported_paths(repository_root: Path, paths: list[str]) -> list[str]:
    """Filter ignored, missing, and unsupported paths while preserving input order."""
    selected: list[str] = []
    for path in paths:
        if formatter_group(path) is None or is_ignored(repository_root, path):
            continue
        if not (repository_root / path).is_file():
            continue
        selected.append(path)
    return selected


def nearest_csharp_project(repository_root: Path, path: str) -> Path:
    """Find the nearest C# project owning a source path."""
    source = (repository_root / path).resolve()
    current = source.parent
    while True:
        projects = sorted(current.glob("*.csproj"))
        if projects:
            return projects[0]
        if current == repository_root:
            break
        current = current.parent
    raise RuntimeError(f"No .csproj found for staged C# file: {path}")


def powershell_command(paths: list[str], check: bool) -> list[str]:
    """Build the PowerShell formatter command for selected scripts."""
    executable = shutil.which("pwsh") or shutil.which("powershell")
    if executable is None:
        raise RuntimeError("Required formatter is unavailable: pwsh or powershell")
    check_literal = "$true" if check else "$false"
    paths_literal = json.dumps(paths, ensure_ascii=False)
    script = f"""
$check = {check_literal}
$paths = ConvertFrom-Json @'
{paths_literal}
'@
if (-not (Get-Command Invoke-Formatter -ErrorAction SilentlyContinue)) {{
    Write-Error 'PSScriptAnalyzer Invoke-Formatter is required.'
    exit 127
}}
foreach ($path in $paths) {{
    $fullPath = [IO.Path]::GetFullPath((Join-Path (Get-Location) $path))
    $original = [IO.File]::ReadAllText($fullPath)
    $formatted = Invoke-Formatter -ScriptDefinition $original
    if ($check -and $formatted -ne $original) {{
        Write-Error "PowerShell formatting required: $path"
        exit 1
    }}
    if (-not $check) {{
        [IO.File]::WriteAllText($fullPath, $formatted, [Text.UTF8Encoding]::new($false))
    }}
}}
"""
    return [executable, "-NoProfile", "-NonInteractive", "-Command", script]


def formatter_commands(
    repository_root: Path,
    paths: list[str],
    check: bool,
) -> list[list[str]]:
    """Build formatter commands for supported paths without executing them."""
    grouped: dict[str, list[str]] = defaultdict(list)
    for path in paths:
        group = formatter_group(path)
        if group is not None:
            grouped[group].append(path)

    commands: list[list[str]] = []
    if grouped["dart"]:
        commands.append(
            [
                "dart",
                "format",
                *(["--output=none", "--set-exit-if-changed"] if check else []),
                *grouped["dart"],
            ]
        )
    if grouped["cpp"]:
        commands.append(
            [
                "clang-format",
                *(["--dry-run", "--Werror"] if check else ["-i"]),
                *grouped["cpp"],
            ]
        )
    if grouped["python"]:
        commands.append(
            [
                "ruff",
                "format",
                *(["--check"] if check else []),
                *grouped["python"],
            ]
        )
    if grouped["powershell"]:
        commands.append(powershell_command(grouped["powershell"], check))
    if grouped["csharp"]:
        by_project: dict[Path, list[str]] = defaultdict(list)
        for path in grouped["csharp"]:
            project = nearest_csharp_project(repository_root, path)
            by_project[project].append(path)
        for project, project_paths in sorted(by_project.items()):
            commands.append(
                [
                    "dotnet",
                    "format",
                    "whitespace",
                    str(project),
                    "--no-restore",
                    *(["--verify-no-changes"] if check else []),
                    "--include",
                    *[
                        str(
                            (repository_root / path)
                            .resolve()
                            .relative_to(project.parent)
                        )
                        for path in project_paths
                    ],
                ]
            )
    return commands


def execute_commands(
    repository_root: Path,
    commands: list[list[str]],
) -> int:
    """Run formatter commands and return the first non-zero exit code."""
    missing = sorted(
        {command[0] for command in commands if shutil.which(command[0]) is None}
    )
    if missing:
        print(
            "Required formatter(s) unavailable: " + ", ".join(missing),
            file=sys.stderr,
        )
        return 127
    for command in commands:
        try:
            result = subprocess.run(command, cwd=repository_root, check=False)
        except OSError as error:
            print(str(error), file=sys.stderr)
            return 127
        if result.returncode != 0:
            return result.returncode
    return 0


def index_snapshot(repository_root: Path) -> bytes:
    """Capture the staged index diff so concurrent index changes can be rejected."""
    result = run_git(repository_root, ["diff", "--cached", "--binary"])
    if result.returncode != 0:
        raise RuntimeError(
            result.stderr.decode("utf-8", errors="surrogateescape").strip()
        )
    return result.stdout


def format_paths(
    repository_root: Path,
    paths: list[str],
    check: bool,
) -> int:
    """Format selected paths and require review before they can be committed."""
    selected = supported_paths(repository_root, paths)
    if not selected:
        return 0
    before_index = index_snapshot(repository_root) if not check else None
    result = execute_commands(
        repository_root, formatter_commands(repository_root, selected, check)
    )
    if result != 0 or check:
        return result
    if index_snapshot(repository_root) != before_index:
        print(
            "The Git index changed while formatting; review and stage manually.",
            file=sys.stderr,
        )
        return 1
    changed = sorted(set(selected).intersection(unstaged_paths(repository_root)))
    if changed:
        print(
            "Formatting changed files; review and stage them before retrying:\n"
            + "\n".join(f"  {path}" for path in changed),
            file=sys.stderr,
        )
        return 1
    return 0


def parse_arguments(arguments: list[str] | None) -> argparse.Namespace:
    """Parse command-line options for staged or explicit-path formatting."""
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument(
        "--check", action="store_true", help="fail when formatting is needed"
    )
    parser.add_argument(
        "--paths", nargs="*", help="format these repository-relative paths"
    )
    return parser.parse_args(arguments)


def main(arguments: list[str] | None = None) -> int:
    """Run the staged formatter hook or explicit-path check."""
    options = parse_arguments(arguments)
    repository_root = REPOSITORY_ROOT
    try:
        paths = (
            options.paths
            if options.paths is not None
            else staged_paths(repository_root)
        )
        if options.paths is None:
            partial = supported_paths(
                repository_root,
                partial_staged_paths(paths, unstaged_paths(repository_root)),
            )
            if partial:
                print(
                    "Partially staged supported files are not allowed:\n"
                    + "\n".join(f"  {path}" for path in partial),
                    file=sys.stderr,
                )
                return 1
        return format_paths(repository_root, paths, options.check)
    except RuntimeError as error:
        print(str(error), file=sys.stderr)
        return 1


if __name__ == "__main__":
    raise SystemExit(main())
