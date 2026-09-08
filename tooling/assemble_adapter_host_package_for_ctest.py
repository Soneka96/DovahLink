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
    parser.add_argument(
        "--configuration",
        choices=("Debug", "Release"),
        required=True,
        help="The adapter build configuration this invocation was run for, matching "
        "adapter/CMakeLists.txt's DOVAHLINK_HOST_BUILD_CONFIGURATION. Missing Release-named "
        "runtime DLLs skip only when this is Debug; the same gap in a Release configuration fails.",
    )
    return parser.parse_args(argv)


def main(argv: list[str]) -> int:
    """Assembles the real package, or skips a Debug build missing Release-named runtime DLLs.

    `AdapterHostPackager.assemble_package` requires its runtime dependency DLLs under their exact
    production names (`ADAPTER_RUNTIME_DLL_NAMES`), which only a Release-configuration adapter build
    produces -- a Debug build produces debug-suffixed names instead. Rather than weakening the real
    packager to accept those, a Debug adapter build missing those exact names simply skips this
    check, matching `SKIP_RETURN_CODE`'s configured meaning in `adapter/CMakeLists.txt` so the
    dependent package-layout test is skipped too, not failed. A Release configuration missing the
    same names is a genuine build problem, not an expected shape difference, so it fails instead of
    skipping: `--configuration` distinguishes the two rather than inferring it from DLL presence
    alone, which would let a broken Release build silently skip instead of failing.

    Args:
        argv: The argument list, excluding the program name.

    Returns:
        Zero on success, or `MISSING_RELEASE_RUNTIME_DLLS_SKIP_CODE` when a Debug build skips.

    Raises:
        FileNotFoundError: `--configuration Release` was given but a required runtime DLL is
            missing from `--adapter-build-dir`.
    """
    args = parse_args(argv)

    missing_dll_names = [
        dll_name
        for dll_name in ADAPTER_RUNTIME_DLL_NAMES
        if not (args.adapter_build_dir / dll_name).is_file()
    ]
    if missing_dll_names:
        if args.configuration == "Debug":
            print(
                "Skipping the real assembled-package test: "
                f"{args.adapter_build_dir} is missing {', '.join(missing_dll_names)} "
                "(a Release-configuration adapter build is required; a Debug build "
                "produces debug-suffixed runtime DLL names instead)."
            )
            return MISSING_RELEASE_RUNTIME_DLLS_SKIP_CODE
        raise FileNotFoundError(
            f"{args.adapter_build_dir} is missing {', '.join(missing_dll_names)} in a "
            "Release-configuration adapter build; this is a real build problem, not an "
            "expected Debug/Release naming difference."
        )

    packager = AdapterHostPackager(SubprocessProcessRunner())
    packager.assemble_package(
        adapter_build_dir=args.adapter_build_dir,
        host_publish_dir=args.host_publish_dir,
        package_dir=args.package_dir,
    )
    return 0


if __name__ == "__main__":
    sys.exit(main(sys.argv[1:]))
