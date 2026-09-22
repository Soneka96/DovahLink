#include "enums.hpp"
#include "test_support/source_text_test_support.hpp"

#include <catch2/catch_test_macros.hpp>

#include <charconv>
#include <cstdint>
#include <string>
#include <string_view>

using dovahlink::adapter::capture::CharacterEventKey;
using dovahlink::adapter::capture::CharacterSampleToken;
using dovahlink::adapter::test_support::ReadSource;

//  back to its own tests/capture/live_state_catalog_fixture_test.cpp in the
namespace {

///  Reads one integer field from the checked-in host/adapter live-state
///  capture catalog fixture, the same way
///  `adapter_ipc_connection_test.cpp`'s `ReadPrivateIpcLimit` reads the
///  private-IPC rate-limit fixture.
std::uint32_t ReadLiveStateToken(std::string_view key) {
    const std::string source = ReadSource(DOVAHLINK_LIVE_STATE_CATALOG_FIXTURE);
    const std::string marker = "\"" + std::string(key) + "\"";
    const std::size_t keyPosition = source.find(marker);
    REQUIRE(keyPosition != std::string::npos);
    const std::size_t colon = source.find(':', keyPosition + marker.size());
    REQUIRE(colon != std::string::npos);
    const std::size_t valuePosition =
        source.find_first_not_of(" \t\r\n", colon + 1);
    REQUIRE(valuePosition != std::string::npos);

    std::uint32_t value = 0;
    const auto [end, error] = std::from_chars(
        source.data() + valuePosition, source.data() + source.size(), value);
    REQUIRE(error == std::errc{});
    REQUIRE(end != source.data() + valuePosition);
    return value;
}

} //  namespace

TEST_CASE("Adapter live-state capture enums match the shared catalog fixture",
          "[capture][live-state]") {
    CHECK(static_cast<std::uint32_t>(CharacterSampleToken::kCharacterVitals) ==
          ReadLiveStateToken("characterVitals"));
    CHECK(static_cast<std::uint32_t>(CharacterSampleToken::kCharacterXp) ==
          ReadLiveStateToken("characterXp"));
    CHECK(static_cast<std::uint32_t>(
              CharacterSampleToken::kCharacterLevelBaseline) ==
          ReadLiveStateToken("characterLevelBaseline"));
    CHECK(static_cast<std::uint32_t>(
              CharacterEventKey::kCharacterLevelChanged) ==
          ReadLiveStateToken("characterLevelChanged"));
}
