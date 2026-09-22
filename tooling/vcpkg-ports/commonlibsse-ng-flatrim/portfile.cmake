vcpkg_from_github(
    OUT_SOURCE_PATH SOURCE_PATH
    REPO alandtse/CommonLibSSE-NG
    REF 5decf47b01dde5501b03afaa91cd4d182e793cca
    SHA512 58a1647f5e7a23d3f5e75a02b2d1a4ef9d8a1b4e799c034c2086c20d736ca920cc8927100157130ee766a32cd20d4e2de60c1019e6d3e2bc37bfb9c6fcd8c25b
    HEAD_REF ng
)

# SKSE_SUPPORT_XBYAK=off (the option's own upstream default): this only gates
# SKSE/ContextHook.h's internal Xbyak-based trampoline hooking, which this
# project's adapter never calls (no SKSE::Trampoline/ContextHook usage --
# only REL::safe_fill/safe_write byte patches). Leaving it on makes
# ContextHook.h's own #include <xbyak/xbyak.h> pull in the real <Windows.h>
# (for VirtualProtect) as a PUBLIC compile definition on every consumer of
# CommonLibSSE::CommonLibSSE, before RE::/REX::W32's own headers are done
# being parsed in a single-#include "RE/Skyrim.h" consumer -- <Windows.h>'s
# real min/max/MAX_PATH macros then corrupt dozens of later REX::W32 and
# RE:: declarations project-wide. The adapter's own direct
# #include <xbyak/xbyak.h> in commonlib_adapter_game_behavior_compatibility.cpp
# (placed after RE/Skyrim.h, per this project's own include-order rule) is
# unaffected: it links the standalone xbyak vcpkg port directly, not this flag.
vcpkg_configure_cmake(
    SOURCE_PATH "${SOURCE_PATH}"
    PREFER_NINJA
    OPTIONS -DENABLE_SKYRIM_VR=off -DBUILD_TESTS=off -DSKSE_SUPPORT_XBYAK=off
)

vcpkg_install_cmake()
vcpkg_cmake_config_fixup(PACKAGE_NAME CommonLibSSE CONFIG_PATH lib/cmake)
vcpkg_copy_pdbs()

file(GLOB CMAKE_CONFIGS "${CURRENT_PACKAGES_DIR}/share/CommonLibSSE/CommonLibSSE/*.cmake")
file(INSTALL ${CMAKE_CONFIGS} DESTINATION "${CURRENT_PACKAGES_DIR}/share/CommonLibSSE")
file(INSTALL "${SOURCE_PATH}/cmake/CommonLibSSE.cmake" DESTINATION "${CURRENT_PACKAGES_DIR}/share/CommonLibSSE")

file(REMOVE_RECURSE "${CURRENT_PACKAGES_DIR}/debug/include")
file(REMOVE_RECURSE "${CURRENT_PACKAGES_DIR}/share/CommonLibSSE/CommonLibSSE")

# Upstream's own cmake/config.cmake.in (as of this pinned commit) only declares
# find_dependency(spdlog), even though CommonLibSSE::CommonLibSSE's public link
# interface now also exposes Microsoft::DirectXTK: a consumer's find_package(CommonLibSSE)
# fails with "the target was not found" for DirectXTK without this. fmt, rapidcsv, and
# xbyak need no equivalent line: they are not part of that public link interface.
file(APPEND "${CURRENT_PACKAGES_DIR}/share/CommonLibSSE/CommonLibSSEConfig.cmake"
    "\nfind_dependency(directxtk CONFIG)\n")

file(
    INSTALL "${SOURCE_PATH}/COPYING.txt"
    DESTINATION "${CURRENT_PACKAGES_DIR}/share/${PORT}"
    RENAME copyright
)
