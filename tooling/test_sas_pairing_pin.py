"""Tests for sas_pairing_pin.py and the committed sas-pairing dependency pin."""

from __future__ import annotations

import json
import tempfile
import unittest
from pathlib import Path

from sas_pairing_pin import PIN_FIELDS, load_pin

# The committed pin the dormant Host integration builds against.
COMMITTED_PIN = (
    Path(__file__).resolve().parent.parent / "host" / "sas-pairing-dependency.json"
)


def _valid_pin() -> dict[str, object]:
    """Returns a representative valid pin."""
    return {
        "schema_version": 1,
        "repository": "https://github.com/Soneka96/sas-pairing.git",
        "commit": "b938016d1de53d3e269fe0840485fbd3dc715fd7",
        "package_id": "SasPairing",
        "package_version": "0.1.0-dev.1",
        "target_framework": "net10.0",
        "native_abi_version": 1,
        "platform": "windows-x64",
        "rust_target": "x86_64-pc-windows-msvc",
        "rust_toolchain": "1.99.0",
    }


class LoadPinTests(unittest.TestCase):
    """Tests for load_pin."""

    def _load(self, pin: object):
        """Writes `pin` to a temporary file and loads it."""
        with tempfile.TemporaryDirectory() as temp_dir:
            path = Path(temp_dir) / "pin.json"
            path.write_text(json.dumps(pin), encoding="utf-8")
            return load_pin(path)

    def test_valid_pin_loads_every_field(self) -> None:
        """Verifies a complete pin loads with its exact values and package file name."""
        pin = self._load(_valid_pin())

        self.assertEqual(pin.commit, "b938016d1de53d3e269fe0840485fbd3dc715fd7")
        self.assertEqual(pin.rust_toolchain, "1.99.0")
        self.assertEqual(pin.nupkg_file_name, "SasPairing.0.1.0-dev.1.nupkg")

    def test_committed_pin_is_valid_and_exact(self) -> None:
        """Verifies the committed pin passes validation and pins one full commit."""
        pin = load_pin(COMMITTED_PIN)

        self.assertRegex(pin.commit, r"^[0-9a-f]{40}$")
        self.assertEqual(pin.package_id, "SasPairing")
        self.assertEqual(pin.native_abi_version, 1)

    def test_floating_or_short_commit_is_rejected(self) -> None:
        """Verifies branch names, tags, "latest", short SHAs, and uppercase SHAs never pass as a commit."""
        for commit in (
            "main",
            "latest",
            "HEAD",
            "v0.1.0",
            "b938016",
            "B938016D1DE53D3E269FE0840485FBD3DC715FD7",
            "",
        ):
            with self.subTest(commit=commit), self.assertRaises(ValueError):
                self._load({**_valid_pin(), "commit": commit})

    def test_floating_versions_and_toolchains_are_rejected(self) -> None:
        """Verifies version ranges, wildcards, and Rust channel names are rejected."""
        for field, value in (
            ("package_version", "latest"),
            ("package_version", "0.1.*"),
            ("package_version", "[0.1.0,)"),
            ("rust_toolchain", "stable"),
            ("rust_toolchain", "nightly"),
            ("rust_toolchain", "1.99"),
        ):
            with self.subTest(field=field, value=value), self.assertRaises(ValueError):
                self._load({**_valid_pin(), field: value})

    def test_unsupported_dependency_identity_is_rejected(self) -> None:
        """Verifies another repository, package, framework, ABI, platform, or target is a different dependency."""
        for field, value in (
            ("repository", "https://github.com/someone-else/sas-pairing.git"),
            ("repository", "../sas-pairing"),
            ("repository", "C:\\Projects\\sas-pairing"),
            ("package_id", "SasPairing.Fork"),
            ("target_framework", "net9.0"),
            ("native_abi_version", 2),
            ("native_abi_version", True),
            ("platform", "linux-x64"),
            ("rust_target", "aarch64-pc-windows-msvc"),
        ):
            with self.subTest(field=field, value=value), self.assertRaises(ValueError):
                self._load({**_valid_pin(), field: value})

    def test_missing_extra_or_mistyped_fields_are_rejected(self) -> None:
        """Verifies the field set is exact and string fields must be strings."""
        missing = _valid_pin()
        del missing["rust_toolchain"]
        for pin in (
            missing,
            {**_valid_pin(), "extra": 1},
            {**_valid_pin(), "commit": 1},
            [],
            {**_valid_pin(), "schema_version": 2},
        ):
            with self.subTest(pin=pin), self.assertRaises(ValueError):
                self._load(pin)

    def test_malformed_json_is_rejected(self) -> None:
        """Verifies a pin file that is not JSON fails as a validation error."""
        with tempfile.TemporaryDirectory() as temp_dir:
            path = Path(temp_dir) / "pin.json"
            path.write_text("{ not json", encoding="utf-8")

            with self.assertRaises(ValueError):
                load_pin(path)

    def test_field_set_is_the_documented_contract(self) -> None:
        """Verifies the representative pin uses exactly the contract's field set."""
        self.assertEqual(set(_valid_pin()), PIN_FIELDS)


if __name__ == "__main__":
    unittest.main()
