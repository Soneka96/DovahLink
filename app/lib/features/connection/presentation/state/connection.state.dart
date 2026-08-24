import 'package:equatable/equatable.dart';
import 'package:meta/meta.dart';

import 'package:dovahlink_client/features/connection/domain/entities/bridge.entity.dart';
import 'package:dovahlink_client/shared/constants/constants.dart';

/// Immutable Redux state for the bridge connection.
@immutable
class ConnectionState extends Equatable {
  /// Creates connection state with an explicit Bridge list.
  const ConnectionState({this.bridges = const <BridgeEntity>[]});

  /// Returns the state before a connection attempt starts, with the static default Bridge list
  /// until Bridge discovery exists.
  factory ConnectionState.initial() => ConnectionState(
    bridges: [BridgeEntity(displayName: 'Local Bridge', uri: defaultBridgeUri)],
  );

  /// The Bridges available to select.
  final List<BridgeEntity> bridges;

  /// Returns a copy with selected values replaced.
  ConnectionState copyWith({List<BridgeEntity>? bridges}) =>
      ConnectionState(bridges: bridges ?? this.bridges);

  /// See [Equatable.props].
  @override
  List<Object?> get props => [bridges];
}
