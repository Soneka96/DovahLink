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
find_package(boost_beast CONFIG REQUIRED)
find_package(boost_json CONFIG REQUIRED)

# Skyrim-independent core: application, protocol, transport, and security
# code. Per ai/context/skse/architecture.md, only the game-state adapter and
# plugin entry point (added later) may depend on CommonLib/Skyrim runtime
# types; nothing in this target may do so.
add_library(dovahlink_bridge_core STATIC
    application/bridge_transport.cpp
    application/bridge_worker_pool.cpp
    application/character_state.cpp
    application/character_state_store.cpp
    application/connection_session.cpp
    application/connection_timeout_tracker.cpp
    application/coordinator.cpp
    application/event_coalescer.cpp
    application/handshake_handler.cpp
    application/message_dispatcher.cpp
    application/outbound_queue.cpp
    application/revision_tracker.cpp
    application/replay_guard.cpp
    application/session.cpp
    application/subscription_handler.cpp
    application/timestamp.cpp
    game_state/level_adapter.cpp
    game_state/level_increase_handler.cpp
    protocol/bounded_json.cpp
    protocol/envelope.cpp
    protocol/json_field_decoders.cpp
    protocol/messages.cpp
    security/csprng.cpp
    security/hex.cpp
    security/throttle.cpp
    security/token_provider.cpp
    security/token_store.cpp
    transport/connection_slot.cpp
    transport/listener.cpp
    transport/websocket_session.cpp
)
target_include_directories(dovahlink_bridge_core PUBLIC ${CMAKE_CURRENT_SOURCE_DIR})
target_compile_features(dovahlink_bridge_core PUBLIC cxx_std_23)
target_link_libraries(dovahlink_bridge_core PUBLIC Boost::asio Boost::beast Boost::json bcrypt)

# CommonLib-dependent game-state adapters. Kept in a separate target from
# dovahlink_bridge_core so dovahlink_bridge_tests never needs to link
# CommonLibSSE-NG: nothing here is unit-tested (there is no Skyrim process
# to run against), only the Skyrim-independent conversion logic each
# adapter's header exposes is (see e.g. game_state/level_adapter.hpp). This
# target is linked into the real SKSE plugin binary, added in a later step.
find_package(CommonLibSSE CONFIG REQUIRED)
add_library(dovahlink_bridge_game_state STATIC
    game_state/commonlib_level_accessor.cpp
    game_state/commonlib_level_increase_sink.cpp
)
target_link_libraries(dovahlink_bridge_game_state PUBLIC dovahlink_bridge_core CommonLibSSE::CommonLibSSE)

add_executable(dovahlink_bridge_tests
    application/bridge_transport_test.cpp
    application/bridge_worker_pool_test.cpp
    application/character_state_store_test.cpp
    application/character_state_test.cpp
    application/connection_session_test.cpp
    application/connection_timeout_tracker_test.cpp
    application/coordinator_failure_test.cpp
    application/coordinator_test.cpp
    application/event_coalescer_test.cpp
    application/handshake_handler_test.cpp
    application/level_event_sink_test.cpp
    application/message_dispatcher_test.cpp
    application/outbound_queue_test.cpp
    application/reconnect_reset_test.cpp
    application/revision_tracker_test.cpp
    application/replay_guard_test.cpp
    application/session_test.cpp
    application/subscription_handler_test.cpp
    application/timestamp_test.cpp
    game_state/level_adapter_test.cpp
    game_state/level_increase_handler_test.cpp
    game_state/runtime_guard_test.cpp
    security/limits_test.cpp
    security/csprng_test.cpp
    security/hex_test.cpp
    security/throttle_test.cpp
    security/token_provider_test.cpp
    security/token_store_test.cpp
    protocol/bounded_json_test.cpp
    protocol/envelope_test.cpp
    protocol/json_field_decoders_test.cpp
    protocol/messages_test.cpp
    transport/connection_slot_test.cpp
    transport/inbound_limit_test.cpp
    transport/listener_test.cpp
    transport/loopback_peer_test.cpp
    transport/websocket_session_test.cpp
)
target_link_libraries(dovahlink_bridge_tests PRIVATE dovahlink_bridge_core Catch2::Catch2WithMain)
target_compile_definitions(dovahlink_bridge_tests PRIVATE
    DOVAHLINK_FIXTURES_DIR="${CMAKE_CURRENT_SOURCE_DIR}/../protocol/fixtures"
)

include(CTest)
include(Catch)
catch_discover_tests(dovahlink_bridge_tests)
