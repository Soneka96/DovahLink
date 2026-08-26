cmake_minimum_required(VERSION 3.21)

project(DovahLinkBridge LANGUAGES CXX)

# CMake 4.4 reports a preset toolchainFile as unused on reconfigure unless
# the project reads the value after the toolchain has been loaded.
if(CMAKE_TOOLCHAIN_FILE)
    message(VERBOSE "Using CMake toolchain file: ${CMAKE_TOOLCHAIN_FILE}")
endif()

# vcpkg packages provide Debug and Release imported configurations. Map this
# single-config preset's RelWithDebInfo build to the compatible Release
# dependency libraries instead of letting CMake select the first configuration.
set(CMAKE_MAP_IMPORTED_CONFIG_RELWITHDEBINFO Release)

# Toolchain and dependency versions pinned for the reproducible build are
# recorded in bridge/README.md; this file only declares the floor CMake
# needs to understand the presets and dependencies below.

set(CMAKE_EXPORT_COMPILE_COMMANDS ON)
set(CMAKE_CXX_STANDARD 23)
set(CMAKE_CXX_STANDARD_REQUIRED ON)
set(CMAKE_CXX_EXTENSIONS OFF)

find_package(Catch2 3.0 CONFIG REQUIRED)
find_package(GTest CONFIG REQUIRED)
find_package(boost_asio CONFIG REQUIRED)
find_package(boost_beast CONFIG REQUIRED)
find_package(boost_json CONFIG REQUIRED)

# Skyrim-independent core: application, protocol, transport, and security
# code. Per ai/context/skse/architecture.md, only the game-state adapter and
# plugin entry point (added later) may depend on CommonLib/Skyrim runtime
# types; nothing in this target may do so.
add_library(dovahlink_bridge_core STATIC
    application/active_session_controller.cpp
    application/active_session_disconnector.cpp
    application/active_session_socket.cpp
    application/bridge_transport.cpp
    application/bridge_worker_pool.cpp
    application/character_state_store.cpp
    application/connection_session.cpp
    application/connection_timeout_tracker.cpp
    application/coordinator.cpp
    application/game_behavior_config.cpp
    application/play_context_lifecycle.cpp
    application/handshake_handler.cpp
    application/message_dispatcher.cpp
    application/pairing_handler.cpp
    application/active_play_context_reader.cpp
    application/active_play_context_level_sink.cpp
    application/revision_tracker.cpp
    application/replay_guard.cpp
    application/session.cpp
    application/subscription_handler.cpp
    application/timestamp.cpp
    application/trust_device_admin_service.cpp
    application/trust_mutation_coordinator.cpp
    application/trust_reset_service.cpp
    application/rename_handler.cpp
    game_state/level_adapter.cpp
    game_state/level_increase_handler.cpp
    protocol/bounded_json.cpp
    protocol/capabilities_payload.cpp
    protocol/envelope.cpp
    protocol/error_payload.cpp
    protocol/hello_ack_payload.cpp
    protocol/hello_payload.cpp
    protocol/json_field_decoders.cpp
    protocol/pairing_ack_payload.cpp
    protocol/pairing_confirm_payload.cpp
    protocol/pairing_outcome_payload.cpp
    protocol/pairing_status_payload.cpp
    protocol/rename_outcome_payload.cpp
    protocol/rename_request_payload.cpp
    protocol/session_invalidated_payload.cpp
    protocol/snapshot_request_payload.cpp
    protocol/state_event_payload.cpp
    protocol/state_snapshot_payload.cpp
    protocol/subscribe_payload.cpp
    protocol/subscription_ack_payload.cpp
    security/constant_time_compare.cpp
    security/csprng.cpp
    security/dpapi.cpp
    security/factory_reset_challenge.cpp
    security/hex.cpp
    security/pairing_session.cpp
    security/throttle.cpp
    security/token_provider.cpp
    security/token_store.cpp
    security/trust_device_store.cpp
    security/trust_reset_store.cpp
    security/trust_store.cpp
    security/windows_trust_store_persistence.cpp
    shared/scoped_release.cpp
    transport/connection_slot.cpp
    transport/listener.cpp
    transport/websocket_session.cpp
)
target_include_directories(dovahlink_bridge_core PUBLIC ${CMAKE_CURRENT_SOURCE_DIR})
target_compile_features(dovahlink_bridge_core PUBLIC cxx_std_23)
if(WIN32)
    # Keep DovahLink-owned core sources on the Windows 10 SDK surface without
    # overriding CommonLibSSE's runtime-target compile contract.
    target_compile_definitions(dovahlink_bridge_core PRIVATE _WIN32_WINNT=0x0A00)
endif()
if(MSVC)
    target_compile_options(dovahlink_bridge_core PUBLIC /EHsc)
endif()
target_link_libraries(dovahlink_bridge_core PUBLIC Boost::asio Boost::beast Boost::json bcrypt crypt32)

# CommonLib-dependent game-state adapters. Kept in a separate target from
# dovahlink_bridge_core so dovahlink_bridge_tests never needs to link
# CommonLibSSE-NG: runtime-specific adapters are covered through their
# Skyrim-independent conversion seams or structural checks (see e.g.
# game_state/level_adapter.hpp). This target is linked into the real SKSE plugin
# binary.
find_package(CommonLibSSE CONFIG REQUIRED)
add_library(dovahlink_bridge_game_state STATIC
    game_state/commonlib_game_behavior_compatibility.cpp
    game_state/commonlib_game_lifecycle_sink.cpp
    game_state/player_level_accessor.cpp
    game_state/commonlib_level_increase_sink.cpp
    game_state/commonlib_pairing_notification_sink.cpp
    game_state/commonlib_trust_admin_papyrus_adapter.cpp
    application/bridge_callback_registry.cpp
)
target_link_libraries(dovahlink_bridge_game_state PUBLIC dovahlink_bridge_core CommonLibSSE::CommonLibSSE)

# The real SKSE plugin binary. Deliberately NOT using add_commonlibsse_plugin:
# that macro auto-generates a small __<target>Plugin.cpp containing
# `#include "REL/Relocation.h"` before `#include "SKSE/SKSE.h"`, but
# REL/Relocation.h (at this project's pinned CommonLibSSE-NG source commit --
# see bridge/README.md) requires namespace aliases (REL::stl, REL::WinAPI)
# that are only established partway through SKSE/SKSE.h's own precompiled-
# header chain (SKSE/Impl/PCH.h) -- confirmed directly against the vendored
# source, and by reproducing the resulting cascade of "'stl' is not a class
# or namespace" / missing std::span / invalid "sv" literal errors when built
# via that macro. plugin/dovahlink_bridge_plugin.cpp includes SKSE/SKSE.h
# first (the order PCH.h actually requires) and declares the SKSEPluginInfo
# block itself instead, sidestepping the auto-generated file entirely.
# USE_ADDRESS_LIBRARY is what that plugin info block declares, matching
# the project's Address-Library-based toolchain; the plugin's own
# runtime guard (game_state/runtime_guard.hpp, checked in SKSEPluginLoad) is
# what actually restricts DovahLink to the one approved runtime -- Address
# Library capability is not the same as DovahLink support.
add_library(dovahlink_bridge_plugin SHARED plugin/dovahlink_bridge_plugin.cpp)
target_compile_definitions(dovahlink_bridge_plugin PRIVATE __CMAKE_COMMONLIBSSE_PLUGIN=1)
target_link_libraries(dovahlink_bridge_plugin PRIVATE dovahlink_bridge_game_state)

# Skyrim-independent test harness: runs the real Coordinator/BridgeTransport/
# BridgeWorkerPool stack against a fake character level instead of a running
# Skyrim process, so integration/'s .NET validation client has something real
# to connect to without requiring Skyrim. Links dovahlink_bridge_core only,
# same as the test binary -- no CommonLibSSE-NG dependency.
add_executable(dovahlink_bridge_harness harness/dovahlink_bridge_harness.cpp)
if(WIN32)
    target_compile_definitions(dovahlink_bridge_harness PRIVATE _WIN32_WINNT=0x0A00)
endif()
target_link_libraries(dovahlink_bridge_harness PRIVATE dovahlink_bridge_core)

add_executable(dovahlink_bridge_tests
    test_support/source_text_test_support_test.cpp
    application/application_test_support_test.cpp
    application/active_session_controller_test.cpp
    application/active_session_disconnector_test.cpp
    application/active_session_socket_test.cpp
    application/bridge_callback_registry_test.cpp
    application/bridge_transport_test.cpp
    application/bridge_worker_pool_test.cpp
    application/character_state_store_test.cpp
    application/connection_session_test.cpp
    application/connection_timeout_tracker_test.cpp
    application/coordinator_failure_test.cpp
    application/coordinator_test.cpp
    application/game_behavior_config_test.cpp
    application/play_context_lifecycle_test.cpp
    application/play_context_transition_test.cpp
    application/handshake_handler_test.cpp
    application/active_play_context_reader_test.cpp
    application/active_play_context_level_sink_test.cpp
    application/message_dispatcher_test.cpp
    application/pairing_handler_test.cpp
    application/pairing_notification_sink_test.cpp
    application/play_context_test.cpp
    application/reconnect_reset_test.cpp
    application/revision_tracker_test.cpp
    application/replay_guard_test.cpp
    application/session_test.cpp
    application/subscription_handler_test.cpp
    application/timestamp_test.cpp
    application/trust_device_admin_service_test.cpp
    application/trust_mutation_coordinator_test.cpp
    application/trust_reset_service_test.cpp
    application/rename_handler_test.cpp
    application/mock_framework_pilot_test.cpp
    game_state/level_adapter_test.cpp
    game_state/level_increase_handler_test.cpp
    game_state/commonlib_level_increase_sink_test.cpp
    game_state/commonlib_game_lifecycle_sink_test.cpp
    game_state/commonlib_runtime_include_order_test.cpp
    game_state/runtime_guard_test.cpp
    game_state/commonlib_trust_admin_papyrus_adapter_test.cpp
    harness/dovahlink_bridge_harness_test.cpp
    plugin/dovahlink_bridge_plugin_compatibility_test.cpp
    plugin/dovahlink_bridge_plugin_logging_test.cpp
    plugin/dovahlink_bridge_plugin_registration_test.cpp
    security/limits_test.cpp
    security/constant_time_compare_test.cpp
    security/csprng_test.cpp
    security/dpapi_test.cpp
    security/factory_reset_challenge_test.cpp
    security/hex_test.cpp
    security/pairing_session_test.cpp
    security/throttle_test.cpp
    security/token_provider_test.cpp
    security/token_store_test.cpp
    security/trust_store_test.cpp
    security/windows_trust_store_persistence_test.cpp
    protocol/bounded_json_test.cpp
    protocol/capabilities_payload_test.cpp
    protocol/envelope_test.cpp
    protocol/error_payload_test.cpp
    protocol/hello_ack_payload_test.cpp
    protocol/hello_payload_test.cpp
    protocol/json_field_decoders_test.cpp
    protocol/pairing_ack_payload_test.cpp
    protocol/pairing_confirm_payload_test.cpp
    protocol/pairing_outcome_payload_test.cpp
    protocol/pairing_status_payload_test.cpp
    protocol/rename_outcome_payload_test.cpp
    protocol/rename_request_payload_test.cpp
    protocol/session_invalidated_payload_test.cpp
    protocol/snapshot_request_payload_test.cpp
    protocol/state_event_payload_test.cpp
    protocol/state_snapshot_payload_test.cpp
    protocol/subscribe_payload_test.cpp
    protocol/subscription_ack_payload_test.cpp
    shared/scoped_release_test.cpp
    transport/connection_slot_test.cpp
    transport/inbound_limit_test.cpp
    transport/listener_test.cpp
    transport/loopback_peer_test.cpp
    transport/websocket_session_test.cpp
)
target_link_libraries(dovahlink_bridge_tests PRIVATE
    dovahlink_bridge_core
    Catch2::Catch2WithMain
    GTest::gmock
)
target_compile_definitions(dovahlink_bridge_tests PRIVATE
    DOVAHLINK_FIXTURES_DIR="${CMAKE_CURRENT_SOURCE_DIR}/../protocol/fixtures"
    DOVAHLINK_HARNESS_EXE="$<TARGET_FILE:dovahlink_bridge_harness>"
    DOVAHLINK_PLUGIN_SOURCE_FILE="${CMAKE_CURRENT_SOURCE_DIR}/plugin/dovahlink_bridge_plugin.cpp"
    DOVAHLINK_LIFECYCLE_SINK_HEADER_FILE="${CMAKE_CURRENT_SOURCE_DIR}/game_state/commonlib_game_lifecycle_sink.hpp"
    DOVAHLINK_LIFECYCLE_SINK_SOURCE_FILE="${CMAKE_CURRENT_SOURCE_DIR}/game_state/commonlib_game_lifecycle_sink.cpp"
    DOVAHLINK_LEVEL_INCREASE_SINK_HEADER_FILE="${CMAKE_CURRENT_SOURCE_DIR}/game_state/commonlib_level_increase_sink.hpp"
    DOVAHLINK_LEVEL_INCREASE_SINK_SOURCE_FILE="${CMAKE_CURRENT_SOURCE_DIR}/game_state/commonlib_level_increase_sink.cpp"
    DOVAHLINK_TRUST_ADMIN_PAPYRUS_SOURCE_FILE="${CMAKE_CURRENT_SOURCE_DIR}/game_state/commonlib_trust_admin_papyrus_adapter.cpp"
    DOVAHLINK_CALLBACK_REGISTRY_CONTRACT_HEADER_FILE="${CMAKE_CURRENT_SOURCE_DIR}/application/i_bridge_callback_registry.hpp"
    DOVAHLINK_CALLBACK_REGISTRY_HEADER_FILE="${CMAKE_CURRENT_SOURCE_DIR}/application/bridge_callback_registry.hpp"
    DOVAHLINK_CALLBACK_REGISTRY_SOURCE_FILE="${CMAKE_CURRENT_SOURCE_DIR}/application/bridge_callback_registry.cpp"
    DOVAHLINK_COORDINATOR_HEADER_FILE="${CMAKE_CURRENT_SOURCE_DIR}/application/coordinator.hpp"
)
if(WIN32)
    target_compile_definitions(dovahlink_bridge_tests PRIVATE _WIN32_WINNT=0x0A00)
endif()

include(CTest)
include(Catch)
catch_discover_tests(dovahlink_bridge_tests)
