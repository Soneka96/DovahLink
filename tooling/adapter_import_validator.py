"""Reads Adapter PE import tables without executing the Adapter binary."""

from __future__ import annotations

import struct
from pathlib import Path

# ---- Import policy ----

FORBIDDEN_ADAPTER_DEPENDENCIES = frozenset(
    {"fmt.dll", "spdlog.dll", "fmtd.dll", "spdlogd.dll"}
)

# ---- PE parsing ----


def find_forbidden_adapter_dependency(adapter_path: Path) -> str | None:
    """Returns the first forbidden imported DLL name, or ``None`` when imports are safe.

    Args:
        adapter_path: Path to the Adapter PE binary to inspect.

    Returns:
        The matching forbidden DLL name using the spelling found in the binary, or ``None``.

    Raises:
        OSError: The Adapter binary cannot be read.
        ValueError: The Adapter binary is not a supported, structurally valid PE file.
    """
    imported_names = read_imported_dll_names(adapter_path.read_bytes())
    forbidden_names = {name.casefold(): name for name in imported_names}
    for dependency_name in sorted(FORBIDDEN_ADAPTER_DEPENDENCIES):
        if dependency_name.casefold() in forbidden_names:
            return forbidden_names[dependency_name.casefold()]
    return None


def read_imported_dll_names(data: bytes) -> frozenset[str]:
    """Reads normal and delay-load imported DLL names from PE ``data``.

    Args:
        data: Complete PE file contents.

    Returns:
        The imported DLL names using the spelling stored in the PE tables.

    Raises:
        ValueError: The data is not a supported, structurally valid PE file.
    """
    if len(data) < 0x40 or data[:2] != b"MZ":
        raise ValueError("PE data is missing the DOS header")

    pe_offset = _read_u32(data, 0x3C, "DOS header PE offset")
    if data[pe_offset : pe_offset + 4] != b"PE\0\0":
        raise ValueError("PE data is missing the NT signature")

    coff_offset = pe_offset + 4
    section_count = _read_u16(data, coff_offset + 2, "COFF section count")
    optional_size = _read_u16(data, coff_offset + 16, "COFF optional-header size")
    optional_offset = coff_offset + 20
    optional_end = optional_offset + optional_size
    _require_range(data, optional_offset, optional_size, "optional header")

    magic = _read_u16(data, optional_offset, "optional-header magic")
    if magic == 0x20B:
        data_directory_offset = optional_offset + 112
        size_of_headers_offset = optional_offset + 60
        image_base = _read_u64(data, optional_offset + 24, "PE image base")
    elif magic == 0x10B:
        data_directory_offset = optional_offset + 96
        size_of_headers_offset = optional_offset + 60
        image_base = _read_u32(data, optional_offset + 28, "PE image base")
    else:
        raise ValueError(f"unsupported PE optional-header magic: 0x{magic:X}")

    if size_of_headers_offset + 4 > optional_end:
        raise ValueError("PE optional header is too small for its header size")
    if (
        data_directory_offset - 4 < optional_offset
        or data_directory_offset > optional_end
    ):
        raise ValueError("PE optional header is too small for its data-directory count")
    number_of_directories = _read_u32(
        data,
        data_directory_offset - 4,
        "optional-header data-directory count",
    )
    if number_of_directories > 0x1000:
        raise ValueError("PE data-directory count is unreasonable")
    directory_bytes = number_of_directories * 8
    _require_range(
        data,
        data_directory_offset,
        directory_bytes,
        "PE data directories",
    )
    if data_directory_offset + directory_bytes > optional_end:
        raise ValueError("PE data directories exceed the optional header")

    size_of_headers = _read_u32(data, size_of_headers_offset, "PE header size")
    section_offset = optional_end
    section_bytes = section_count * 40
    _require_range(data, section_offset, section_bytes, "PE section headers")
    sections = _read_sections(data, section_offset, section_count)

    imported_names: set[str] = set()
    for directory_index, descriptor_size, name_offset in (
        (1, 20, 12),  # IMAGE_DIRECTORY_ENTRY_IMPORT
        (13, 32, 4),  # IMAGE_DIRECTORY_ENTRY_DELAY_IMPORT
    ):
        if number_of_directories <= directory_index:
            continue
        directory_offset = data_directory_offset + directory_index * 8
        directory_rva = _read_u32(data, directory_offset, "import directory RVA")
        directory_size = _read_u32(data, directory_offset + 4, "import directory size")
        if directory_rva == 0 or directory_size == 0:
            continue
        imported_names.update(
            _read_import_directory(
                data,
                sections,
                size_of_headers,
                directory_rva,
                directory_size,
                descriptor_size,
                name_offset,
                image_base,
            )
        )

    return frozenset(imported_names)


def _read_sections(
    data: bytes, section_offset: int, section_count: int
) -> tuple[tuple[int, int, int, int], ...]:
    """Reads PE section RVA spans and their raw file offsets."""
    sections: list[tuple[int, int, int, int]] = []
    for index in range(section_count):
        offset = section_offset + index * 40
        virtual_size = _read_u32(data, offset + 8, "section virtual size")
        virtual_address = _read_u32(data, offset + 12, "section virtual address")
        raw_size = _read_u32(data, offset + 16, "section raw size")
        raw_pointer = _read_u32(data, offset + 20, "section raw pointer")
        sections.append(
            (virtual_address, max(virtual_size, raw_size), raw_size, raw_pointer)
        )
    return tuple(sections)


def _read_import_directory(
    data: bytes,
    sections: tuple[tuple[int, int, int, int], ...],
    size_of_headers: int,
    directory_rva: int,
    directory_size: int,
    descriptor_size: int,
    name_offset: int,
    image_base: int,
) -> set[str]:
    """Reads one normal or delay-load PE import directory."""
    directory_file_offset = _rva_to_file_offset(
        data, sections, size_of_headers, directory_rva
    )
    directory_end = directory_file_offset + directory_size
    _require_range(data, directory_file_offset, directory_size, "import directory")

    names: set[str] = set()
    descriptor_offset = directory_file_offset
    while descriptor_offset + descriptor_size <= directory_end:
        descriptor = data[descriptor_offset : descriptor_offset + descriptor_size]
        if not any(descriptor):
            return names
        name_value = struct.unpack_from("<I", descriptor, name_offset)[0]
        if name_value == 0:
            raise ValueError("import descriptor has no DLL name")
        if descriptor_size == 32 and not (struct.unpack_from("<I", descriptor)[0] & 1):
            if name_value < image_base:
                raise ValueError("delay-load DLL name VA precedes the PE image")
            name_rva = name_value - image_base
        else:
            name_rva = name_value
        name_file_offset = _rva_to_file_offset(
            data, sections, size_of_headers, name_rva
        )
        names.add(_read_c_string(data, name_file_offset, "imported DLL name"))
        descriptor_offset += descriptor_size

    raise ValueError("import directory has no terminating descriptor")


def _rva_to_file_offset(
    data: bytes,
    sections: tuple[tuple[int, int, int, int], ...],
    size_of_headers: int,
    rva: int,
) -> int:
    """Maps a PE RVA to a checked raw file offset."""
    if rva < size_of_headers and rva < len(data):
        return rva
    for virtual_address, span, raw_size, raw_pointer in sections:
        if virtual_address <= rva < virtual_address + span:
            raw_offset = rva - virtual_address
            if raw_offset >= raw_size:
                raise ValueError(
                    f"PE RVA 0x{rva:X} falls in a virtual-only section tail"
                )
            file_offset = raw_pointer + raw_offset
            if file_offset < len(data):
                return file_offset
            break
    raise ValueError(f"PE RVA 0x{rva:X} does not map to file data")


def _read_c_string(data: bytes, offset: int, description: str) -> str:
    """Reads a null-terminated ASCII string from a checked file offset."""
    terminator = data.find(b"\0", offset)
    if terminator < 0:
        raise ValueError(f"{description} is not null-terminated")
    try:
        return data[offset:terminator].decode("ascii")
    except UnicodeDecodeError as error:
        raise ValueError(f"{description} is not ASCII") from error


def _read_u16(data: bytes, offset: int, description: str) -> int:
    """Reads a little-endian 16-bit value from ``data``."""
    _require_range(data, offset, 2, description)
    return struct.unpack_from("<H", data, offset)[0]


def _read_u32(data: bytes, offset: int, description: str) -> int:
    """Reads a little-endian 32-bit value from ``data``."""
    _require_range(data, offset, 4, description)
    return struct.unpack_from("<I", data, offset)[0]


def _read_u64(data: bytes, offset: int, description: str) -> int:
    """Reads a little-endian 64-bit value from ``data``."""
    _require_range(data, offset, 8, description)
    return struct.unpack_from("<Q", data, offset)[0]


def _require_range(data: bytes, offset: int, size: int, description: str) -> None:
    """Raises ``ValueError`` when a requested byte range is outside ``data``."""
    if offset < 0 or size < 0 or offset > len(data) - size:
        raise ValueError(f"PE {description} is truncated")
