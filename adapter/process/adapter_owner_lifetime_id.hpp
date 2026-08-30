#pragma once

#include "ipc/ipc_constants.hpp"

#include <array>
#include <cstddef>
#include <optional>
#include <string>
#include <string_view>

namespace dovahlink::adapter::process {

///  Derives the current process's 12-byte owning-Skyrim-process lifetime
///  identity: a 4-byte little-endian process id, then an 8-byte
///  little-endian process creation timestamp (`FILETIME`). Both values are
///  trivially and deterministically re-derivable by any adapter instance
///  running inside the same Skyrim process -- including after an SKSE plugin
///  reload, since the owning OS process and its creation time do not change
///  -- and are guaranteed to differ from any other Skyrim process, including
///  one that later reuses the same process id (its creation time will
///  differ). This value scopes the rendezvous file, the shutdown-request
///  named event, and the handshake's lifetime check to the intended Skyrim
///  lifetime; it is not itself a cryptographic ownership proof, since it is
///  not secret.
///  @return The current process's 12-byte lifetime identity.
std::array<std::byte, ipc::kIpcOwnerLifetimeIdBytes> DeriveOwnerLifetimeId();

///  Formats a lifetime identity as 24 lowercase hex characters, for use in a
///  file name, a named kernel object name, or a process argument.
///  @param lifetimeId The 12-byte lifetime identity to format.
///  @return The formatted lowercase hex text.
std::string FormatOwnerLifetimeId(
    const std::array<std::byte, ipc::kIpcOwnerLifetimeIdBytes> &lifetimeId);

///  Parses a lifetime identity previously produced by `FormatOwnerLifetimeId`.
///  @param text The candidate hex text to parse.
///  @return The parsed 12-byte identity, or `std::nullopt` if `text` is not
///  exactly 24 lowercase hex characters.
std::optional<std::array<std::byte, ipc::kIpcOwnerLifetimeIdBytes>>
ParseOwnerLifetimeId(std::string_view text);

} //  namespace dovahlink::adapter::process
