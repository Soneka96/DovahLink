"""Defines DovahLink Builder's single ownership invariant for destructively managed output roots.

An output root DovahLink Builder may destructively clean or replace files under must be provable as
Builder-owned: a location under the repository's own `tooling/out` default, or a custom folder that
is new or empty (safely adopted) or already carries this module's ownership marker from a previous
run. An existing, non-empty custom folder without that marker is refused before any destructive
operation, mirroring `BuildOutputOwnershipGuard`'s identical rule for the C# WPF Builder GUI -- the
two implementations cannot share code across the language boundary and must be kept in sync by hand.
"""

from __future__ import annotations

from pathlib import Path
from typing import Protocol

# ---- Ownership marker ----

MARKER_FILE_NAME = ".dovahlink-builder-output"
MARKER_CONTENTS = (
    "This folder is managed by DovahLink Builder. Do not delete this file.\n"
)


class IBuildOutputOwnershipGuard(Protocol):
    """Verifies or safely establishes DovahLink Builder's ownership of an output root."""

    def ensure_owned(self, output_root: Path, repository_root: Path) -> None:
        """Verifies `output_root` is safe to destructively manage, adopting it if needed.

        Args:
            output_root: The resolved output root a build is about to write to or clean.
            repository_root: The repository root the build targets, used to recognize the
                default `tooling/out` location.

        Raises:
            RuntimeError: `output_root` is a custom folder that already has unrelated content
                and no valid Builder-ownership marker, or the marker could not be created
                because the location could not be accessed.
        """
        ...


class BuildOutputOwnershipGuard:
    """Verifies or safely establishes DovahLink Builder's ownership of an output root."""

    def ensure_owned(self, output_root: Path, repository_root: Path) -> None:
        """Verifies `output_root` is safe to destructively manage, adopting it if needed.

        Args:
            output_root: The resolved output root a build is about to write to or clean.
            repository_root: The repository root the build targets, used to recognize the
                default `tooling/out` location.

        Raises:
            RuntimeError: `output_root` is a custom folder that already has unrelated content
                and no valid Builder-ownership marker, or the marker could not be created
                because the location could not be accessed.
        """
        try:
            normalized_root = output_root.resolve()
            normalized_default_root = (repository_root / "tooling" / "out").resolve()
            if (
                normalized_root == normalized_default_root
                or normalized_default_root in normalized_root.parents
            ):
                return

            marker_path = normalized_root / MARKER_FILE_NAME
            if marker_path.is_file():
                return

            if normalized_root.exists() and any(normalized_root.iterdir()):
                raise RuntimeError(
                    f"'{normalized_root}' already contains files and has never been used as a "
                    "DovahLink Builder output folder. Choose an empty or previously-used output "
                    "folder to avoid deleting unrelated content."
                )

            normalized_root.mkdir(parents=True, exist_ok=True)
            marker_path.write_text(MARKER_CONTENTS, encoding="utf-8")
        except (OSError, ValueError) as exception:
            # ValueError covers a malformed path Python itself rejects before ever reaching the
            # OS (for example an embedded null character), which mkdir raises as ValueError
            # rather than OSError on this platform.
            raise RuntimeError(
                f"Could not create or mark the build output location: {output_root}"
            ) from exception
