#pragma once

#include <cstdint>
#include <optional>
#include <vector>

namespace dovahlink::security {

/// Encrypts `plaintext` so only the current Windows user can decrypt it, using DPAPI
/// (`CryptProtectData`) in its default per-user scope -- the approved secure-storage primitive
/// recorded in `ai/context/protocol/security.md`'s "Persistent local trust" section. Never shows a
/// UI prompt; any condition that would otherwise require one fails closed instead.
/// @return The encrypted bytes, or `std::nullopt` on any DPAPI failure.
[[nodiscard]] std::optional<std::vector<std::uint8_t>> EncryptForCurrentUser(
    const std::vector<std::uint8_t>& plaintext);

/// Decrypts `ciphertext` produced by `EncryptForCurrentUser` for the current Windows user. Never
/// shows a UI prompt.
/// @return The original plaintext, or `std::nullopt` for tampered, truncated, or otherwise invalid
///     input.
[[nodiscard]] std::optional<std::vector<std::uint8_t>> DecryptForCurrentUser(
    const std::vector<std::uint8_t>& ciphertext);

}  // namespace dovahlink::security
