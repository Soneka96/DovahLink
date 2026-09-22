"""Tests for the dependency-free Adapter PE import validator."""

from __future__ import annotations

import struct
import tempfile
import unittest
from pathlib import Path

from adapter_import_validator import (
    find_forbidden_adapter_dependency,
    read_imported_dll_names,
)


class AdapterImportValidatorTests(unittest.TestCase):
    """Verifies normal, delay-load, and malformed PE import handling."""

    def test_reads_normal_import_names(self) -> None:
        """Reads every DLL name from the normal import directory."""
        data = _build_pe(["kernel32.dll", "user32.dll"])

        self.assertEqual(
            read_imported_dll_names(data),
            {"kernel32.dll", "user32.dll"},
        )

    def test_reads_delay_load_import_names(self) -> None:
        """Reads DLL names from the delay-load import directory as well."""
        data = _build_pe([], delay_names=["delayload.dll"])

        self.assertEqual(read_imported_dll_names(data), {"delayload.dll"})

    def test_finds_each_forbidden_dependency_case_insensitively(self) -> None:
        """Returns each forbidden dependency using its spelling from the PE table."""
        with tempfile.TemporaryDirectory() as temp_dir:
            for dependency_name in ("fmt.dll", "spdlog.dll", "fmtd.dll", "spdlogd.dll"):
                adapter_path = Path(temp_dir) / dependency_name
                adapter_path.write_bytes(_build_pe([dependency_name.upper()]))

                self.assertEqual(
                    find_forbidden_adapter_dependency(adapter_path),
                    dependency_name.upper(),
                )

    def test_returns_none_when_imports_are_allowed(self) -> None:
        """Accepts a valid Adapter whose imports do not include forbidden DLLs."""
        with tempfile.TemporaryDirectory() as temp_dir:
            adapter_path = Path(temp_dir) / "adapter.dll"
            adapter_path.write_bytes(_build_pe(["kernel32.dll"]))

            self.assertIsNone(find_forbidden_adapter_dependency(adapter_path))

    def test_accepts_a_valid_pe_with_no_import_directory(self) -> None:
        """Treats a valid PE without import directories as having no imports."""
        self.assertEqual(read_imported_dll_names(_build_pe([])), frozenset())

    def test_rejects_malformed_or_truncated_pe_data(self) -> None:
        """Rejects missing headers, unsupported formats, and truncated tables."""
        with self.assertRaises(ValueError):
            read_imported_dll_names(b"not a PE")

        truncated = _build_pe(["kernel32.dll"])[:0x220]
        with self.assertRaises(ValueError):
            read_imported_dll_names(truncated)

        unsupported = bytearray(_build_pe([]))
        struct.pack_into("<H", unsupported, 0x98, 0x123)
        with self.assertRaises(ValueError):
            read_imported_dll_names(bytes(unsupported))

        with self.assertRaises(ValueError):
            read_imported_dll_names(_build_pe([""]))

        short_optional_header = bytearray(_build_pe([]))
        struct.pack_into("<H", short_optional_header, 0x94, 0x40)
        with self.assertRaises(ValueError):
            read_imported_dll_names(bytes(short_optional_header))

    def test_rejects_an_import_directory_that_does_not_map_to_file_data(self) -> None:
        """Rejects an import-directory RVA outside every PE section."""
        data = bytearray(_build_pe(["kernel32.dll"]))
        optional_offset = 0x98
        import_directory_offset = optional_offset + 112 + 8
        struct.pack_into("<I", data, import_directory_offset, 0x9000)

        with self.assertRaises(ValueError):
            read_imported_dll_names(bytes(data))

    def test_rejects_an_import_directory_in_a_virtual_only_section_tail(self) -> None:
        """Rejects an import-directory RVA with no corresponding raw file bytes."""
        data = bytearray(_build_pe(["kernel32.dll"]))
        section_offset = 0x188
        struct.pack_into("<I", data, section_offset + 8, 0x800)
        import_directory_offset = 0x98 + 112 + 8
        struct.pack_into("<I", data, import_directory_offset, 0x1400)

        with self.assertRaises(ValueError):
            read_imported_dll_names(bytes(data))


def _build_pe(import_names: list[str], delay_names: list[str] | None = None) -> bytes:
    """Builds a minimal PE32+ fixture with normal and optional delay-load imports."""
    if delay_names is None:
        delay_names = []

    data = bytearray(0x800)
    data[:2] = b"MZ"
    struct.pack_into("<I", data, 0x3C, 0x80)
    data[0x80:0x84] = b"PE\0\0"

    coff_offset = 0x84
    struct.pack_into("<H", data, coff_offset, 0x8664)
    struct.pack_into("<H", data, coff_offset + 2, 1)
    struct.pack_into("<H", data, coff_offset + 16, 0xF0)

    optional_offset = 0x98
    struct.pack_into("<H", data, optional_offset, 0x20B)
    struct.pack_into("<I", data, optional_offset + 60, 0x200)
    struct.pack_into("<I", data, optional_offset + 108, 16)

    section_offset = optional_offset + 0xF0
    data[section_offset : section_offset + 6] = b".idata"
    struct.pack_into("<I", data, section_offset + 8, 0x400)
    struct.pack_into("<I", data, section_offset + 12, 0x1000)
    struct.pack_into("<I", data, section_offset + 16, 0x400)
    struct.pack_into("<I", data, section_offset + 20, 0x200)

    _write_import_directory(
        data,
        optional_offset + 112 + 8,
        0x1000,
        0x200,
        import_names,
        descriptor_size=20,
        name_offset=12,
    )
    _write_import_directory(
        data,
        optional_offset + 112 + 13 * 8,
        0x1080,
        0x280,
        delay_names,
        descriptor_size=32,
        name_offset=4,
    )
    return bytes(data)


def _write_import_directory(
    data: bytearray,
    directory_offset: int,
    directory_rva: int,
    directory_file_offset: int,
    names: list[str],
    *,
    descriptor_size: int,
    name_offset: int,
) -> None:
    """Writes one minimal normal or delay-load import directory fixture."""
    if not names:
        return

    struct.pack_into(
        "<II", data, directory_offset, directory_rva, (len(names) + 1) * descriptor_size
    )
    for index, name in enumerate(names):
        descriptor_offset = directory_file_offset + index * descriptor_size
        name_file_offset = directory_file_offset + 0x40 + index * 0x20
        name_rva = 0x1000 + name_file_offset - 0x200
        if descriptor_size == 32:
            struct.pack_into("<I", data, descriptor_offset, 1)
        struct.pack_into("<I", data, descriptor_offset + name_offset, name_rva)
        encoded_name = name.encode("ascii") + b"\0"
        data[name_file_offset : name_file_offset + len(encoded_name)] = encoded_name
