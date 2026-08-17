import 'package:equatable/equatable.dart';

/// A DovahLink Bridge a client can select and connect or pair with.
class BridgeEntity extends Equatable {
  /// Creates a Bridge identity.
  const BridgeEntity({required this.displayName, required this.uri});

  /// User-facing name for this Bridge.
  final String displayName;

  /// The Bridge's WebSocket endpoint.
  final Uri uri;

  /// See [Equatable.props].
  @override
  List<Object?> get props => [displayName, uri];
}
