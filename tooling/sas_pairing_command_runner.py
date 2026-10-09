"""Runs the external commands that fetch, build, and verify the pinned `sas-pairing` dependency."""

from __future__ import annotations

import subprocess
from pathlib import Path
from typing import Protocol


class ICommandRunner(Protocol):
    """Runs one external command to completion."""

    def run(self, args: list[str], cwd: Path) -> str:
        """Runs `args` in `cwd` and returns its standard output.

        Args:
            args: The full command line, as separate argv elements.
            cwd: The working directory.

        Returns:
            The command's standard output.

        Raises:
            subprocess.CalledProcessError: The command exited with a nonzero status.
        """
        ...


class SubprocessCommandRunner:
    """Runs commands through the real `subprocess` module, echoing their output."""

    def run(self, args: list[str], cwd: Path) -> str:
        """Runs `args` in `cwd` via `subprocess.run` without a shell.

        Args:
            args: The full command line, as separate argv elements.
            cwd: The working directory.

        Returns:
            The command's standard output.

        Raises:
            subprocess.CalledProcessError: The command exited with a nonzero status.
        """
        completed = subprocess.run(
            args,
            cwd=cwd,
            check=False,
            capture_output=True,
            text=True,
            encoding="utf-8",
            errors="replace",
        )
        print(completed.stdout, end="")
        print(completed.stderr, end="")
        completed.check_returncode()
        return completed.stdout
