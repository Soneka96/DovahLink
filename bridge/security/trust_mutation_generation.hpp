#pragma once

#include <cstdint>

namespace dovahlink::security {

///  Monotonic generation for administrative trust mutations that invalidate
///  pending pairing commits.
using TrustMutationGeneration = std::uint64_t;

} //  namespace dovahlink::security
