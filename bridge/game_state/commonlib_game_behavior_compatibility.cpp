#include "game_state/commonlib_game_behavior_compatibility.hpp"

#include "RE/Skyrim.h"
#include "SKSE/SKSE.h"

#include <xbyak/xbyak.h>

#include <cstddef>

namespace dovahlink::game_state {

namespace {

// Address Library IDs for Skyrim's achievement-eligibility check: SE 13647,
// AE/current-runtime 441528. Confirmed against aers/EngineFixesSkyrim64
// (MIT-licensed; commit c37a8041ffc0a5859e78a19c71b877327773455d -- see
// bridge/README.md for the full attribution record) rather than assumed --
// the same technique, translated to this project's pinned CommonLibSSE-NG,
// which exposes REL::safe_fill/safe_write free functions where that
// reference's CommonLibSSE fork exposes Relocation::write_fill/write member
// functions instead.
constexpr std::uint64_t kAchievementFunctionSeId = 13647;
constexpr std::uint64_t kAchievementFunctionAeId = 441528;

/// Generates the replacement function body: `xor rax, rax; ret`, which makes
/// the patched function unconditionally report achievements as eligible.
struct ReturnFalsePatch final : Xbyak::CodeGenerator {
  /// Emits the replacement instructions.
  ReturnFalsePatch() {
    xor_(rax, rax);
    ret();
  }
};

} // namespace

void ApplyAlwaysActiveSetting() {
  auto *iniSettings = RE::INISettingCollection::GetSingleton();
  if (!iniSettings) {
    SKSE::log::warn("Could not resolve the INI setting collection; "
                    "always-active mode was not applied.");
    return;
  }
  auto *setting = iniSettings->GetSetting("bAlwaysActive:General");
  if (!setting) {
    SKSE::log::warn("bAlwaysActive:General was not found; always-active mode "
                    "was not applied.");
    return;
  }
  setting->data.b = true;
  SKSE::log::info(
      "Always-active mode applied (bAlwaysActive:General set to true).");
}

void InstallAchievementCompatibilityPatch() {
  REL::Relocation<std::uintptr_t> target{
      REL::RelocationID(kAchievementFunctionSeId, kAchievementFunctionAeId)};
  if (target.address() == 0) {
    SKSE::log::warn("Could not resolve the achievement-eligibility function "
                    "address; achievement compatibility was not "
                    "applied.");
    return;
  }

  // The original function body is padded with INT3 first (matching the
  // reference implementation) so no stray original bytes remain between
  // the end of the 4-byte replacement and the function's documented size.
  const std::size_t originalSize = REL::Module::IsAE() ? 0x6E : 0x73;
  REL::safe_fill(target.address(), REL::INT3, originalSize);

  ReturnFalsePatch patch;
  patch.ready();
  REL::safe_write(target.address(), patch.getCode<const void *>(),
                  patch.getSize());

  SKSE::log::info("Achievement compatibility patch installed.");
}

} // namespace dovahlink::game_state
