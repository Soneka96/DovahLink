#include "identity/adapter_play_context_generator.hpp"

#include <algorithm>
#include <random>

namespace dovahlink::adapter::identity {

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

std::array<std::byte, 16> AdapterPlayContextGenerator::Generate() {
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
