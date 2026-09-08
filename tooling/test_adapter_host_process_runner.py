"""Tests for adapter_host_process_runner.py."""

from __future__ import annotations

import subprocess
import sys
import unittest

from adapter_host_process_runner import SubprocessProcessRunner


class SubprocessProcessRunnerTests(unittest.TestCase):
    """Tests for SubprocessProcessRunner, using the real subprocess module."""

    def test_run_executes_the_given_command(self) -> None:
        """Verifies a successful command actually runs, using this interpreter as the target."""
        runner = SubprocessProcessRunner()

        runner.run([sys.executable, "-c", "pass"])

    def test_run_raises_when_the_command_exits_nonzero(self) -> None:
        """Verifies a failing command's nonzero exit surfaces as CalledProcessError."""
        runner = SubprocessProcessRunner()

        with self.assertRaises(subprocess.CalledProcessError):
            runner.run([sys.executable, "-c", "import sys; sys.exit(1)"])


if __name__ == "__main__":
    unittest.main()
