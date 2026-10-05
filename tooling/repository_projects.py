"""Lists the .NET projects DovahLink owns, for repository-wide restore and checks.

Repository tooling discovers DovahLink-owned source projects only: `*.csproj` files tracked by Git
outside the generated output roots. Acquired dependency sources (such as the pinned `sas-pairing`
checkout under `out/sas-pairing/source/`) and build output carry project files of their own, but
they are never DovahLink projects and must never be restored, built, or formatted as if they were.
"""

from __future__ import annotations

import argparse
import subprocess
import sys
from pathlib import Path

# The repository root, resolved from this script's own location.
REPOSITORY_ROOT = Path(__file__).resolve().parent.parent

# Generated, ignored output roots: nothing under them is DovahLink-owned source, even if a file
# there were ever force-added to the index.
GENERATED_ROOTS = ("out/", "tooling/out/")


def owned_projects(repository_root: Path) -> list[str]:
    """Returns the repository-relative paths of every DovahLink-owned `*.csproj`.

    Args:
        repository_root: The root of the DovahLink Git checkout.

    Returns:
        The tracked project paths outside every generated output root, sorted, with `/`
        separators.

    Raises:
        RuntimeError: Git could not list the tracked files.
    """
    result = subprocess.run(
        ["git", "ls-files", "-z", "--cached", "--", "*.csproj"],
        cwd=repository_root,
        check=False,
        stdout=subprocess.PIPE,
        stderr=subprocess.PIPE,
    )
    if result.returncode != 0:
        raise RuntimeError(result.stderr.decode("utf-8").strip())
    paths = [
        path
        for path in result.stdout.decode("utf-8", errors="surrogateescape").split("\0")
        if path
    ]
    return sorted(
        path
        for path in paths
        if not any(path.startswith(root) for root in GENERATED_ROOTS)
    )


def parse_args(argv: list[str]) -> argparse.Namespace:
    """Parses this script's command-line arguments.

    Args:
        argv: The argument list, excluding the program name.

    Returns:
        The parsed arguments.
    """
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument(
        "-z",
        dest="null_terminated",
        action="store_true",
        help="terminate each path with NUL instead of a newline",
    )
    return parser.parse_args(argv)


def main(argv: list[str]) -> int:
    """Prints every DovahLink-owned project path.

    Args:
        argv: The argument list, excluding the program name.

    Returns:
        The process exit code.
    """
    args = parse_args(argv)
    terminator = "\0" if args.null_terminated else "\n"
    for path in owned_projects(REPOSITORY_ROOT):
        sys.stdout.write(path + terminator)
    return 0


if __name__ == "__main__":
    sys.exit(main(sys.argv[1:]))
