import 'enums.dart';

/// The bootstrap information `DovahLinkClient.hello` returns on success.
class HelloResult {
  /// Creates a hello result.
  const HelloResult({required this.bridgeVersion, required this.trustState});

  /// The DovahLink Bridge/mod release version.
  final String bridgeVersion;

  /// The trust tier the session was admitted at.
  final DovahLinkTrustState trustState;
}
