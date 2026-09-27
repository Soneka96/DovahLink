import 'package:equatable/equatable.dart';
import 'package:fpdart/fpdart.dart';
import 'package:meta/meta.dart';

import 'package:dovahlink_client/features/connection/domain/entities/host.entity.dart';
import 'package:dovahlink_client/shared/constants/enums.dart';

/// Immutable Redux state for the Host connection.
@immutable
class ConnectionState extends Equatable {
  /// The Hosts available to select.
  final List<Host> hosts;

  /// The Host the user most recently selected to pair or connect with, or `null` before any
  /// selection.
  final Host? selectedHost;

  /// The latest discovery operation's state.
  final ConnectionDiscoveryStatus discoveryStatus;

  /// The latest discovery error, or `null` when discovery did not fail.
  final Object? discoveryError;

  /// Creates connection state with an explicit Host list and an optional selected Host.
  const ConnectionState({
    this.hosts = const <Host>[],
    this.selectedHost,
    this.discoveryStatus = ConnectionDiscoveryStatus.idle,
    this.discoveryError,
  });

  /// Returns the initial connection state before Host discovery or selection.
  factory ConnectionState.initial() => const ConnectionState();

  /// Returns a copy with selected values replaced. [selectedHost] is an [Option] so an omitted,
  /// cleared, and set value stay distinct.
  ConnectionState copyWith({
    List<Host>? hosts,
    Option<Host>? selectedHost,
    ConnectionDiscoveryStatus? discoveryStatus,
    Option<Object>? discoveryError,
  }) => ConnectionState(
    hosts: hosts ?? this.hosts,
    selectedHost: selectedHost == null
        ? this.selectedHost
        : selectedHost.toNullable(),
    discoveryStatus: discoveryStatus ?? this.discoveryStatus,
    discoveryError: discoveryError == null
        ? this.discoveryError
        : discoveryError.toNullable(),
  );

  /// See [Equatable.props].
  @override
  List<Object?> get props => [
    hosts,
    selectedHost,
    discoveryStatus,
    discoveryError,
  ];
}
