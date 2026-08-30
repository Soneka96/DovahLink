#pragma once

#include <cstddef>
#include <cstdint>
#include <optional>
#include <vector>

namespace dovahlink::adapter::dispatch {

///  The adapter's single, generic key-to-Skyrim translation seam: the last
///  step before Skyrim, per `ai/context/adapter/architecture.md`. A host-
///  directed event key or sample token maps here to one approved event
///  registration or synchronous read function; this concept registers no real
///  key, matching this phase's non-goal against speculative domain
///  registries. A future phase that adds a real key may need to move this
///  dispatcher (or delegate from it) into a CommonLib-linked module at that
///  time, the same way `bridge/`'s `RegisteredStateAreaPolicy` stays Skyrim-
///  independent while its actual game-state adapters do not.
class IAdapterNativeDispatcher {
public:
  virtual ~IAdapterNativeDispatcher() = default;

  ///  Performs the one approved translation for a host-directed intent key,
  ///  synchronously on the calling thread. Callers are responsible for
  ///  calling this only from the Skyrim game thread once a real translation
  ///  is registered for a key.
  ///  @param intentKey The host-owned event key or sample token.
  ///  @return The captured value, or `std::nullopt` for an intent key with no
  ///  approved translation.
  virtual std::optional<std::vector<std::byte>>
  TryDispatch(std::uint32_t intentKey) = 0;
};

///  @copydoc IAdapterNativeDispatcher
class AdapterNativeDispatcher final : public IAdapterNativeDispatcher {
public:
  ///  @copydoc IAdapterNativeDispatcher::TryDispatch
  std::optional<std::vector<std::byte>>
  TryDispatch(std::uint32_t intentKey) override;
};

} //  namespace dovahlink::adapter::dispatch
