#pragma once

#include <functional>

namespace dovahlink::application {

///  Owns one potentially move-only operation submitted at a runtime boundary.
using ContainedWork = std::move_only_function<void()>;

///  Executes one synchronous runtime-boundary operation without allowing an
///  exception to escape.
///  @return `true` when the operation was admitted and completed successfully.
using ContainedWorkRunner = std::function<bool(ContainedWork)>;

} //  namespace dovahlink::application
