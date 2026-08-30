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
# ai/context/adapter/architecture.md's "Technology boundary".
add_library(dovahlink_adapter_core STATIC
    ipc/ipc_frame_codec.cpp
    ipc/winsock_adapter_ipc_socket.cpp
    ipc/adapter_ipc_connection.cpp
    ipc/adapter_ipc_peer_proof_provider.cpp
    ipc/adapter_ipc_session.cpp
    ipc/adapter_ipc_hmac.cpp
    ipc/settable_adapter_ipc_peer_proof_provider.cpp
    capture/adapter_capture_handoff_queue.cpp
    dispatch/adapter_native_dispatcher.cpp
    identity/adapter_instance_id_generator.cpp
    process/adapter_owner_lifetime_id.cpp
    process/adapter_host_rendezvous_reader.cpp
)
target_include_directories(dovahlink_adapter_core PUBLIC ${CMAKE_CURRENT_SOURCE_DIR})
target_compile_features(dovahlink_adapter_core PUBLIC cxx_std_23)
if(MSVC)
    target_compile_options(dovahlink_adapter_core PUBLIC /EHsc)
endif()
if(WIN32)
    target_link_libraries(dovahlink_adapter_core PUBLIC ws2_32 bcrypt)
endif()

# CommonLib-dependent runtime adapters. Kept in a separate target from
# dovahlink_adapter_core so dovahlink_adapter_tests never needs to link
# CommonLibSSE-NG: runtime-specific adapters are covered through structural
# checks instead (see tests/runtime/). This is the only adapter/ target
# allowed to depend on Skyrim/CommonLib runtime types, per
# ai/context/adapter/architecture.md's "Technology boundary".
find_package(CommonLibSSE CONFIG REQUIRED)
add_library(dovahlink_adapter_runtime STATIC
    runtime/commonlib_adapter_task_marshaller.cpp
    papyrus/commonlib_adapter_status_papyrus_adapter.cpp
)
target_link_libraries(dovahlink_adapter_runtime PUBLIC dovahlink_adapter_core CommonLibSSE::CommonLibSSE)

# The real SKSE plugin binary. Deliberately NOT using add_commonlibsse_plugin,
# for the same reason documented in bridge/CMakeLists.txt: that macro
# auto-generates a small file including REL/Relocation.h before SKSE/SKSE.h,
# which breaks at this project's pinned CommonLibSSE-NG commit.
# plugin/dovahlink_adapter_plugin.cpp includes SKSE/SKSE.h first and declares
# the SKSEPluginInfo block itself instead.
add_library(dovahlink_adapter_plugin SHARED plugin/dovahlink_adapter_plugin.cpp)
target_compile_definitions(dovahlink_adapter_plugin PRIVATE __CMAKE_COMMONLIBSSE_PLUGIN=1)
target_link_libraries(dovahlink_adapter_plugin PRIVATE dovahlink_adapter_runtime)

add_executable(dovahlink_adapter_tests
    tests/ipc/ipc_frame_codec_test.cpp
    tests/ipc/winsock_adapter_ipc_socket_test.cpp
    tests/ipc/adapter_ipc_connection_test.cpp
    tests/ipc/adapter_ipc_session_test.cpp
    tests/ipc/adapter_ipc_hmac_test.cpp
    tests/ipc/settable_adapter_ipc_peer_proof_provider_test.cpp
    tests/process/adapter_owner_lifetime_id_test.cpp
    tests/process/adapter_host_rendezvous_reader_test.cpp
    tests/capture/adapter_capture_handoff_queue_test.cpp
    tests/dispatch/adapter_native_dispatcher_test.cpp
    tests/identity/adapter_instance_id_generator_test.cpp
    tests/runtime/commonlib_adapter_task_marshaller_test.cpp
    tests/papyrus/commonlib_adapter_status_papyrus_adapter_test.cpp
    tests/plugin/dovahlink_adapter_plugin_test.cpp
)
target_include_directories(dovahlink_adapter_tests PRIVATE ${CMAKE_CURRENT_SOURCE_DIR}/tests)
target_link_libraries(dovahlink_adapter_tests PRIVATE
    dovahlink_adapter_core
    Catch2::Catch2WithMain
)
target_compile_definitions(dovahlink_adapter_tests PRIVATE
    DOVAHLINK_ADAPTER_IPC_DIR="${CMAKE_CURRENT_SOURCE_DIR}/ipc"
    DOVAHLINK_ADAPTER_PROCESS_DIR="${CMAKE_CURRENT_SOURCE_DIR}/process"
    DOVAHLINK_ADAPTER_RUNTIME_DIR="${CMAKE_CURRENT_SOURCE_DIR}/runtime"
    DOVAHLINK_ADAPTER_TASK_MARSHALLER_SOURCE_FILE="${CMAKE_CURRENT_SOURCE_DIR}/runtime/commonlib_adapter_task_marshaller.cpp"
    DOVAHLINK_ADAPTER_PAPYRUS_DIR="${CMAKE_CURRENT_SOURCE_DIR}/papyrus"
    DOVAHLINK_ADAPTER_STATUS_PAPYRUS_ADAPTER_SOURCE_FILE="${CMAKE_CURRENT_SOURCE_DIR}/papyrus/commonlib_adapter_status_papyrus_adapter.cpp"
    DOVAHLINK_ADAPTER_PLUGIN_SOURCE_FILE="${CMAKE_CURRENT_SOURCE_DIR}/plugin/dovahlink_adapter_plugin.cpp"
    DOVAHLINK_ADAPTER_SOURCE_ROOT_DIR="${CMAKE_CURRENT_SOURCE_DIR}"
)

include(CTest)
include(Catch)
catch_discover_tests(dovahlink_adapter_tests)
