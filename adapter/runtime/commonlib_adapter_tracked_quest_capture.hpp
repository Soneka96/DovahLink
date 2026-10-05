#pragma once

#include "capture/tracked_quest_page_request.hpp"
#include "dispatch/sample_capture_result.hpp"

namespace dovahlink::adapter::runtime {

///  Reads and encodes one bounded tracked-quest page from Skyrim state.
///  @param request The page kind and cursor to capture.
///  @return An available encoded page, unavailable engine facts, or unsupported arguments.
dispatch::SampleCaptureResult CaptureCommonLibTrackedQuestPage(
    const capture::TrackedQuestPageRequest& request);

} //  namespace dovahlink::adapter::runtime
