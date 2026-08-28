#include <catch2/catch_test_macros.hpp>

#include <fstream>
#include <sstream>
#include <string>

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
}
