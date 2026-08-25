#pragma once

#include <cstdint>

namespace dovahlink::security {

///  Mutation fence captured by a pending pairing before it can become trusted.
///  `global` advances after Reset Trust or Factory Reset; `client` advances
///  after a successful Revoke or Block for the pending pairing's client. A
///  pairing is stale when either component no longer matches the trust store.
struct TrustMutationGeneration {
    ///  Generation shared by mutations that invalidate every pending pairing.
    std::uint64_t global = 0;

    ///  Generation for mutations that invalidate this client's pending pairing.
    std::uint64_t client = 0;

    ///  Compares both invalidation scopes.
    friend bool operator==(const TrustMutationGeneration&,
                           const TrustMutationGeneration&) = default;
};

} //  namespace dovahlink::security
