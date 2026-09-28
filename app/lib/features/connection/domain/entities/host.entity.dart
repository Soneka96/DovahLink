import 'package:equatable/equatable.dart';

/// A DovahLink Host a client can select and connect or pair with.
class Host extends Equatable {
  /// Stable DovahLink Host installation identity reported by the SDK.
  final String hostId;

  /// User-facing name for this Host.
  final String displayName;

  /// The Host's WebSocket endpoint.
  final Uri uri;

  /// Creates a Host identity.
  const Host({
    required this.hostId,
    required this.displayName,
    required this.uri,
  });

  /// See [Equatable.props].
  @override
  List<Object?> get props => [hostId, displayName, uri];
}
