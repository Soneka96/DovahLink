import 'package:equatable/equatable.dart';

/// A DovahLink Host a client can select and connect or pair with.
class Host extends Equatable {
  /// Creates a Host identity.
  const Host({required this.displayName, required this.uri});

  /// User-facing name for this Host.
  final String displayName;

  /// The Host's WebSocket endpoint.
  final Uri uri;

  /// See [Equatable.props].
  @override
  List<Object?> get props => [displayName, uri];
}
