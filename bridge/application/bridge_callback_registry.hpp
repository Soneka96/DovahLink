#pragma once

#include "application/contained_work.hpp"
#include "application/i_bridge_callback_registry.hpp"
#include "game_state/commonlib_level_increase_sink.hpp"

namespace dovahlink::application {

///  Connects coordinator callback lifecycle calls to the runtime event sink.
///  CommonLib-dependent (through `game_state::ICommonLibLevelIncreaseSink`,
///  paired with the CommonLib-dependent `CommonLibLevelIncreaseSink` in the
///  same file), so this file is compiled into the CommonLib-linked
///  `dovahlink_bridge_game_state` target rather than the Skyrim-independent
///  core -- confirmed necessary empirically: compiling anything that reaches
///  `RE/Skyrim.h` outside a target linked against `CommonLibSSE::CommonLibSSE`
///  fails with over 100 cascading errors.
class BridgeCallbackRegistry final : public IBridgeCallbackRegistry {
  public:
    ///  Binds the registry to the runtime level-increase sink.
    explicit BridgeCallbackRegistry(
        game_state::ICommonLibLevelIncreaseSink& sink);

    ///  @copydoc IBridgeCallbackRegistry::RegisterAll
    void RegisterAll(ContainedWorkRunner callbackRunner) override;

    ///  @copydoc IBridgeCallbackRegistry::UnregisterAll
    void UnregisterAll() override;

  private:
    ///  Runtime event sink controlled by the coordinator lifecycle.
    game_state::ICommonLibLevelIncreaseSink& sink_;
};

} //  namespace dovahlink::application
