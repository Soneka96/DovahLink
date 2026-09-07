#pragma once

#include <optional>
#include <string>

namespace dovahlink::adapter::ipc {

///  The three distinguishable outcomes of one
///  `IAdapterIpcSession::SendTrustAdminRequest` call.
enum class TrustAdminRequestOutcome {
  ///  The host returned a correlated result before the request's bound elapsed.
  kCompleted,
  ///  The request was never successfully handed to the host: no authenticated
  ///  connection was
  ///  available, the outstanding-request bound was already reached, or the send
  ///  itself failed.
  ///  The host's own state is provably unaffected.
  kUnavailable,
  ///  The request was submitted to the host, or its submission could not be
  ///  ruled out (for
  ///  example this session closed or the connection ended while it was still
  ///  outstanding), but no
  ///  correlated result arrived before the bound elapsed. The host's own
  ///  mutation, if any, may
  ///  have already completed, may still be in flight, or may never have
  ///  started; this outcome
  ///  cannot distinguish those cases.
  kTimedOut,
};

///  Result of one `IAdapterIpcSession::SendTrustAdminRequest` call, delivered
///  to its `onResult` callback exactly once.
struct TrustAdminRequestResult {
  ///  Which of the three outcomes occurred. Defaults to `kUnavailable` so a
  ///  default-constructed result never leaves this indeterminate.
  TrustAdminRequestOutcome outcome = TrustAdminRequestOutcome::kUnavailable;
  ///  The host's formatted result text, populated only when `outcome ==
  ///  TrustAdminRequestOutcome::kCompleted`.
  std::optional<std::string> resultText;
};

} //  namespace dovahlink::adapter::ipc
