import '../shared/enums.dart';

/// Upgrades trust standing alone, for a collaborator that never connects or disconnects -- see
/// `ai/context/sdk/architecture.md`'s "Internal composition". Implemented by [ClientSession].
abstract interface class SessionTrustWriter {
  /// Upgrades the current session's trust standing to [DovahLinkTrustState.trusted].
  void markTrusted();
}
