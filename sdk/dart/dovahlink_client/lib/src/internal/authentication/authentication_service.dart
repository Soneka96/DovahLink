import 'package:dovahlink_client_sdk/src/dovahlink_protocol_exception.dart';
import 'package:dovahlink_client_sdk/src/hello_result.dart';

/// Owns `hello`/authentication and credential-rejection recovery, per
/// `ai/context/sdk/architecture.md`'s "Internal composition". Resolves and persists this
/// installation's `clientId`, negotiates trust with the bridge, and recovers from a rejected
/// `trusted_device_credential` hello by discarding it and retrying once as `unpaired`.
abstract interface class AuthenticationService {
  /// This installation's stable client ID, or `null` before [hello] has resolved it.
  String? get clientId;

  /// Sends `hello` and negotiates the session. Resolves and persists this installation's
  /// `clientId` on first use, and automatically presents a stored trusted credential as
  /// `trusted_device_credential` for an ordinary reconnect. Admits `unpaired` both when no
  /// credential is stored yet and when a `CONFIRMING` pairing is still outstanding -- the bridge
  /// has not yet committed that credential as trusted, so it must not be presented as one. Once
  /// the new session's trust state is known, retransmits any retry-safe operation an earlier
  /// ordinary transport loss orphaned, provided the new session still satisfies its required
  /// trust state.
  /// @throws [DovahLinkProtocolException] if the bridge rejects authentication.
  Future<HelloResult> hello();

  /// Connects to [uri] and authenticates, recovering from a rejected `trusted_device_credential`
  /// hello (`revoked` or an unrecognized credential) by discarding it and retrying once as
  /// `unpaired` -- the bridge always accepts that, so a recoverable rejection never surfaces as a
  /// thrown exception here. [HelloResult.recoveredFromRejectedCredential] reports whether that
  /// happened and why, so a caller can still explain it to the user. A transport failure, a
  /// non-recoverable protocol rejection, or the retry attempt's own failure still throws normally.
  ///
  /// A no-op that returns the cached result of the last [hello] when this client is already
  /// connected and trusted -- the bridge's one-session-per-connection limit
  /// (`handshake_handler.cpp`'s `TryCreateSession`) rejects a second `hello` on a socket that
  /// already holds a session, so re-authenticating an already-trusted, still-open connection must
  /// not re-send one.
  /// @throws [DovahLinkConnectionException] if the socket cannot be established (initial or retry).
  /// @throws [DovahLinkProtocolException] if hello is rejected for a non-recoverable reason, or the
  ///     retry attempt is itself rejected.
  Future<HelloResult> authenticate(Uri uri);

  /// Discards the persisted pairing credential and recovery state while preserving [clientId], so
  /// the next [hello] presents `AuthMethod.unpaired` instead of a credential the bridge has
  /// already rejected. Call after a `trusted_device_credential` hello is rejected
  /// (`unauthenticated`/`revoked`) and before retrying -- this installation's identity is not
  /// itself invalid, only its stored credential. Does not touch the transport or in-memory
  /// connection state.
  Future<void> forgetCredential();
}
