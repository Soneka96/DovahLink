#include "SKSE/SKSE.h"

#include "game_state/commonlib_task_marshaller.hpp"

#include <utility>

namespace dovahlink::game_state {

void CommonLibTaskMarshaller::RunOnGameThread(std::function<void()> task) {
    SKSE::GetTaskInterface()->AddTask(std::move(task));
}

} //  namespace dovahlink::game_state
