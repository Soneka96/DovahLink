"""CTest fixture entry point: assembles the real Vortex package for the adapter's package-layout E2E.

Wraps `AdapterHostPackager.assemble_package` (see `adapter_host_packager.py`) unmodified, reusing an
already-published self-contained Host rather than re-publishing one. Exists only to give
`adapter/CMakeLists.txt`'s `AssembleRealAdapterHostPackage` CTest fixture a small
entry point, distinct from `package_adapter_host.py`, which remains the unmodified real release CLI.
"""

from __future__ import annotations

import argparse
import sys
from pathlib import Path

from adapter_host_packager import AdapterHostPackager
from adapter_host_process_runner import SubprocessProcessRunner
from build_output_ownership import BuildOutputOwnershipGuard

REPOSITORY_ROOT = Path(__file__).resolve().parent.parent


def parse_args(argv: list[str]) -> argparse.Namespace:
    """Parses this script's command-line arguments.

    Args:
        argv: The argument list, excluding the program name.

    Returns:
        The parsed arguments.
    """
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument(
        "--adapter-build-dir",
        type=Path,
        required=True,
        help="Directory containing the built adapter plugin DLL.",
    )
    parser.add_argument(
        "--host-publish-dir",
        type=Path,
        required=True,
        help="Directory an already-completed self-contained Host publish wrote the executable into.",
    )
    parser.add_argument(
        "--package-dir",
        type=Path,
        required=True,
        help="Directory the assembled Data/ layout is written under.",
    )
    return parser.parse_args(argv)


def main(argv: list[str]) -> int:
    """Assembles the real package for either native build configuration.

    Args:
        argv: The argument list, excluding the program name.

    Returns:
        Zero after successful assembly.

    Raises:
        FileNotFoundError: A required Adapter or Host input is missing.
    """
    args = parse_args(argv)

    # Verified before assemble_package ever touches args.package_dir: this fixture has no separate
    # output-dir/package split like package_adapter_host.py's real release CLI -- args.package_dir
    # is the one root this entry point owns, so it is what gets checked and marked directly.
    guard = BuildOutputOwnershipGuard()
    guard.ensure_owned(args.package_dir, REPOSITORY_ROOT)

    packager = AdapterHostPackager(SubprocessProcessRunner())
    packager.assemble_package(
        adapter_build_dir=args.adapter_build_dir,
        host_publish_dir=args.host_publish_dir,
        package_dir=args.package_dir,
    )

    # Unlike package_adapter_host.py's real release CLI, where the marker lives in a separate
    # output_dir that assemble_package never touches, this fixture's marked root and the
    # directory assemble_package just wiped-and-rebuilt are the same path -- so the mark ensure_owned
    # planted above was destroyed along with everything else package_dir held. Replanting it now
    # lets a second run against the same package_dir (an ordinary incremental CTest rebuild, not a
    # clean one) still be recognized as owned instead of refused as an unrelated non-empty folder.
    #
    # Known limitation: assemble_package validates every source file before it wipes package_dir,
    # so a missing source leaves a prior run's own content and marker untouched (this call is
    # simply never reached). A copy failing after that wipe (for example a disk-space or
    # permission error, not a missing source) is the one window this can't cover: package_dir is
    # left wiped and partially rebuilt with no marker, and the next run refuses it as unrelated
    # non-empty content until it is manually cleared. Accepted because closing it would mean
    # replanting the marker inside assemble_package itself, right after its own wipe -- reintroducing
    # the ownership-awareness this redesign moved out of it -- for a narrow, easily-recovered failure
    # in a CI build-tree fixture that a clean rebuild clears anyway.
    guard.mark_owned(args.package_dir)
    return 0


if __name__ == "__main__":
    sys.exit(main(sys.argv[1:]))
