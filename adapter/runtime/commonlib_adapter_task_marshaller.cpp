#include "SKSE/SKSE.h"

#include "runtime/commonlib_adapter_task_marshaller.hpp"

#include <utility>

namespace dovahlink::adapter::runtime {

void CommonLibAdapterTaskMarshaller::RunOnGameThread(
    std::function<void()> task) {
  SKSE::GetTaskInterface()->AddTask(std::move(task));
}

} //  namespace dovahlink::adapter::runtime
