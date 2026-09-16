#pragma once

#include "ipc/adapter_ipc_peer_proof_provider.hpp"

#include <cstddef>
#include <mutex>
#include <vector>

namespace dovahlink::adapter::ipc {

///  @copydoc IAdapterIpcPeerProofProvider
///  Unlike `FixedAdapterIpcPeerProofProvider`, this provider's token can be
///  reconfigured after construction, once the real value is discovered via
///  the packaged-host startup rendezvous. Currently used only by the
///  real-process integration test, which discovers that value itself; a
///  fixture that already knows a fixed token uses
///  `FixedAdapterIpcPeerProofProvider` instead.
class SettableAdapterIpcPeerProofProvider final
    : public IAdapterIpcPeerProofProvider {
  public:
    ///  Creates a provider with no token set; `Token()` returns an empty
    ///  vector until `SetToken` is called.
    SettableAdapterIpcPeerProofProvider() = default;

    ///  @copydoc IAdapterIpcPeerProofProvider::Token
    std::vector<std::byte> Token() const override;

    ///  Reconfigures the token this provider returns. Safe to call
    ///  concurrently with `Token()` from another thread.
    ///  @param token The new proof value to return from subsequent `Token()`
    ///  calls.
    void SetToken(std::vector<std::byte> token);

  private:
    ///  Guards `token_`.
    mutable std::mutex mutex_;
    ///  The configured proof value, owned.
    std::vector<std::byte> token_;
};

} //  namespace dovahlink::adapter::ipc
