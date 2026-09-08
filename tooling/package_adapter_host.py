"""CLI entry point: publishes the Host and builds the Vortex-installable Adapter+Host package.

See `adapter_host_packager.py` for the publishing strategy and package layout this orchestrates.
"""

from __future__ import annotations

import argparse
import sys
from pathlib import Path

from adapter_host_packager import AdapterHostPackager
from adapter_host_process_runner import SubprocessProcessRunner

REPOSITORY_ROOT = Path(__file__).resolve().parent.parent
HOST_PROJECT_PATH = (
    REPOSITORY_ROOT / "host" / "DovahLink.Host" / "DovahLink.Host.csproj"
)
VERSION_PATH = REPOSITORY_ROOT / "VERSION"


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
    return parser.parse_args(argv)


def main(argv: list[str]) -> int:
    """Publishes the Host, assembles the Adapter+Host package, and zips it.

    Args:
        argv: The argument list, excluding the program name.

    Returns:
        Zero on success.
    """
    args = parse_args(argv)

    packager = AdapterHostPackager(SubprocessProcessRunner())
    host_publish_dir = args.output_dir / "publish"
    packager.publish_host(HOST_PROJECT_PATH, host_publish_dir)

    package_dir = args.output_dir / "package"
    packager.assemble_package(
        adapter_build_dir=args.adapter_build_dir,
        host_publish_dir=host_publish_dir,
        package_dir=package_dir,
        console_admin_pex=args.console_admin_pex,
        console_admin_yaml=args.console_admin_yaml,
    )

    version = read_product_version(VERSION_PATH)
    archive_path = packager.zip_package(
        package_dir, args.output_dir / f"DovahLink-Adapter-{version}"
    )
    print(f"Wrote {archive_path}")
    return 0


if __name__ == "__main__":
    sys.exit(main(sys.argv[1:]))
