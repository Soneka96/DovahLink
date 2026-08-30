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
    capture/adapter_capture_handoff_queue.cpp
    dispatch/adapter_native_dispatcher.cpp
    identity/adapter_instance_id_generator.cpp
)
target_include_directories(dovahlink_adapter_core PUBLIC ${CMAKE_CURRENT_SOURCE_DIR})
target_compile_features(dovahlink_adapter_core PUBLIC cxx_std_23)
if(MSVC)
    target_compile_options(dovahlink_adapter_core PUBLIC /EHsc)
endif()
if(WIN32)
    target_link_libraries(dovahlink_adapter_core PUBLIC ws2_32)
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
)
target_link_libraries(dovahlink_adapter_runtime PUBLIC dovahlink_adapter_core CommonLibSSE::CommonLibSSE)

add_executable(dovahlink_adapter_tests
    tests/ipc/ipc_frame_codec_test.cpp
    tests/ipc/winsock_adapter_ipc_socket_test.cpp
    tests/ipc/adapter_ipc_connection_test.cpp
    tests/ipc/adapter_ipc_session_test.cpp
    tests/capture/adapter_capture_handoff_queue_test.cpp
    tests/dispatch/adapter_native_dispatcher_test.cpp
    tests/identity/adapter_instance_id_generator_test.cpp
    tests/runtime/commonlib_adapter_task_marshaller_test.cpp
)
target_include_directories(dovahlink_adapter_tests PRIVATE ${CMAKE_CURRENT_SOURCE_DIR}/tests)
target_link_libraries(dovahlink_adapter_tests PRIVATE
    dovahlink_adapter_core
    Catch2::Catch2WithMain
)
target_compile_definitions(dovahlink_adapter_tests PRIVATE
    DOVAHLINK_ADAPTER_IPC_DIR="${CMAKE_CURRENT_SOURCE_DIR}/ipc"
    DOVAHLINK_ADAPTER_RUNTIME_DIR="${CMAKE_CURRENT_SOURCE_DIR}/runtime"
    DOVAHLINK_ADAPTER_TASK_MARSHALLER_SOURCE_FILE="${CMAKE_CURRENT_SOURCE_DIR}/runtime/commonlib_adapter_task_marshaller.cpp"
)

include(CTest)
include(Catch)
catch_discover_tests(dovahlink_adapter_tests)
