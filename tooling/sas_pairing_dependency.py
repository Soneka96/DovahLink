"""CLI entry point: builds the pinned `sas-pairing` dependency of the dormant Host integration.

Run `python tooling/sas_pairing_dependency.py acquire --native` before restoring or testing the Host
projects that reference the `SasPairing` package. `--native` also builds the Windows x64 native
library the real-native Host tests load; omit it where only a restore is needed.
"""

from __future__ import annotations

import argparse
import sys
from pathlib import Path

from sas_pairing_acquirer import SasPairingAcquirer
from sas_pairing_command_runner import SubprocessCommandRunner
from sas_pairing_pin import load_pin

# The repository root, resolved from this script's own location.
REPOSITORY_ROOT = Path(__file__).resolve().parent.parent

# The committed pin of the one supported sas-pairing revision.
PIN_PATH = REPOSITORY_ROOT / "host" / "sas-pairing-dependency.json"


def parse_args(argv: list[str]) -> argparse.Namespace:
    """Parses this script's command-line arguments.

    Args:
        argv: The argument list, excluding the program name.

    Returns:
        The parsed arguments.
    """
    parser = argparse.ArgumentParser(
        description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter
    )
    commands = parser.add_subparsers(dest="command", required=True)
    acquire = commands.add_parser(
        "acquire", help="build and verify the pinned package (and native library)"
    )
    acquire.add_argument(
        "--native",
        action="store_true",
        help="also build and verify the Windows x64 native library",
    )
    return parser.parse_args(argv)


def main(argv: list[str]) -> int:
    """Acquires the pinned dependency and prints the acquisition record's path.

    Args:
        argv: The argument list, excluding the program name.

    Returns:
        The process exit code.
    """
    args = parse_args(argv)
    pin = load_pin(PIN_PATH)
    acquirer = SasPairingAcquirer(
        REPOSITORY_ROOT, pin, SubprocessCommandRunner(), sys.executable
    )
    record = acquirer.acquire(native=args.native)
    print(f"Wrote {record}")
    return 0


if __name__ == "__main__":
    sys.exit(main(sys.argv[1:]))
