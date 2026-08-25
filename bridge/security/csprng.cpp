#include "security/csprng.hpp"

#include "security/hex.hpp"

#include <windows.h>

#include <bcrypt.h>

#include <cassert>
#include <format>

namespace dovahlink::security {

std::optional<std::vector<std::uint8_t>> GenerateRandomBytes(std::size_t size) {
  std::vector<std::uint8_t> buffer(size);
  if (size == 0) {
    return buffer;
  }

  NTSTATUS status =
      BCryptGenRandom(nullptr, buffer.data(), static_cast<ULONG>(size),
                      BCRYPT_USE_SYSTEM_PREFERRED_RNG);
  if (!BCRYPT_SUCCESS(status)) {
    return std::nullopt;
  }
  return buffer;
}

std::optional<std::string> GenerateOpaqueId() {
  constexpr std::size_t kOpaqueIdBytes = 16;
  auto bytes = GenerateRandomBytes(kOpaqueIdBytes);
  if (!bytes.has_value()) {
    return std::nullopt;
  }
  return EncodeHex(*bytes);
}

std::optional<std::string> GenerateNumericCode(std::size_t digits) {
  assert(digits >= 1 && digits <= 9);

  auto bytes = GenerateRandomBytes(sizeof(std::uint32_t));
  if (!bytes.has_value()) {
    return std::nullopt;
  }
  std::uint32_t value = 0;
  for (auto byte : *bytes) {
    value = (value << 8) | byte;
  }

  // Known limitation: value % range is mildly non-uniform (low residues occur
  // one extra time each once range doesn't evenly divide 2^32) -- irrelevant at
  // this usage scale: current callers only request 5-6 digit administrative/UX
  // codes, brute-force-limited by throttling rather than relying on perfect
  // distribution. Revisit with rejection sampling if a caller ever needs a
  // uniformity guarantee.
  std::uint32_t range = 1;
  for (std::size_t i = 0; i < digits; ++i) {
    range *= 10;
  }
  return std::format("{:0{}}", value % range, digits);
}

} // namespace dovahlink::security
