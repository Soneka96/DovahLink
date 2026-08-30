#include "dispatch/adapter_native_dispatcher.hpp"

namespace dovahlink::adapter::dispatch {

std::optional<std::vector<std::byte>>
AdapterNativeDispatcher::TryDispatch(std::uint32_t /*intentKey*/) {
  //  No production intent key is registered yet; a future phase adds real
  //  cases here as it adds approved event registrations and reads.
  return std::nullopt;
}

} //  namespace dovahlink::adapter::dispatch
