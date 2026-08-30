#pragma once

#include "ipc/ipc_constants.hpp"

#include <array>
#include <cstddef>
#include <cstdint>
#include <span>

namespace dovahlink::adapter::ipc {

///  Computes the private handshake's keyed proof: `HMAC-SHA256(key, message)`,
///  via Windows CNG/BCrypt. Stateless and side-effect-free; used to compute
///  the adapter's expected `hostProof` for comparison against a received
///  `IpcHelloAckMessage`. This is not a general-purpose HMAC utility -- it
///  exists only for this one private-channel proof.
///  @param key The shared secret (the private channel's `peerProofToken`).
///  @param message The bytes to authenticate.
///  @return The full, untruncated 32-byte HMAC-SHA256 digest.
///  @throws std::runtime_error A CNG/BCrypt API call failed.
std::array<std::byte, kIpcHostProofBytes>
ComputeIpcHmacSha256(std::span<const std::byte> key,
                     std::span<const std::byte> message);

///  Builds the fixed message `hostProof` is computed over: `challenge ||
///  correlationId || adapterInstanceId || ownerLifetimeId`, in exactly this
///  field order, matching the wire's little-endian integer convention. Used
///  identically to compute the expected proof (adapter side) and, in a
///  future concept, the actual proof (host side, mirrored in C#).
std::array<std::byte, kIpcHostProofMessageBytes> BuildHostProofMessage(
    const std::array<std::byte, kIpcChallengeBytes> &challenge,
    std::uint64_t correlationId,
    const std::array<std::byte, 16> &adapterInstanceId,
    const std::array<std::byte, kIpcOwnerLifetimeIdBytes> &ownerLifetimeId);

///  Compares two byte spans in constant time with respect to their content
///  (though not their length), so comparing a computed proof against a
///  received one does not leak timing information about where they first
///  differ.
///  @return `true` if `a` and `b` have equal length and equal content.
bool ConstantTimeEqual(std::span<const std::byte> a,
                       std::span<const std::byte> b);

///  Generates a fresh, cryptographically random challenge via Windows
///  CNG/BCrypt, for the adapter to send in every Hello -- including every
///  retry -- so a captured `hostProof` can never be replayed against a later
///  handshake attempt.
///  @throws std::runtime_error A CNG/BCrypt API call failed.
std::array<std::byte, kIpcChallengeBytes> GenerateIpcChallenge();

} //  namespace dovahlink::adapter::ipc
