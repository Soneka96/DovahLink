#include "ipc/settable_adapter_ipc_peer_proof_provider.hpp"

#include <utility>

namespace dovahlink::adapter::ipc {

std::vector<std::byte> SettableAdapterIpcPeerProofProvider::Token() const {
  std::lock_guard<std::mutex> lock(mutex_);
  return token_;
}

void SettableAdapterIpcPeerProofProvider::SetToken(
    std::vector<std::byte> token) {
  std::lock_guard<std::mutex> lock(mutex_);
  token_ = std::move(token);
}

} //  namespace dovahlink::adapter::ipc
