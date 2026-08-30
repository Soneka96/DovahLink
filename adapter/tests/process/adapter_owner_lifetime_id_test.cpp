#include "process/adapter_owner_lifetime_id.hpp"

#include "test_support/source_text_test_support.hpp"

#include <catch2/catch_test_macros.hpp>

#ifndef NOMINMAX
#define NOMINMAX
#endif
#include <windows.h>

#include <array>
#include <cstddef>
#include <filesystem>
#include <string>

using dovahlink::adapter::ipc::kIpcOwnerLifetimeIdBytes;
using dovahlink::adapter::process::DeriveOwnerLifetimeId;
using dovahlink::adapter::process::FormatOwnerLifetimeId;
using dovahlink::adapter::process::ParseOwnerLifetimeId;

namespace {

///  A representative, fixed lifetime identity for tests that don't care
///  about a real derived value.
std::array<std::byte, kIpcOwnerLifetimeIdBytes> SampleLifetimeId() {
  std::array<std::byte, kIpcOwnerLifetimeIdBytes> id{};
  for (std::size_t index = 0; index < id.size(); ++index) {
    id[index] = static_cast<std::byte>(index + 1);
  }
  return id;
}

} //  namespace

TEST_CASE("DeriveOwnerLifetimeId returns the same value across successive "
          "calls in one process",
          "[process][adapter_owner_lifetime_id]") {
  auto first = DeriveOwnerLifetimeId();
  auto second = DeriveOwnerLifetimeId();

  CHECK(first == second);
}

TEST_CASE("FormatOwnerLifetimeId produces exactly 24 lowercase hex "
          "characters",
          "[process][adapter_owner_lifetime_id]") {
  std::string text = FormatOwnerLifetimeId(SampleLifetimeId());

  REQUIRE(text.size() == kIpcOwnerLifetimeIdBytes * 2);
  for (char character : text) {
    CHECK(((character >= '0' && character <= '9') ||
           (character >= 'a' && character <= 'f')));
  }
}

TEST_CASE("a formatted lifetime id round-trips through ParseOwnerLifetimeId",
          "[process][adapter_owner_lifetime_id]") {
  std::array<std::byte, kIpcOwnerLifetimeIdBytes> original = SampleLifetimeId();

  std::string text = FormatOwnerLifetimeId(original);
  auto parsed = ParseOwnerLifetimeId(text);

  REQUIRE(parsed.has_value());
  CHECK(*parsed == original);
}

TEST_CASE("an all-zero and an all-0xFF lifetime id each round-trip through "
          "format and parse",
          "[process][adapter_owner_lifetime_id]") {
  std::array<std::byte, kIpcOwnerLifetimeIdBytes> allZero{};
  std::array<std::byte, kIpcOwnerLifetimeIdBytes> allOnes{};
  allOnes.fill(std::byte{0xFF});

  auto parsedZero = ParseOwnerLifetimeId(FormatOwnerLifetimeId(allZero));
  auto parsedOnes = ParseOwnerLifetimeId(FormatOwnerLifetimeId(allOnes));

  REQUIRE(parsedZero.has_value());
  REQUIRE(parsedOnes.has_value());
  CHECK(*parsedZero == allZero);
  CHECK(*parsedOnes == allOnes);
}

TEST_CASE("DeriveOwnerLifetimeId's first 4 bytes are the current process id, "
          "little-endian",
          "[process][adapter_owner_lifetime_id]") {
  auto lifetimeId = DeriveOwnerLifetimeId();
  DWORD expectedProcessId = GetCurrentProcessId();

  std::uint32_t encodedProcessId = 0;
  for (int index = 3; index >= 0; --index) {
    encodedProcessId = (encodedProcessId << 8) |
                       std::to_integer<std::uint32_t>(
                           lifetimeId[static_cast<std::size_t>(index)]);
  }

  CHECK(encodedProcessId == static_cast<std::uint32_t>(expectedProcessId));
}

TEST_CASE("a derived lifetime id round-trips through format and parse",
          "[process][adapter_owner_lifetime_id]") {
  auto derived = DeriveOwnerLifetimeId();

  auto parsed = ParseOwnerLifetimeId(FormatOwnerLifetimeId(derived));

  REQUIRE(parsed.has_value());
  CHECK(*parsed == derived);
}

TEST_CASE("ParseOwnerLifetimeId rejects text of the wrong length",
          "[process][adapter_owner_lifetime_id]") {
  CHECK_FALSE(ParseOwnerLifetimeId("").has_value());
  CHECK_FALSE(ParseOwnerLifetimeId(std::string(23, '0')).has_value());
  CHECK_FALSE(ParseOwnerLifetimeId(std::string(25, '0')).has_value());
}

TEST_CASE("ParseOwnerLifetimeId rejects non-hex or uppercase characters",
          "[process][adapter_owner_lifetime_id]") {
  std::string uppercase(kIpcOwnerLifetimeIdBytes * 2, '0');
  uppercase[0] = 'A';
  CHECK_FALSE(ParseOwnerLifetimeId(uppercase).has_value());

  std::string nonHex(kIpcOwnerLifetimeIdBytes * 2, '0');
  nonHex[0] = 'g';
  CHECK_FALSE(ParseOwnerLifetimeId(nonHex).has_value());

  std::string punctuation(kIpcOwnerLifetimeIdBytes * 2, '0');
  punctuation[5] = '-';
  CHECK_FALSE(ParseOwnerLifetimeId(punctuation).has_value());
}

TEST_CASE("no adapter/process header includes a Skyrim or SKSE runtime "
          "header",
          "[process][adapter_owner_lifetime_id][structural]") {
  //  Structural pin, not a functional assertion: adapter/process/ is
  //  compiled into the CommonLib-free dovahlink_adapter_core target, per
  //  ai/context/adapter/architecture.md's "Technology boundary".
  std::filesystem::path processDir{DOVAHLINK_ADAPTER_PROCESS_DIR};
  REQUIRE(std::filesystem::exists(processDir));

  int headerCount = 0;
  for (const auto &entry : std::filesystem::directory_iterator(processDir)) {
    if (entry.path().extension() != ".hpp") {
      continue;
    }
    ++headerCount;

    std::string text =
        dovahlink::adapter::test_support::ReadSource(entry.path());

    INFO("checking " << entry.path().filename().string());
    CHECK(text.find("RE/") == std::string::npos);
    CHECK(text.find("SKSE/") == std::string::npos);
  }

  CHECK(headerCount > 0);
}
