"""CTest fixture entry point: assembles the real Vortex package for the adapter's package-layout E2E.

Wraps `AdapterHostPackager.assemble_package` (see `adapter_host_packager.py`) unmodified, reusing an
already-published self-contained Host rather than re-publishing one. Exists only to give
`adapter/CMakeLists.txt`'s `AssembleRealAdapterHostPackage` CTest fixture a small, skip-aware
entry point, distinct from `package_adapter_host.py`, which remains the unmodified real release CLI.
"""

from __future__ import annotations

import argparse
import sys
from pathlib import Path

from adapter_host_packager import ADAPTER_RUNTIME_DLL_NAMES, AdapterHostPackager
from adapter_host_process_runner import SubprocessProcessRunner

# The exit code CTest's SKIP_RETURN_CODE test property (set in adapter/CMakeLists.txt) recognizes
# as "this test was skipped", not failed.
MISSING_RELEASE_RUNTIME_DLLS_SKIP_CODE = 125


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
        help="Directory containing the built adapter plugin DLL and its runtime dependency DLLs.",
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
    """Assembles the real package, or skips when the adapter build is not a Release configuration.

    `AdapterHostPackager.assemble_package` requires its runtime dependency DLLs under their exact
    production names (`ADAPTER_RUNTIME_DLL_NAMES`), which only a Release-configuration adapter build
    produces -- a Debug build produces debug-suffixed names instead. Rather than weakening the real
    packager to accept those, a Debug adapter build simply skips this check, matching
    `SKIP_RETURN_CODE`'s configured meaning in `adapter/CMakeLists.txt` so the dependent package-layout
    test is skipped too, not failed.

    Args:
        argv: The argument list, excluding the program name.

    Returns:
        Zero on success, or `MISSING_RELEASE_RUNTIME_DLLS_SKIP_CODE` when skipped.
    """
    args = parse_args(argv)

    missing_dll_names = [
        dll_name
        for dll_name in ADAPTER_RUNTIME_DLL_NAMES
        if not (args.adapter_build_dir / dll_name).is_file()
    ]
    if missing_dll_names:
        print(
            "Skipping the real assembled-package test: "
            f"{args.adapter_build_dir} is missing {', '.join(missing_dll_names)} "
            "(a Release-configuration adapter build is required; a Debug build "
            "produces debug-suffixed runtime DLL names instead)."
        )
        return MISSING_RELEASE_RUNTIME_DLLS_SKIP_CODE

    packager = AdapterHostPackager(SubprocessProcessRunner())
    packager.assemble_package(
        adapter_build_dir=args.adapter_build_dir,
        host_publish_dir=args.host_publish_dir,
        package_dir=args.package_dir,
    )
    return 0


if __name__ == "__main__":
    sys.exit(main(sys.argv[1:]))
