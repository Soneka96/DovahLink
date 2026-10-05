"""Loads and validates the pinned `sas-pairing` dependency of the dormant Host integration."""

from __future__ import annotations

import json
import re
from dataclasses import dataclass
from pathlib import Path

# ---- Pin contract ----

# The only schema version of `host/sas-pairing-dependency.json` this tool understands.
PIN_SCHEMA_VERSION = 1

# The exact field set of a pin; any other or missing field fails closed.
PIN_FIELDS = frozenset(
    {
        "schema_version",
        "repository",
        "commit",
        "package_id",
        "package_version",
        "target_framework",
        "native_abi_version",
        "platform",
        "rust_target",
        "rust_toolchain",
    }
)

# A full, lowercase 40-hex-digit Git commit: never a branch, tag, short SHA, or "latest".
COMMIT_PATTERN = re.compile(r"[0-9a-f]{40}")

# An exact SemVer package version with an optional prerelease; no ranges or wildcards.
PACKAGE_VERSION_PATTERN = re.compile(r"[0-9]+\.[0-9]+\.[0-9]+(-[0-9A-Za-z.]+)?")

# An exact Rust release version; never a channel name such as "stable" or "nightly".
RUST_TOOLCHAIN_PATTERN = re.compile(r"[0-9]+\.[0-9]+\.[0-9]+")

# The values P10.3 supports; anything else is a different dependency, not this pin.
SUPPORTED_REPOSITORY = "https://github.com/Soneka96/sas-pairing.git"
SUPPORTED_PACKAGE_ID = "SasPairing"
SUPPORTED_TARGET_FRAMEWORK = "net10.0"
SUPPORTED_NATIVE_ABI_VERSION = 1
SUPPORTED_PLATFORM = "windows-x64"
SUPPORTED_RUST_TARGET = "x86_64-pc-windows-msvc"


@dataclass(frozen=True)
class SasPairingPin:
    """One exact, immutable `sas-pairing` revision and the artifacts expected from it."""

    # The HTTPS clone URL of the `sas-pairing` repository.
    repository: str
    # The full 40-hex-digit commit every artifact is built from.
    commit: str
    # The NuGet package ID built from that commit.
    package_id: str
    # The exact package version that commit packs.
    package_version: str
    # The package's only target framework.
    target_framework: str
    # The native ABI version the native library must report.
    native_abi_version: int
    # The one supported native platform.
    platform: str
    # The Rust target triple the native library must be built for.
    rust_target: str
    # The exact Rust release that builds the native library.
    rust_toolchain: str

    @property
    def nupkg_file_name(self) -> str:
        """The file name `dotnet pack` gives the package."""
        return f"{self.package_id}.{self.package_version}.nupkg"


def load_pin(path: Path) -> SasPairingPin:
    """Reads and strictly validates a pin file.

    Args:
        path: The pin file, normally `host/sas-pairing-dependency.json`.

    Returns:
        The validated pin.

    Raises:
        ValueError: The pin is malformed, incomplete, floating, or names an unsupported dependency.
    """
    data = json.loads(path.read_text(encoding="utf-8"))
    if not isinstance(data, dict) or set(data) != PIN_FIELDS:
        raise ValueError(
            f"{path}: the pin must have exactly the fields {sorted(PIN_FIELDS)}."
        )
    if data["schema_version"] != PIN_SCHEMA_VERSION:
        raise ValueError(
            f"{path}: unsupported pin schema version {data['schema_version']!r}."
        )

    pin = SasPairingPin(
        repository=_require_string(data, "repository"),
        commit=_require_string(data, "commit"),
        package_id=_require_string(data, "package_id"),
        package_version=_require_string(data, "package_version"),
        target_framework=_require_string(data, "target_framework"),
        native_abi_version=data["native_abi_version"],
        platform=_require_string(data, "platform"),
        rust_target=_require_string(data, "rust_target"),
        rust_toolchain=_require_string(data, "rust_toolchain"),
    )
    _require(
        pin.repository == SUPPORTED_REPOSITORY,
        path,
        "repository must be the sas-pairing HTTPS URL",
    )
    _require(
        COMMIT_PATTERN.fullmatch(pin.commit) is not None,
        path,
        "commit must be a full lowercase 40-hex SHA",
    )
    _require(
        pin.package_id == SUPPORTED_PACKAGE_ID, path, "package_id must be SasPairing"
    )
    _require(
        PACKAGE_VERSION_PATTERN.fullmatch(pin.package_version) is not None,
        path,
        "package_version must be one exact version",
    )
    _require(
        pin.target_framework == SUPPORTED_TARGET_FRAMEWORK,
        path,
        "target_framework must be net10.0",
    )
    _require(
        type(pin.native_abi_version) is int
        and pin.native_abi_version == SUPPORTED_NATIVE_ABI_VERSION,
        path,
        "native_abi_version must be 1",
    )
    _require(pin.platform == SUPPORTED_PLATFORM, path, "platform must be windows-x64")
    _require(
        pin.rust_target == SUPPORTED_RUST_TARGET,
        path,
        "rust_target must be x86_64-pc-windows-msvc",
    )
    _require(
        RUST_TOOLCHAIN_PATTERN.fullmatch(pin.rust_toolchain) is not None,
        path,
        "rust_toolchain must be one exact release",
    )
    return pin


def _require_string(data: dict[str, object], field: str) -> str:
    """Returns one field that must be a string.

    Raises:
        ValueError: The field is not a string.
    """
    value = data[field]
    if not isinstance(value, str):
        raise ValueError(f"The pin field {field!r} must be a string.")
    return value


def _require(condition: bool, path: Path, message: str) -> None:
    """Raises a pin validation error unless `condition` holds.

    Raises:
        ValueError: `condition` is false.
    """
    if not condition:
        raise ValueError(f"{path}: {message}.")
