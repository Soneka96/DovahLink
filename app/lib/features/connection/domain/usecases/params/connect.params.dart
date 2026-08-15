import 'package:equatable/equatable.dart';
import 'package:meta/meta.dart';

/// Parameters for connecting to the bridge.
@immutable
class ConnectParams extends Equatable {
  /// One-time credential used to authenticate the bridge session.
  final String token;

  /// This installation's persisted logical client identity, supplied in the
  /// v2 hello payload once negotiation reaches protocol version 2.
  final String clientId;

  /// Protocol versions this client can negotiate; the bridge selects the
  /// highest mutually supported version.
  final List<int> supportedProtocolVersions;

  /// Creates connection parameters.
  const ConnectParams({
    required this.token,
    required this.clientId,
    this.supportedProtocolVersions = const [1, 2],
  });

  /// See [Equatable.props].
  @override
  List<Object?> get props => [token, clientId, supportedProtocolVersions];
}
