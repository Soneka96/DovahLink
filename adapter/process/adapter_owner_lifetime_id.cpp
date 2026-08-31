#include "process/adapter_owner_lifetime_id.hpp"

#ifndef NOMINMAX
#define NOMINMAX
#endif
#include <windows.h>

#include <span>

namespace dovahlink::adapter::process {

namespace {

///  Writes a 32-bit value into exactly 4 bytes, least-significant byte first,
///  matching the private IPC codec's own little-endian convention.
void WriteUInt32LittleEndian(std::span<std::byte, 4> destination,
                             std::uint32_t value) {
  for (int index = 0; index < 4; ++index) {
    destination[static_cast<std::size_t>(index)] =
        static_cast<std::byte>((value >> (8 * index)) & 0xFF);
  }
}

///  Writes a 64-bit value into exactly 8 bytes, least-significant byte first,
///  matching the private IPC codec's own little-endian convention.
void WriteUInt64LittleEndian(std::span<std::byte, 8> destination,
                             std::uint64_t value) {
  for (int index = 0; index < 8; ++index) {
    destination[static_cast<std::size_t>(index)] =
        static_cast<std::byte>((value >> (8 * index)) & 0xFF);
  }
}

///  The value of a single lowercase hex digit, or `std::nullopt` if `digit`
///  is not one.
std::optional<std::uint8_t> LowercaseHexDigitValue(char digit) {
  if (digit >= '0' && digit <= '9') {
    return static_cast<std::uint8_t>(digit - '0');
  }
  if (digit >= 'a' && digit <= 'f') {
    return static_cast<std::uint8_t>(digit - 'a' + 10);
  }
  return std::nullopt;
}

} //  namespace

std::optional<std::array<std::byte, ipc::kIpcOwnerLifetimeIdBytes>>
DeriveOwnerLifetimeId() {
  DWORD processId = GetCurrentProcessId();

  FILETIME creationTime{};
  FILETIME exitTime{};
  FILETIME kernelTime{};
  FILETIME userTime{};
  if (!GetProcessTimes(GetCurrentProcess(), &creationTime, &exitTime,
                       &kernelTime, &userTime)) {
    return std::nullopt;
  }
  const std::uint64_t creationTimeValue =
      (static_cast<std::uint64_t>(creationTime.dwHighDateTime) << 32) |
      creationTime.dwLowDateTime;

  std::array<std::byte, ipc::kIpcOwnerLifetimeIdBytes> lifetimeId{};
  WriteUInt32LittleEndian(std::span<std::byte, 4>(lifetimeId.data(), 4),
                          static_cast<std::uint32_t>(processId));
  WriteUInt64LittleEndian(std::span<std::byte, 8>(lifetimeId.data() + 4, 8),
                          creationTimeValue);
  return lifetimeId;
}

std::string FormatOwnerLifetimeId(
    const std::array<std::byte, ipc::kIpcOwnerLifetimeIdBytes> &lifetimeId) {
  static constexpr char kHexDigits[] = "0123456789abcdef";
  std::string text;
  text.reserve(lifetimeId.size() * 2);
  for (std::byte value : lifetimeId) {
    const auto byteValue = std::to_integer<std::uint8_t>(value);
    text.push_back(kHexDigits[byteValue >> 4]);
    text.push_back(kHexDigits[byteValue & 0x0F]);
  }
  return text;
}

std::optional<std::array<std::byte, ipc::kIpcOwnerLifetimeIdBytes>>
ParseOwnerLifetimeId(std::string_view text) {
  constexpr std::size_t kExpectedLength = ipc::kIpcOwnerLifetimeIdBytes * 2;
  if (text.size() != kExpectedLength) {
    return std::nullopt;
  }

  std::array<std::byte, ipc::kIpcOwnerLifetimeIdBytes> lifetimeId{};
  for (std::size_t index = 0; index < lifetimeId.size(); ++index) {
    std::optional<std::uint8_t> high = LowercaseHexDigitValue(text[index * 2]);
    std::optional<std::uint8_t> low =
        LowercaseHexDigitValue(text[index * 2 + 1]);
    if (!high.has_value() || !low.has_value()) {
      return std::nullopt;
    }
    lifetimeId[index] = static_cast<std::byte>((*high << 4) | *low);
  }
  return lifetimeId;
}

} //  namespace dovahlink::adapter::process
