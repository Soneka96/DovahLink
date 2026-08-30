#pragma once

#include <cstddef>
#include <vector>

namespace dovahlink::adapter::ipc {

///  Supplies the peer-ownership proof this adapter presents in its
///  `IpcHelloMessage`, per the D1 divergence's same-package proof: host and
///  adapter are shipped as one atomic package with no negotiated protocol
///  version, so this shared value is what proves a connecting peer belongs
///  to the matching packaged pair. The host generates the value; process
///  launch is responsible for handing the same value to the adapter it
///  starts (`ai/context/host/architecture.md`'s "Framing and package
///  ownership") -- a later concept's process-lifecycle work, not this one.
class IAdapterIpcPeerProofProvider {
public:
  virtual ~IAdapterIpcPeerProofProvider() = default;

  ///  The proof bytes to present in this adapter's Hello.
  virtual std::vector<std::byte> Token() const = 0;
};

///  @copydoc IAdapterIpcPeerProofProvider
class FixedAdapterIpcPeerProofProvider final
    : public IAdapterIpcPeerProofProvider {
public:
  ///  Creates a provider that always returns `token`.
  explicit FixedAdapterIpcPeerProofProvider(std::vector<std::byte> token);

  ///  @copydoc IAdapterIpcPeerProofProvider::Token
  std::vector<std::byte> Token() const override;

private:
  ///  The configured proof value.
  std::vector<std::byte> token_;
};

} //  namespace dovahlink::adapter::ipc
