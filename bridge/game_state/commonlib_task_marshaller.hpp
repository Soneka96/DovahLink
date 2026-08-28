#pragma once

#include "application/task_marshaller.hpp"

namespace dovahlink::game_state {

///  @copydoc dovahlink::application::ITaskMarshaller
class CommonLibTaskMarshaller final : public application::ITaskMarshaller {
  public:
    ///  @copydoc dovahlink::application::ITaskMarshaller::RunOnGameThread
    void RunOnGameThread(std::function<void()> task) override;
};

} //  namespace dovahlink::game_state
