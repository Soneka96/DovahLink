#include "ipc/adapter_ipc_peer_proof_provider.hpp"

#include <utility>

namespace dovahlink::adapter::ipc {

FixedAdapterIpcPeerProofProvider::FixedAdapterIpcPeerProofProvider(
    std::vector<std::byte> token)
    : token_(std::move(token)) {}

std::vector<std::byte> FixedAdapterIpcPeerProofProvider::Token() const {
  return token_;
}

} //  namespace dovahlink::adapter::ipc
