#pragma once

#include <algorithm>
#include <array>
#include <cstddef>
#include <random>

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

//  TODO(stage4-file-extraction): Move this definition back to its own
//  identity/adapter_play_context_generator.cpp in the post-Stage-4
//  structural cleanup PR. Temporarily header-only to hold this PR's
//  changed-file count down; extraction only, no behavior change.
namespace {

///  A thread-local random engine, seeded once per thread from a
///  non-deterministic source. Keeps generation free of shared mutable state
///  across `AdapterPlayContextGenerator` instances or threads, without paying
///  the cost of reseeding on every call. Mirrors
///  `AdapterInstanceIdGenerator`'s own identical engine.
std::mt19937_64& RandomEngine() {
    thread_local std::mt19937_64 engine{std::random_device{}()};
    return engine;
}

} //  namespace

inline std::array<std::byte, 16> AdapterPlayContextGenerator::Generate() {
    std::uniform_int_distribution<int> byteDistribution(0, 255);
    std::array<std::byte, 16> playContextId{};
    //  All-zero is reserved elsewhere as an invariant no real generated value
    //  may ever collide with (see IpcFrameCodec::DecodePlayContextChanged);
    //  re-rolling on the (1 in 2^128) chance of drawing it keeps every byte
    //  still uniformly distributed, unlike forcing a fixed bit.
    do {
        std::ranges::generate(playContextId, [&] {
            return static_cast<std::byte>(byteDistribution(RandomEngine()));
        });
    } while (playContextId == std::array<std::byte, 16>{});
    return playContextId;
}

} //  namespace dovahlink::adapter::identity
