add_executable(window_lifecycle_tests EXCLUDE_FROM_ALL
  "window_lifecycle_tests.cpp"
  "../flutter_window.cpp"
  "../window_lifecycle_state.cpp"
  "../win32_window.cpp"
)

target_compile_features(window_lifecycle_tests PRIVATE cxx_std_17)
target_compile_definitions(window_lifecycle_tests PRIVATE "NOMINMAX")
target_include_directories(window_lifecycle_tests PRIVATE ".." "${CMAKE_SOURCE_DIR}")
target_link_libraries(window_lifecycle_tests PRIVATE flutter flutter_wrapper_app "dwmapi.lib")
set_target_properties(window_lifecycle_tests PROPERTIES
  RUNTIME_OUTPUT_DIRECTORY "$<TARGET_FILE_DIR:${BINARY_NAME}>"
)
if(MSVC)
  target_compile_options(window_lifecycle_tests PRIVATE /W4 /WX)
endif()

add_test(NAME window_lifecycle_tests COMMAND window_lifecycle_tests)
