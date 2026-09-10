/// Shares this installation's resolved `clientId` between `AuthenticationService` and
/// `PendingOperationTransmitter` without either depending on the other, per
/// `ai/context/sdk/architecture.md`'s "Dependency injection" -- a direct dependency between them
/// would create a construction-order cycle, since `IAuthenticationService` depends on
/// `IRequestService`, which privately owns `PendingOperationTransmitter`. A plain, no-interface
/// supporting object per "Not everything is a Service": like `PendingOperationBookkeeping`, it
/// holds one value and the single transition it can undergo, and makes no decision of its own.
class ClientIdCache {
  /// The cached client ID, or `null` before the first successful `hello()` resolves one.
  String? _clientId;

  /// This installation's stable client ID, or `null` before [set] has been called.
  String? get clientId => _clientId;

  /// Records the resolved `clientId`. Called exactly once per successful `hello()`, by
  /// `AuthenticationService`.
  void set(String clientId) => _clientId = clientId;
}
