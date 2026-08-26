#include <catch2/catch_test_macros.hpp>

#include <fstream>
#include <sstream>
#include <string>

namespace {

///  Reads the CommonLib-free callback registry contract for structural
///  assertions.
std::string ReadCallbackRegistryContractHeader() {
    std::ifstream file(DOVAHLINK_CALLBACK_REGISTRY_CONTRACT_HEADER_FILE);
    REQUIRE(file.is_open());
    std::ostringstream buffer;
    buffer << file.rdbuf();
    return buffer.str();
}

///  Reads the runtime callback registry header for structural assertions.
std::string ReadCallbackRegistryHeader() {
    std::ifstream file(DOVAHLINK_CALLBACK_REGISTRY_HEADER_FILE);
    REQUIRE(file.is_open());
    std::ostringstream buffer;
    buffer << file.rdbuf();
    return buffer.str();
}

///  Reads the runtime callback registry source for structural assertions.
std::string ReadCallbackRegistrySource() {
    std::ifstream file(DOVAHLINK_CALLBACK_REGISTRY_SOURCE_FILE);
    REQUIRE(file.is_open());
    std::ostringstream buffer;
    buffer << file.rdbuf();
    return buffer.str();
}

} //  namespace

//  BridgeCallbackRegistry is CommonLib-dependent (through
//  game_state::ICommonLibLevelIncreaseSink) and compiled into
//  dovahlink_bridge_game_state, not dovahlink_bridge_tests -- this test target
//  intentionally never links CommonLibSSE-NG. This structural test protects
//  its contract and forwarding boundary without pretending to test Skyrim
//  internals as DovahLink behavior.
TEST_CASE("BridgeCallbackRegistry keeps its contract and forwarding boundary",
          "[application][bridge_callback_registry]") {
    const std::string contractHeader = ReadCallbackRegistryContractHeader();
    const std::string header = ReadCallbackRegistryHeader();
    const std::string source = ReadCallbackRegistrySource();

    CHECK(contractHeader.find("class IBridgeCallbackRegistry") !=
          std::string::npos);
    CHECK(header.find("public IBridgeCallbackRegistry") != std::string::npos);
    CHECK(header.find("ICommonLibLevelIncreaseSink& sink_") !=
          std::string::npos);
    CHECK(source.find("sink_.Register(std::move(callbackRunner));") !=
          std::string::npos);
    CHECK(source.find(
              "void BridgeCallbackRegistry::UnregisterAll() { sink_.Unregister(); }") !=
          std::string::npos);
}
