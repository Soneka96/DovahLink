import 'package:equatable/equatable.dart';
import 'package:fpdart/fpdart.dart';
import 'package:meta/meta.dart';

import 'package:dovahlink_client/features/connection/domain/entities/host.entity.dart';
import 'package:dovahlink_client/shared/constants/constants.dart';

/// Immutable Redux state for the Host connection.
@immutable
class ConnectionState extends Equatable {
  /// The Hosts available to select.
  final List<Host> hosts;

  /// The Host the user most recently selected to pair or connect with, or `null` before any
  /// selection.
  final Host? selectedHost;

  /// Creates connection state with an explicit Host list and an optional selected Host.
  const ConnectionState({this.hosts = const <Host>[], this.selectedHost});

  /// Returns the state before a connection attempt starts, with the static default Host list
  /// until Host discovery exists and no Host selected.
  factory ConnectionState.initial() => ConnectionState(
    hosts: [Host(displayName: 'Local Host', uri: defaultHostUri)],
  );

  /// Returns a copy with selected values replaced. [selectedHost] is an [Option] so an omitted,
  /// cleared, and set value stay distinct.
  ConnectionState copyWith({List<Host>? hosts, Option<Host>? selectedHost}) =>
      ConnectionState(
        hosts: hosts ?? this.hosts,
        selectedHost: selectedHost == null
            ? this.selectedHost
            : selectedHost.toNullable(),
      );

  /// See [Equatable.props].
  @override
  List<Object?> get props => [hosts, selectedHost];
}
