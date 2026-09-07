#pragma once

#include <cstdint>
#include <optional>
#include <string>

#include "ipc/ipc_enums.hpp"

namespace dovahlink::adapter::ipc {

///  Sent by the adapter to forward one Papyrus-originated trust-administration
///  command to the host, the sole mutation authority. Every operation carries
///  only the argument its closed shape requires; see
///  `ai/context/protocol/security.md`'s "Trust administration surface" for the
///  exact operation set this mirrors.
struct IpcTrustAdminRequestMessage {
  ///  The nonzero request identity the host's `IpcTrustAdminResultMessage`
  ///  reply correlates to.
  std::uint64_t correlationId = 0;
  ///  Which trust-administration command this request carries.
  TrustAdminOperation operation = TrustAdminOperation::kHelp;
  ///  The device scope for `TrustAdminOperation::kList`; otherwise unset.
  std::optional<TrustAdminListScope> listScope;
  ///  The five-digit device identity for `TrustAdminOperation::kRevoke`,
  ///  `kBlock`, `kUnblock`, or `kForget`; otherwise unset.
  std::optional<std::string> shortId;
  ///  The six-digit Factory Reset confirmation code for
  ///  `TrustAdminOperation::kConfirmReset`; otherwise unset.
  std::optional<std::string> confirmationCode;

  ///  Structural equality over every field.
  bool operator==(const IpcTrustAdminRequestMessage &) const = default;
};

} //  namespace dovahlink::adapter::ipc
