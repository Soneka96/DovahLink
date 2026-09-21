#pragma once

#include <array>
#include <cstddef>

namespace dovahlink::adapter::identity {

///  Generates a fresh play-context identity for a genuinely new or reloaded
///  Skyrim save, per `roadmap/04-live-state-synchronization-foundation.md`'s
///  "Real capture and host integration". Not security-sensitive: this
///  identifies a loaded-game timeline for capture provenance and staleness
///  checks, distinct from the separate peer-proof token that actually
///  establishes trust. Returns the same bare 16-byte shape
///  `IAdapterIpcSession::SendPlayContextChanged` and
///  `capture::AdapterCaptureWorkItem::playContextId` already use, rather than
///  a new wrapper type.
class IAdapterPlayContextGenerator {
  public:
    virtual ~IAdapterPlayContextGenerator() = default;

    ///  Generates a new, randomly generated play-context identity.
    ///  @return A uniformly random 16-byte value, never all-zero: that value
    ///  is reserved as an invariant no real generated id may ever collide
    ///  with.
    virtual std::array<std::byte, 16> Generate() = 0;
};

///  @copydoc IAdapterPlayContextGenerator
class AdapterPlayContextGenerator final : public IAdapterPlayContextGenerator {
  public:
    ///  @copydoc IAdapterPlayContextGenerator::Generate
    std::array<std::byte, 16> Generate() override;
};

} //  namespace dovahlink::adapter::identity
