import 'package:equatable/equatable.dart';

import 'package:dovahlink_client/features/connection/domain/entities/bridge.entity.dart';

/// Requests navigating to pairing for the selected Bridge.
class ConnectionBridgeSelectedAction extends Equatable {
  /// Creates a Bridge-selection action.
  const ConnectionBridgeSelectedAction(this.bridge);

  /// The Bridge the user selected.
  final BridgeEntity bridge;

  /// See [Equatable.props].
  @override
  List<Object?> get props => [bridge];
}
