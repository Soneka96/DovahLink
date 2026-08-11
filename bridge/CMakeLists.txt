cmake_minimum_required(VERSION 3.21)

project(DovahLinkBridge LANGUAGES CXX)

# Toolchain and dependency versions pinned for the reproducible build are
# recorded in bridge/README.md; this file only declares the floor CMake
# needs to understand the presets and dependencies below.

set(CMAKE_CXX_STANDARD 23)
set(CMAKE_CXX_STANDARD_REQUIRED ON)
set(CMAKE_CXX_EXTENSIONS OFF)

find_package(Catch2 3 CONFIG REQUIRED)
find_package(boost_asio CONFIG REQUIRED)
find_package(boost_json CONFIG REQUIRED)

# Skyrim-independent core: application, protocol, transport, and security
# code. Per ai/context/skse/architecture.md, only the game-state adapter and
# plugin entry point (added later) may depend on CommonLib/Skyrim runtime
# types; nothing in this target may do so.
add_library(dovahlink_bridge_core STATIC
    application/character_state.cpp
    application/connection_timeout_tracker.cpp
    application/coordinator.cpp
    application/event_coalescer.cpp
    application/outbound_queue.cpp
    application/revision_tracker.cpp
    application/replay_guard.cpp
    application/session.cpp
    protocol/bounded_json.cpp
    protocol/envelope.cpp
    protocol/json_field_decoders.cpp
    protocol/messages.cpp
    security/csprng.cpp
    security/throttle.cpp
    security/token_store.cpp
    transport/listener.cpp
)
target_include_directories(dovahlink_bridge_core PUBLIC ${CMAKE_CURRENT_SOURCE_DIR})
target_compile_features(dovahlink_bridge_core PUBLIC cxx_std_23)
target_link_libraries(dovahlink_bridge_core PUBLIC Boost::asio Boost::json bcrypt)

add_executable(dovahlink_bridge_tests
    application/character_state_test.cpp
    application/connection_timeout_tracker_test.cpp
    application/coordinator_failure_test.cpp
    application/coordinator_test.cpp
    application/event_coalescer_test.cpp
    application/level_event_sink_test.cpp
    application/outbound_queue_test.cpp
    application/reconnect_reset_test.cpp
    application/revision_tracker_test.cpp
    application/replay_guard_test.cpp
    application/session_test.cpp
    security/limits_test.cpp
    security/csprng_test.cpp
    security/throttle_test.cpp
    security/token_store_test.cpp
    protocol/bounded_json_test.cpp
    protocol/envelope_test.cpp
    protocol/json_field_decoders_test.cpp
    protocol/messages_test.cpp
    transport/listener_test.cpp
)
target_link_libraries(dovahlink_bridge_tests PRIVATE dovahlink_bridge_core Catch2::Catch2WithMain)
target_compile_definitions(dovahlink_bridge_tests PRIVATE
    DOVAHLINK_FIXTURES_DIR="${CMAKE_CURRENT_SOURCE_DIR}/../protocol/fixtures"
)

include(CTest)
include(Catch)
catch_discover_tests(dovahlink_bridge_tests)
