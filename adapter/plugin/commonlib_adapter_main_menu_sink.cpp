#ifndef WIN32_LEAN_AND_MEAN
#define WIN32_LEAN_AND_MEAN
#endif

#include "SKSE/SKSE.h"

#include "RE/Skyrim.h"

#include "ipc/adapter_ipc_session.hpp"
#include "plugin/commonlib_adapter_main_menu_sink.hpp"

namespace dovahlink::adapter::plugin {

MainMenuOpenedSink::MainMenuOpenedSink(ipc::IAdapterIpcSession& session)
    : session_(session) {}

RE::BSEventNotifyControl MainMenuOpenedSink::ProcessEvent(
    const RE::MenuOpenCloseEvent* event,
    RE::BSTEventSource<RE::MenuOpenCloseEvent>*) {
    if (event != nullptr && event->opening &&
        event->menuName == RE::MainMenu::MENU_NAME) {
        session_.SendPlayContextEnded();
    }
    return RE::BSEventNotifyControl::kContinue;
}

} //  namespace dovahlink::adapter::plugin
