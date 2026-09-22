#pragma once

#include "RE/Skyrim.h"

namespace dovahlink::adapter::ipc {
class IAdapterIpcSession;
}

namespace dovahlink::adapter::plugin {

///  Detects a return to Skyrim's main menu via the standard CommonLib
///  `RE::MenuOpenCloseEvent` signal: SKSE's own `MessagingInterface` has no
///  dedicated message type for it (`kPreLoadGame` fires only for a save
///  load/new game, not a return to the main menu). Registered once at
///  `SKSEPluginLoad` and never destroyed, matching every other
///  process-lifetime allocation there.
class MainMenuOpenedSink final
    : public RE::BSTEventSink<RE::MenuOpenCloseEvent> {
  public:
    ///  Creates a sink that reports main-menu openings through `session`.
    ///  @param session Notified with `SendPlayContextEnded` every time the
    ///  main menu opens.
    explicit MainMenuOpenedSink(ipc::IAdapterIpcSession& session);

    ///  @copydoc RE::BSTEventSink::ProcessEvent
    RE::BSEventNotifyControl
    ProcessEvent(const RE::MenuOpenCloseEvent* event,
                 RE::BSTEventSource<RE::MenuOpenCloseEvent>*) override;

  private:
    ///  Notified with `SendPlayContextEnded` every time the main menu opens.
    ipc::IAdapterIpcSession& session_;
};

} //  namespace dovahlink::adapter::plugin
