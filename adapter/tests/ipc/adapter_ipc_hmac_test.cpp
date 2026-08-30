#include "ipc/adapter_ipc_hmac.hpp"

#include <catch2/catch_test_macros.hpp>

#include <array>
#include <cstddef>
#include <cstdint>
#include <vector>

using dovahlink::adapter::ipc::BuildHostProofMessage;
using dovahlink::adapter::ipc::ComputeIpcHmacSha256;
using dovahlink::adapter::ipc::ConstantTimeEqual;
using dovahlink::adapter::ipc::GenerateIpcChallenge;
using dovahlink::adapter::ipc::kIpcChallengeBytes;
using dovahlink::adapter::ipc::kIpcOwnerLifetimeIdBytes;

namespace {

///  Builds owned bytes from a sequence of `[start, start + count)` values,
///  the shared construction this file's known-answer vectors use.
std::vector<std::byte> ByteRange(int start, int count) {
  std::vector<std::byte> bytes(static_cast<std::size_t>(count));
  for (int index = 0; index < count; ++index) {
    bytes[static_cast<std::size_t>(index)] =
        static_cast<std::byte>(start + index);
  }
  return bytes;
}

} //  namespace

TEST_CASE("HMAC-SHA256 matches the shared known-answer vector",
          "[ipc][adapter_ipc_hmac]") {
  //  A fixed known-answer vector: key = bytes 0x00..0x1F, message =
  //  challenge(0x20..0x3F) || correlationId=1 (little-endian 8 bytes) ||
  //  adapterInstanceId(0x40..0x4F) || ownerLifetimeId(0x50..0x5B).
  std::vector<std::byte> key = ByteRange(0x00, 32);
  std::vector<std::byte> message;
  message.reserve(68);
  std::vector<std::byte> challenge = ByteRange(0x20, 32);
  message.insert(message.end(), challenge.begin(), challenge.end());
  message.insert(message.end(),
                 {std::byte{0x01}, std::byte{0x00}, std::byte{0x00},
                  std::byte{0x00}, std::byte{0x00}, std::byte{0x00},
                  std::byte{0x00}, std::byte{0x00}});
  std::vector<std::byte> adapterInstanceId = ByteRange(0x40, 16);
  message.insert(message.end(), adapterInstanceId.begin(),
                 adapterInstanceId.end());
  std::vector<std::byte> ownerLifetimeId = ByteRange(0x50, 12);
  message.insert(message.end(), ownerLifetimeId.begin(), ownerLifetimeId.end());
  REQUIRE(message.size() == 68);

  std::array<std::byte, 32> expected{
      std::byte{0x68}, std::byte{0x04}, std::byte{0x80}, std::byte{0xA0},
      std::byte{0x58}, std::byte{0xA6}, std::byte{0x19}, std::byte{0x03},
      std::byte{0x83}, std::byte{0x5D}, std::byte{0x6F}, std::byte{0x63},
      std::byte{0xA3}, std::byte{0xE0}, std::byte{0xE9}, std::byte{0x2E},
      std::byte{0x5E}, std::byte{0xF4}, std::byte{0xF3}, std::byte{0x6E},
      std::byte{0xDE}, std::byte{0x56}, std::byte{0xAA}, std::byte{0xD5},
      std::byte{0x67}, std::byte{0xBD}, std::byte{0xE6}, std::byte{0xB3},
      std::byte{0x05}, std::byte{0x05}, std::byte{0x9D}, std::byte{0xEB}};

  auto digest = ComputeIpcHmacSha256(key, message);

  CHECK(digest == expected);
}

TEST_CASE("HMAC-SHA256 of an empty message matches its known-answer vector",
          "[ipc][adapter_ipc_hmac]") {
  std::array<std::byte, 32> expected{
      std::byte{0xD3}, std::byte{0x8B}, std::byte{0x42}, std::byte{0x09},
      std::byte{0x6D}, std::byte{0x80}, std::byte{0xF4}, std::byte{0x5F},
      std::byte{0x82}, std::byte{0x6B}, std::byte{0x44}, std::byte{0xA9},
      std::byte{0xD5}, std::byte{0x60}, std::byte{0x7D}, std::byte{0xE7},
      std::byte{0x24}, std::byte{0x96}, std::byte{0xA4}, std::byte{0x15},
      std::byte{0xD3}, std::byte{0xF4}, std::byte{0xA1}, std::byte{0xA8},
      std::byte{0xC8}, std::byte{0x8E}, std::byte{0x3B}, std::byte{0xB9},
      std::byte{0xDA}, std::byte{0x8D}, std::byte{0xC1}, std::byte{0xCB}};

  auto digest = ComputeIpcHmacSha256(ByteRange(0, 32), {});

  CHECK(digest == expected);
}

TEST_CASE("HMAC-SHA256 of an empty key matches its known-answer vector",
          "[ipc][adapter_ipc_hmac]") {
  std::array<std::byte, 32> expected{
      std::byte{0x46}, std::byte{0xBD}, std::byte{0x32}, std::byte{0x06},
      std::byte{0x05}, std::byte{0xC5}, std::byte{0xA6}, std::byte{0xB6},
      std::byte{0x16}, std::byte{0x3A}, std::byte{0xB7}, std::byte{0x0B},
      std::byte{0xC6}, std::byte{0x34}, std::byte{0x5B}, std::byte{0x92},
      std::byte{0xA5}, std::byte{0xF9}, std::byte{0x08}, std::byte{0xE7},
      std::byte{0x9F}, std::byte{0xE5}, std::byte{0x89}, std::byte{0x79},
      std::byte{0xC2}, std::byte{0x3E}, std::byte{0xBB}, std::byte{0x47},
      std::byte{0xD1}, std::byte{0xA5}, std::byte{0xE3}, std::byte{0x07}};

  auto digest = ComputeIpcHmacSha256({}, ByteRange(0, 32));

  CHECK(digest == expected);
}

TEST_CASE("HMAC-SHA256 of an empty key and an empty message matches its "
          "known-answer vector",
          "[ipc][adapter_ipc_hmac]") {
  std::array<std::byte, 32> expected{
      std::byte{0xB6}, std::byte{0x13}, std::byte{0x67}, std::byte{0x9A},
      std::byte{0x08}, std::byte{0x14}, std::byte{0xD9}, std::byte{0xEC},
      std::byte{0x77}, std::byte{0x2F}, std::byte{0x95}, std::byte{0xD7},
      std::byte{0x78}, std::byte{0xC3}, std::byte{0x5F}, std::byte{0xC5},
      std::byte{0xFF}, std::byte{0x16}, std::byte{0x97}, std::byte{0xC4},
      std::byte{0x93}, std::byte{0x71}, std::byte{0x56}, std::byte{0x53},
      std::byte{0xC6}, std::byte{0xC7}, std::byte{0x12}, std::byte{0x14},
      std::byte{0x42}, std::byte{0x92}, std::byte{0xC5}, std::byte{0xAD}};

  auto digest = ComputeIpcHmacSha256({}, {});

  CHECK(digest == expected);
}

TEST_CASE("HMAC-SHA256 is idempotent for the same key and message",
          "[ipc][adapter_ipc_hmac]") {
  std::vector<std::byte> key = ByteRange(0, 32);
  std::vector<std::byte> message = ByteRange(100, 68);

  auto first = ComputeIpcHmacSha256(key, message);
  auto second = ComputeIpcHmacSha256(key, message);

  CHECK(first == second);
}

TEST_CASE("HMAC-SHA256 produces different digests for different keys or "
          "different messages",
          "[ipc][adapter_ipc_hmac]") {
  std::vector<std::byte> key = ByteRange(0, 32);
  std::vector<std::byte> message = ByteRange(100, 68);

  auto baseline = ComputeIpcHmacSha256(key, message);
  auto differentKey = ComputeIpcHmacSha256(ByteRange(1, 32), message);
  auto differentMessage = ComputeIpcHmacSha256(key, ByteRange(101, 68));

  CHECK(baseline != differentKey);
  CHECK(baseline != differentMessage);
}

TEST_CASE("BuildHostProofMessage matches the shared known-answer vector's "
          "field layout",
          "[ipc][adapter_ipc_hmac]") {
  std::array<std::byte, kIpcChallengeBytes> challenge{};
  for (std::size_t index = 0; index < challenge.size(); ++index) {
    challenge[index] = static_cast<std::byte>(0x20 + index);
  }
  std::array<std::byte, 16> adapterInstanceId{};
  for (std::size_t index = 0; index < adapterInstanceId.size(); ++index) {
    adapterInstanceId[index] = static_cast<std::byte>(0x40 + index);
  }
  std::array<std::byte, kIpcOwnerLifetimeIdBytes> ownerLifetimeId{};
  for (std::size_t index = 0; index < ownerLifetimeId.size(); ++index) {
    ownerLifetimeId[index] = static_cast<std::byte>(0x50 + index);
  }

  auto message =
      BuildHostProofMessage(challenge, 1, adapterInstanceId, ownerLifetimeId);
  auto digest = ComputeIpcHmacSha256(ByteRange(0x00, 32), message);

  std::array<std::byte, 32> expected{
      std::byte{0x68}, std::byte{0x04}, std::byte{0x80}, std::byte{0xA0},
      std::byte{0x58}, std::byte{0xA6}, std::byte{0x19}, std::byte{0x03},
      std::byte{0x83}, std::byte{0x5D}, std::byte{0x6F}, std::byte{0x63},
      std::byte{0xA3}, std::byte{0xE0}, std::byte{0xE9}, std::byte{0x2E},
      std::byte{0x5E}, std::byte{0xF4}, std::byte{0xF3}, std::byte{0x6E},
      std::byte{0xDE}, std::byte{0x56}, std::byte{0xAA}, std::byte{0xD5},
      std::byte{0x67}, std::byte{0xBD}, std::byte{0xE6}, std::byte{0xB3},
      std::byte{0x05}, std::byte{0x05}, std::byte{0x9D}, std::byte{0xEB}};

  CHECK(digest == expected);
}

TEST_CASE("ConstantTimeEqual compares content and rejects mismatched length "
          "or content",
          "[ipc][adapter_ipc_hmac]") {
  std::vector<std::byte> a = ByteRange(0, 32);
  std::vector<std::byte> sameContent = ByteRange(0, 32);
  std::vector<std::byte> differentContent = ByteRange(1, 32);
  std::vector<std::byte> shorter = ByteRange(0, 16);

  CHECK(ConstantTimeEqual(a, sameContent));
  CHECK_FALSE(ConstantTimeEqual(a, differentContent));
  CHECK_FALSE(ConstantTimeEqual(a, shorter));
  CHECK(ConstantTimeEqual({}, {}));
}

TEST_CASE("GenerateIpcChallenge produces different values across successive "
          "calls",
          "[ipc][adapter_ipc_hmac]") {
  auto first = GenerateIpcChallenge();
  auto second = GenerateIpcChallenge();

  CHECK(first != second);
}
