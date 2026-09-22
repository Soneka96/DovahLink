# Preserve the x64 Windows toolchain and dynamic MSVC runtime.
set(VCPKG_TARGET_ARCHITECTURE x64)
set(VCPKG_CRT_LINKAGE dynamic)
set(VCPKG_LIBRARY_LINKAGE dynamic)

# SKSE does not search the plugin directory for these dependent DLLs.
if(PORT STREQUAL "fmt" OR PORT STREQUAL "spdlog")
    set(VCPKG_LIBRARY_LINKAGE static)
endif()
