"""Runs an external process for the Adapter+Host packaging tool."""

from __future__ import annotations

import subprocess
from typing import Protocol


class IProcessRunner(Protocol):
    """Runs an external process to completion."""

    def run(self, args: list[str]) -> None:
        """Runs `args` to completion.

        Args:
            args: The full command line, as separate argv elements.

        Raises:
            subprocess.CalledProcessError: The process exited with a nonzero status.
        """
        ...


class SubprocessProcessRunner:
    """Runs a process through the real `subprocess` module."""

    def run(self, args: list[str]) -> None:
        """Runs `args` to completion via `subprocess.run`.

        Args:
            args: The full command line, as separate argv elements.

        Raises:
            subprocess.CalledProcessError: The process exited with a nonzero status.
        """
        subprocess.run(args, check=True)
