#pragma once

#include "ipc/ipc_constants.hpp"

#include <array>
#include <cstddef>
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

} //  namespace dovahlink::adapter::ipc
