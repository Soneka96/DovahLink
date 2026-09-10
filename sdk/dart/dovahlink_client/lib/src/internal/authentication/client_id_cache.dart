/// Shares this installation's resolved `clientId` between `AuthenticationService` and
/// `PendingOperationTransmitter` without either depending on the other, per
/// `ai/context/sdk/architecture.md`'s "Dependency injection" -- a direct dependency between them
/// would create a construction-order cycle, since `IAuthenticationService` depends on
/// `IRequestService`, which privately owns `PendingOperationTransmitter`. A plain, no-interface
/// supporting object per "Not everything is a Service": like `PendingOperationBookkeeping`, it
/// holds one value and the single transition it can undergo, and makes no decision of its own.
class ClientIdCache {
  /// The cached client ID, or `null` before [set] has first been called.
  String? _clientId;

  /// This installation's stable client ID, or `null` before [set] has first been called.
  String? get clientId => _clientId;

  /// Records the `clientId` resolved for a `hello` attempt. Called by `AuthenticationService`
  /// before every `hello` request it sends -- the first one and every subsequent reconnect --
  /// not only after a successful one: the value represents this installation's stable identity,
  /// not authenticated-session state, so it is safe to read as soon as it is set regardless of
  /// whether that attempt's `hello` ultimately succeeds.
  void set(String clientId) => _clientId = clientId;
}
