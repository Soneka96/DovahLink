/// Upgrades trust standing, a privileged capability injected only into `PairingServiceImpl`, per
/// `ai/context/sdk/architecture.md`'s "Composing narrow authority". Nothing else may ever upgrade
/// trust standing.
abstract interface class SessionTrustService {
  /// Upgrades the current session's trust standing to `DovahLinkTrustState.trusted`.
  void markTrusted();
}
