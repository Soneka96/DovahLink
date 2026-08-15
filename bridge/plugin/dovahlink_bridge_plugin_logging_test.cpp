#include <catch2/catch_test_macros.hpp>

#include <fstream>
#include <sstream>
#include <string>

namespace {

/// Reads the plugin entry point's own source text for structural assertions.
std::string ReadPluginSource() {
    std::ifstream file(DOVAHLINK_PLUGIN_SOURCE_FILE);
    REQUIRE(file.is_open());
    std::ostringstream buffer;
    buffer << file.rdbuf();
    return buffer.str();
}

}  // namespace

// ai/context/skse/architecture.md forbids blocking filesystem work inside a
// Skyrim callback or game-thread hook; every SKSE::log:: call in this plugin
// can run from inside one (the messaging listener, the serialization revert
// callback). Synchronous disk I/O from the revert callback specifically
// reproduced a crash to desktop empirically. That failure mode plays out
// only inside a running SKSE process, so it cannot be caught by exercising
// plugin behavior in a unit test; this asserts the fix structurally instead.
TEST_CASE("SetupLogging configures the default logger through spdlog's non-blocking async factory",
          "[plugin][logging]") {
    std::string source = ReadPluginSource();
    // Matches the actual call, not just the identifier: SetupLogging's own
    // doc comment names "async_factory_nonblock" in prose, so a bare
    // substring check would pass even if the real construction reverted to
    // a synchronous logger.
    CHECK(source.find("async_factory_nonblock::create<") != std::string::npos);
}
