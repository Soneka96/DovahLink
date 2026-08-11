cmake_minimum_required(VERSION 3.21)

project(DovahLinkBridge LANGUAGES CXX)

# Toolchain and dependency versions pinned for the reproducible build are
# recorded in bridge/README.md; this file only declares the floor CMake
# needs to understand the presets and dependencies below.

set(CMAKE_CXX_STANDARD 23)
set(CMAKE_CXX_STANDARD_REQUIRED ON)
set(CMAKE_CXX_EXTENSIONS OFF)

find_package(Catch2 3 CONFIG REQUIRED)

# Skyrim-independent core: application, protocol, transport, and security
# code. Per ai/context/skse/architecture.md, only the game-state adapter and
# plugin entry point (added later) may depend on CommonLib/Skyrim runtime
# types; nothing in this target may do so.
add_library(dovahlink_bridge_core INTERFACE)
target_include_directories(dovahlink_bridge_core INTERFACE ${CMAKE_CURRENT_SOURCE_DIR})
target_compile_features(dovahlink_bridge_core INTERFACE cxx_std_23)

add_executable(dovahlink_bridge_tests
    security/limits_test.cpp
)
target_link_libraries(dovahlink_bridge_tests PRIVATE dovahlink_bridge_core Catch2::Catch2WithMain)

include(CTest)
include(Catch)
catch_discover_tests(dovahlink_bridge_tests)
