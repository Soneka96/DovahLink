#pragma once

#include "runtime/adapter_task_marshaller.hpp"

namespace dovahlink::adapter::runtime {

///  @copydoc IAdapterTaskMarshaller
class CommonLibAdapterTaskMarshaller final : public IAdapterTaskMarshaller {
public:
  ///  @copydoc IAdapterTaskMarshaller::RunOnGameThread
  void RunOnGameThread(std::function<void()> task) override;
};

} //  namespace dovahlink::adapter::runtime
