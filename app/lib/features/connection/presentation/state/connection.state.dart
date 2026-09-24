import 'package:equatable/equatable.dart';
import 'package:meta/meta.dart';

import 'package:dovahlink_client/features/connection/domain/entities/host.entity.dart';
import 'package:dovahlink_client/shared/constants/constants.dart';

/// Immutable Redux state for the Host connection.
@immutable
class ConnectionState extends Equatable {
  /// The Hosts available to select.
  final List<Host> hosts;

  /// Creates connection state with an explicit Host list.
  const ConnectionState({this.hosts = const <Host>[]});

  /// Returns the state before a connection attempt starts, with the static default Host list
  /// until Host discovery exists.
  factory ConnectionState.initial() => ConnectionState(
    hosts: [Host(displayName: 'Local Host', uri: defaultHostUri)],
  );

  /// Returns a copy with selected values replaced.
  ConnectionState copyWith({List<Host>? hosts}) =>
      ConnectionState(hosts: hosts ?? this.hosts);

  /// See [Equatable.props].
  @override
  List<Object?> get props => [hosts];
}
