#pragma once

#include <cstdint>

#include "capture/tracked_quest_page_request.hpp"

namespace dovahlink::adapter::ipc {

///  Requests one bounded tracked-quest fact page from the Skyrim Adapter.
struct IpcReadTrackedQuestPageMessage {
    ///  The nonzero request identity used for cancellation and response routing.
    std::uint64_t correlationId = 0;
    ///  The page operation and its bounded cursor arguments.
    capture::TrackedQuestPageRequest request;

    ///  Structural equality over every field.
    bool operator==(const IpcReadTrackedQuestPageMessage&) const = default;
};

} //  namespace dovahlink::adapter::ipc
