#include "game_state/commonlib_game_lifecycle_sink.hpp"

#include "application/active_play_context.hpp"

#include <sstream>
#include <thread>
#include <utility>

namespace dovahlink::game_state {

namespace {

///  Formats the calling thread's ID for diagnostic logging.
std::string ThreadIdString() {
    std::ostringstream stream;
    stream << std::this_thread::get_id();
    return stream.str();
}

} //  namespace

CommonLibGameLifecycleSink::CommonLibGameLifecycleSink(
    application::GameLifecycleTracker& tracker,
    application::IActivePlayContext& activePlayContext)
    : tracker_(tracker), activePlayContext_(activePlayContext) {}

void CommonLibGameLifecycleSink::Register(
    application::ContainedWorkRunner callbackRunner) {
    callbackRunner_ = std::move(callbackRunner);
}

void CommonLibGameLifecycleSink::OnMessage(
    const SKSE::MessagingInterface::Message& message) {
    //  Registered directly as SKSE's raw callback (see
    //  dovahlink_bridge_plugin.cpp), so per ai/context/skse/cpp-style.md's
    //  "never allow an exception to escape a callback" this entire body --
    //  not just the part behind callbackRunner_'s containment -- must not let
    //  an exception reach SKSE.
    try {
        //  Every reachable case below assigns event before use; this default
        //  is an inert safety net against a future case that forgets to,
        //  matching OnRevert's own teardown event rather than an arbitrary
        //  enumerator.
        application::LifecycleEvent event = application::LifecycleEvent::kRevert;
        std::string rawDescription;
        switch (message.type) {
        case SKSE::MessagingInterface::kPreLoadGame:
            event = application::LifecycleEvent::kPreLoadGame;
            rawDescription = "kPreLoadGame";
            break;

        case SKSE::MessagingInterface::kPostLoadGame: {
            bool success = application::DecodePostLoadGameSuccess(message.data);
            event = success ? application::LifecycleEvent::kPostLoadGameSuccess
                            : application::LifecycleEvent::kPostLoadGameFailure;
            rawDescription = success ? "kPostLoadGame(true)" : "kPostLoadGame(false)";
            break;
        }

        case SKSE::MessagingInterface::kNewGame:
            event = application::LifecycleEvent::kNewGame;
            rawDescription = "kNewGame";
            break;

        default:
            return;
        }

        if (callbackRunner_) {
            (void)callbackRunner_(
                [this, event, description = std::move(rawDescription)] {
                    HandleEvent(event, description);
                });
        }
    } catch (...) {
        //  Swallowed at the SKSE callback boundary; there is no safe way to
        //  propagate this to SKSE, and lifecycle logging is diagnostic-only.
    }
}

void CommonLibGameLifecycleSink::OnRevert() {
    try {
        if (callbackRunner_) {
            (void)callbackRunner_([this] {
                HandleEvent(application::LifecycleEvent::kRevert, "Revert");
            });
        }
    } catch (...) {
        //  Same SKSE-callback-boundary containment as OnMessage above.
    }
}

void CommonLibGameLifecycleSink::HandleEvent(application::LifecycleEvent event,
                                             std::string_view rawDescription) {
    std::lock_guard<std::mutex> lifecycleLock(lifecycleMutex_);
    std::uint64_t sequence = sequence_.fetch_add(1, std::memory_order_relaxed);
    std::string thread = ThreadIdString();
    SKSE::log::info("[lifecycle #{} thread {}] raw={}", sequence, thread,
                    rawDescription);

    application::GameLifecycleTracker::Transition transition =
        tracker_.HandleEvent(event);
    application::ApplyLifecycleTransition(activePlayContext_, transition);

    SKSE::log::info(
        "[lifecycle #{} thread {}] invalidated={} newPlayContextId={}", sequence,
        thread, transition.contextInvalidated,
        transition.newPlayContextId.value_or("(none)"));
}

} //  namespace dovahlink::game_state
