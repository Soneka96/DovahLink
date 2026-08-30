#include "dispatch/adapter_native_dispatcher.hpp"

#include <catch2/catch_test_macros.hpp>

#include <cstdint>
#include <limits>

using dovahlink::adapter::dispatch::AdapterNativeDispatcher;

TEST_CASE("AdapterNativeDispatcher has no registered translation yet, for "
          "any intent key",
          "[dispatch][adapter_native_dispatcher]") {
  AdapterNativeDispatcher dispatcher;

  for (std::uint32_t intentKey :
       {std::uint32_t{0}, std::uint32_t{1}, std::uint32_t{42},
        std::numeric_limits<std::uint32_t>::max()}) {
    CHECK_FALSE(dispatcher.TryDispatch(intentKey).has_value());
  }
}
