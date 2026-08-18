// ---- Connection lifecycle ----

/// The connection lifecycle phase of a `DovahLinkClient`.
enum DovahLinkConnectionState {
  /// No socket is connected.
  disconnected,

  /// Establishing the socket connection.
  connecting,

  /// A socket is connected.
  connected,
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
