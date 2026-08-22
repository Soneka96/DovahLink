// ---- Connection lifecycle ----

/// The connection lifecycle phase of a `DovahLinkClient`.
enum DovahLinkConnectionState {
  /// No socket is connected.
  disconnected,

  /// Establishing the socket connection.
  connecting,

  /// A socket is connected.
  connected,

  /// An administrator deliberately invalidated the authenticated session (`revoked`, `blocked`,
  /// `trustReset`, or `factoryReset`); see `DovahLinkClient.invalidationReason`. Terminal for the
  /// current session -- recovery is only ever a fresh, explicit `DovahLinkClient.connect`, never
  /// automatic.
  administrativelyInvalidated,
}

/// The client's own trust standing with the bridge, established once `DovahLinkClient.hello`
/// succeeds and possibly upgraded by `DovahLinkClient.acknowledgeTrustedCredential` or
/// `DovahLinkClient.recoverPendingPairing`.
enum DovahLinkTrustState {
  /// Admitted without a trust credential; restricted to the pairing message set.
  unpaired,

  /// Fully trusted, either by a `trusted_device_credential` hello or a completed pairing.
  trusted,
}

/// The wire value of `hello.auth.method` (`protocol/schema/README.md`'s `hello`).
enum AuthMethod {
  /// No credential presented yet; admits a trust-restricted session solely to run pairing.
  unpaired,

  /// Explicit developer authentication via a locally configured one-time token.
  oneTimeLocalToken,

  /// The persisted credential a completed pairing issued.
  trustedDeviceCredential,
}

/// Why a `trusted_device_credential` hello was rejected and `DovahLinkClient.authenticate`
/// recovered by discarding the credential and retrying as `unpaired`.
enum CredentialRejectionReason {
  /// The presented credential belonged to a clientId the bridge explicitly revoked.
  revoked,

  /// The presented credential did not match any credential the bridge currently trusts.
  unrecognized,
}

// ---- Pairing ----

/// The bridge's report of pairing availability, from `DovahLinkClient.requestPairing`.
enum PairingAvailability {
  /// No challenge is currently active, and none was just started.
  unavailable,

  /// A fresh six-digit code was just generated and shown in Skyrim.
  available,

  /// A challenge is already active; no new code was generated.
  inProgress,

  /// A different clientId currently owns the active challenge or pending credential.
  otherDevicePairing,
}

/// The outcome of `DovahLinkClient.requestPairingRenotify`.
enum PairingRenotifyStatus {
  /// The active challenge's code was redisplayed in Skyrim.
  renotified,

  /// Redisplay was rejected; the renotify cooldown has not elapsed yet.
  cooldown,

  /// No challenge or pending credential is owned by this client.
  alreadyIdle,
}

/// The outcome of `DovahLinkClient.cancelPairing`.
enum PairingCancelStatus {
  /// An owned active challenge or pending credential was cleared.
  cancelled,

  /// Nothing was owned; cancellation was a no-op.
  alreadyIdle,
}

// ---- Request policy ----

/// A centralized bounded timeout category a request is classified against, per
/// `ai/context/sdk/api-design.md`'s "Request retry safety, session requirement, and timeout
/// class". Durations live in `shared/constants.dart`'s `kTimeoutClassDurations` rather than here,
/// so a call site's classification and the SDK's tuned duration policy stay independently
/// editable.
enum TimeoutClass {
  /// A fast administrative round trip (a query, an acknowledgement).
  short,

  /// The common case: a request that may involve a little more bridge-side work than [short].
  normal,

  /// Reserved for a request expected to take meaningfully longer than [normal]. Unused until a
  /// real operation actually needs it.
  heavy,
}

// ---- Trust invalidation ----

/// The typed reason `DovahLinkClient.invalidationReason` reports once
/// `DovahLinkConnectionState.administrativelyInvalidated` is reached -- the parsed form of an
/// incoming `session_invalidated.reason` (`protocol/schema/README.md`'s `session_invalidated`).
/// Never durable authoritative trust state; the Bridge remains the sole authority.
enum AdministrativeInvalidationReason {
  /// The presented device credential's `clientId` was explicitly revoked.
  revoked,

  /// The presented `clientId` is a currently blocked Known Device.
  blocked,

  /// The Known Device this session belonged to became Revoked (a Trusted device's trust reset).
  trustReset,

  /// The Bridge's Known Device trust store was cleared. May end a developer-token session even
  /// though the configured developer token remains valid.
  factoryReset,
}
