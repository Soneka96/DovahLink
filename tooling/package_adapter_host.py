"""CLI entry point: publishes the Host and builds the Vortex-installable Adapter+Host package.

See `adapter_host_packager.py` for the publishing strategy and package layout this orchestrates.
"""

from __future__ import annotations

import argparse
import sys
from pathlib import Path

from adapter_host_packager import AdapterHostPackager
from adapter_host_process_runner import SubprocessProcessRunner
from build_output_ownership import BuildOutputOwnershipGuard

REPOSITORY_ROOT = Path(__file__).resolve().parent.parent
HOST_PROJECT_PATH = (
    REPOSITORY_ROOT / "host" / "DovahLink.Host" / "DovahLink.Host.csproj"
)
VERSION_PATH = REPOSITORY_ROOT / "VERSION"

# ---- Stage progress markers ----

# DovahLinkBuilder's coordinator scans stdout for lines of this shape to report structured
# packaging progress (BuildStageProgressParser); this is the only place that cross-language
# contract is defined, mirroring the "Wrote " archive-path contract below.
STAGE_HOST_PUBLISH = "host_publish"
STAGE_PACKAGE_ASSEMBLY = "package_assembly"
STAGE_PACKAGE_VALIDATION = "package_validation"
STAGE_ARCHIVE = "archive"


def read_product_version(version_path: Path) -> str:
    """Reads the published product version from the repository-root VERSION file.

    Args:
        version_path: Path to the repository-root `VERSION` file.

    Returns:
        The version string, with surrounding whitespace stripped.
    """
    return version_path.read_text(encoding="utf-8").strip()


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
        "--output-dir",
        type=Path,
        required=True,
        help="Directory the published Host, assembled package, and zip archive are written under.",
    )
    parser.add_argument(
        "--console-admin-pex",
        type=Path,
        default=None,
        help="Path to an already-compiled DovahLinkAdmin.pex, omitted if not supplied.",
    )
    parser.add_argument(
        "--console-admin-yaml",
        type=Path,
        default=None,
        help="Path to dovahlink.yaml, omitted if not supplied.",
    )
    parser.add_argument(
        "--configuration",
        default="Release",
        help="The dotnet publish --configuration value for the Host, matching the selected build profile.",
    )
    parser.add_argument(
        "--profile-label",
        default="release",
        help="Lowercase build profile name (debug/beta/release), appended to the archive name for every "
        "profile except release, so different profiles' output never overwrites each other's archive.",
    )
    return parser.parse_args(argv)


def main(argv: list[str]) -> int:
    """Publishes the Host, assembles the Adapter+Host package, validates it, and zips it.

    Args:
        argv: The argument list, excluding the program name.

    Returns:
        Zero on success.
    """
    args = parse_args(argv)

    # Verified before any destructive or write work starts, against the one root this CLI actually
    # owns: publish_host below already writes into args.output_dir, so checking ownership any later
    # (for example only once assemble_package eventually touches its package/ subfolder) would let a
    # direct run against an arbitrary, unrelated folder write into it first. Mirrors the check
    # AdapterHostBuildCoordinator (the C# Builder GUI's caller of this script) already performs on
    # the same output_dir before ever invoking this script, so direct CLI use gets the same
    # protection.
    BuildOutputOwnershipGuard().ensure_owned(args.output_dir, REPOSITORY_ROOT)

    packager = AdapterHostPackager(SubprocessProcessRunner())
    host_publish_dir = args.output_dir / "publish"
    _print_stage(STAGE_HOST_PUBLISH, "start")
    packager.publish_host(
        HOST_PROJECT_PATH, host_publish_dir, configuration=args.configuration
    )
    _print_stage(STAGE_HOST_PUBLISH, "done")

    package_dir = args.output_dir / "package"
    _print_stage(STAGE_PACKAGE_ASSEMBLY, "start")
    packager.assemble_package(
        adapter_build_dir=args.adapter_build_dir,
        host_publish_dir=host_publish_dir,
        package_dir=package_dir,
        console_admin_pex=args.console_admin_pex,
        console_admin_yaml=args.console_admin_yaml,
    )
    _print_stage(STAGE_PACKAGE_ASSEMBLY, "done")

    _print_stage(STAGE_PACKAGE_VALIDATION, "start")
    packager.validate_package(
        package_dir,
        console_admin_pex=args.console_admin_pex,
        console_admin_yaml=args.console_admin_yaml,
    )
    _print_stage(STAGE_PACKAGE_VALIDATION, "done")

    version = read_product_version(VERSION_PATH)
    profile_suffix = "" if args.profile_label == "release" else f"-{args.profile_label}"
    _print_stage(STAGE_ARCHIVE, "start")
    archive_path = packager.zip_package(
        package_dir, args.output_dir / f"DovahLink-Adapter-{version}{profile_suffix}"
    )
    _print_stage(STAGE_ARCHIVE, "done")
    # Unlike the stage markers above, DovahLinkBuilder reads this line from the process's
    # accumulated output only after it exits, not live -- so it needs no explicit flush; Python
    # flushes stdout on normal interpreter exit regardless.
    print(f"Wrote {archive_path}")
    return 0


def _print_stage(name: str, status: str) -> None:
    """Prints a `##stage <name> <status>` progress marker for DovahLinkBuilder to parse.

    Flushed explicitly: DovahLinkBuilder launches this script with stdout redirected to a pipe,
    where Python's stdout is fully buffered rather than line-buffered, and later stages (`publish_host`
    in particular) run `dotnet` with stdout inherited straight through. Without an explicit flush a
    marker can sit in this process's buffer while a later stage's own output already reached
    DovahLinkBuilder, making live stage transitions and durations arrive late or out of order.
    """
    print(f"##stage {name} {status}", flush=True)


if __name__ == "__main__":
    sys.exit(main(sys.argv[1:]))
