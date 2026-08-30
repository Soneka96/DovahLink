cmake_minimum_required(VERSION 3.21)

project(DovahLinkAdapter LANGUAGES CXX)

if(CMAKE_TOOLCHAIN_FILE)
    message(VERBOSE "Using CMake toolchain file: ${CMAKE_TOOLCHAIN_FILE}")
endif()

set(CMAKE_MAP_IMPORTED_CONFIG_RELWITHDEBINFO Release)

set(CMAKE_EXPORT_COMPILE_COMMANDS ON)
set(CMAKE_CXX_STANDARD 23)
set(CMAKE_CXX_STANDARD_REQUIRED ON)
set(CMAKE_CXX_EXTENSIONS OFF)

find_package(Catch2 3.0 CONFIG REQUIRED)

# Independent of bridge/: no CommonLib/Skyrim dependency, per
# ai/context/adapter/architecture.md's "Technology boundary". A later concept
# adds the CommonLib/SKSE toolchain to this build alongside the game-thread
# boundary and lifecycle modules this concept does not touch.
add_library(dovahlink_adapter_core STATIC
    ipc/ipc_frame_codec.cpp
    capture/adapter_capture_handoff_queue.cpp
)
target_include_directories(dovahlink_adapter_core PUBLIC ${CMAKE_CURRENT_SOURCE_DIR})
target_compile_features(dovahlink_adapter_core PUBLIC cxx_std_23)
if(MSVC)
    target_compile_options(dovahlink_adapter_core PUBLIC /EHsc)
endif()

add_executable(dovahlink_adapter_tests
    tests/ipc/ipc_frame_codec_test.cpp
    tests/capture/adapter_capture_handoff_queue_test.cpp
)
target_link_libraries(dovahlink_adapter_tests PRIVATE
    dovahlink_adapter_core
    Catch2::Catch2WithMain
)
target_compile_definitions(dovahlink_adapter_tests PRIVATE
    DOVAHLINK_ADAPTER_IPC_DIR="${CMAKE_CURRENT_SOURCE_DIR}/ipc"
)

include(CTest)
include(Catch)
catch_discover_tests(dovahlink_adapter_tests)
