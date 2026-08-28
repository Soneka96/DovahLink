#include <catch2/catch_test_macros.hpp>

#include <fstream>
#include <initializer_list>
#include <sstream>
#include <string>
#include <string_view>
#include <utility>
#include <vector>

#include "test_support/source_text_test_support.hpp"

namespace {

///  Reads a source file for structural include-order assertions.
std::string ReadSource(const char* path) {
    std::ifstream file(path);
    REQUIRE(file.is_open());
    std::ostringstream buffer;
    buffer << file.rdbuf();
    return buffer.str();
}

///  Reports whether every required runtime include appears before all other
///  includes in the adapter source.
bool RuntimeHeadersComeFirst(
    const std::string& source,
    std::initializer_list<std::string_view> runtimeIncludes) {
    std::vector<std::pair<std::size_t, std::string_view>> includes;
    std::size_t linePosition = 0;
    while (linePosition < source.size()) {
        const std::size_t lineEnd = source.find('\n', linePosition);
        const std::size_t lineLength =
            (lineEnd == std::string::npos ? source.size() : lineEnd) -
            linePosition;
        const std::string_view line(source.data() + linePosition, lineLength);
        const std::size_t firstNonWhitespace = line.find_first_not_of(" \t");
        if (firstNonWhitespace != std::string_view::npos &&
            line.substr(firstNonWhitespace).starts_with("#include ")) {
            includes.emplace_back(linePosition, line.substr(firstNonWhitespace));
        }
        if (lineEnd == std::string::npos) {
            break;
        }
        linePosition = lineEnd + 1;
    }

    std::vector<std::size_t> runtimePositions;
    std::size_t firstOtherInclude = std::string::npos;
    for (const auto& [includePosition, line] : includes) {
        bool isRequiredRuntimeInclude = false;
        for (const std::string_view runtimeInclude : runtimeIncludes) {
            if (line.find(runtimeInclude) != std::string_view::npos) {
                isRequiredRuntimeInclude = true;
                runtimePositions.push_back(includePosition);
                break;
            }
        }
        if (!isRequiredRuntimeInclude) {
            firstOtherInclude = includePosition;
            break;
        }
    }
    if (runtimePositions.size() != runtimeIncludes.size()) {
        return false;
    }
    if (firstOtherInclude == std::string::npos) {
        return true;
    }
    for (const std::size_t runtimePosition : runtimePositions) {
        if (runtimePosition > firstOtherInclude) {
            return false;
        }
    }
    return true;
}

} //  namespace

///  Protects CommonLib runtime adapters from Windows macro collisions caused by
///  importing application or Boost headers before CommonLib's runtime headers.
TEST_CASE("CommonLib runtime adapters include runtime headers first",
          "[game_state][commonlib][includes]") {
    const std::string lifecycleHeader =
        ReadSource(DOVAHLINK_LIFECYCLE_SINK_HEADER_FILE);
    const std::string levelHeader =
        ReadSource(DOVAHLINK_LEVEL_INCREASE_SINK_HEADER_FILE);
    const std::string trustAdminSource =
        ReadSource(DOVAHLINK_TRUST_ADMIN_PAPYRUS_SOURCE_FILE);
    const std::string playerLevelSource =
        ReadSource(DOVAHLINK_PLAYER_LEVEL_ACCESSOR_SOURCE_FILE);
    const std::string gameBehaviorSource =
        ReadSource(DOVAHLINK_GAME_BEHAVIOR_COMPATIBILITY_SOURCE_FILE);
    const std::string levelSource =
        ReadSource(DOVAHLINK_LEVEL_INCREASE_SINK_SOURCE_FILE);
    const std::string pairingSource =
        ReadSource(DOVAHLINK_PAIRING_NOTIFICATION_SOURCE_FILE);
    const std::string publicationSource =
        ReadSource(DOVAHLINK_PUBLICATION_DIAGNOSTICS_SOURCE_FILE);
    const std::string captureQueueSource =
        ReadSource(DOVAHLINK_CAPTURE_QUEUE_DIAGNOSTICS_SOURCE_FILE);
    const std::string taskMarshallerSource =
        ReadSource(DOVAHLINK_TASK_MARSHALLER_SOURCE_FILE);

    const std::size_t lifecycleSkseInclude =
        lifecycleHeader.find("#include \"SKSE/SKSE.h\"");
    const std::size_t lifecycleApplicationInclude =
        lifecycleHeader.find("#include \"application/coordinator.hpp\"");
    const std::size_t levelReInclude =
        levelHeader.find("#include \"RE/Skyrim.h\"");
    const std::size_t levelApplicationInclude =
        levelHeader.find("#include \"application/contained_work.hpp\"");
    const std::size_t trustSkseInclude =
        trustAdminSource.find("#include \"SKSE/SKSE.h\"");
    const std::size_t trustReInclude =
        trustAdminSource.find("#include \"RE/Skyrim.h\"");
    const std::size_t trustAdapterInclude = trustAdminSource.find(
        "#include \"game_state/commonlib_trust_admin_papyrus_adapter.hpp\"");

    REQUIRE(lifecycleSkseInclude != std::string::npos);
    REQUIRE(lifecycleApplicationInclude != std::string::npos);
    REQUIRE(levelReInclude != std::string::npos);
    REQUIRE(levelApplicationInclude != std::string::npos);
    REQUIRE(trustSkseInclude != std::string::npos);
    REQUIRE(trustReInclude != std::string::npos);
    REQUIRE(trustAdapterInclude != std::string::npos);
    CHECK(lifecycleSkseInclude < lifecycleApplicationInclude);
    CHECK(levelReInclude < levelApplicationInclude);
    CHECK(trustSkseInclude < trustAdapterInclude);
    CHECK(trustReInclude < trustAdapterInclude);
    CHECK(RuntimeHeadersComeFirst(
        playerLevelSource, {"#include \"RE/Skyrim.h\""}));
    CHECK(RuntimeHeadersComeFirst(
        gameBehaviorSource,
        {"#include \"RE/Skyrim.h\"", "#include \"SKSE/SKSE.h\""}));
    CHECK(RuntimeHeadersComeFirst(
        levelSource, {"#include \"SKSE/SKSE.h\""}));
    CHECK(RuntimeHeadersComeFirst(
        pairingSource, {"#include \"RE/Skyrim.h\""}));
    CHECK(RuntimeHeadersComeFirst(
        publicationSource, {"#include \"SKSE/SKSE.h\""}));
    CHECK(RuntimeHeadersComeFirst(
        captureQueueSource, {"#include \"SKSE/SKSE.h\""}));
    CHECK(RuntimeHeadersComeFirst(
        taskMarshallerSource, {"#include \"SKSE/SKSE.h\""}));
}
