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

  /// The latest app-mapped projection emitted by the SDK's authoritative Known Host state.
  final Host? knownHost;

  /// The latest discovery operation's state.
  final ConnectionDiscoveryStatus discoveryStatus;

  /// The semantic reason the latest discovery operation failed, or `null` when it did not fail.
  final ConnectionFailureReason? discoveryFailure;

  /// Creates connection state with an explicit Host list and an optional selected Host.
  /// @param knownHost The latest SDK-observed Known Host projection.
  const ConnectionState({
    this.hosts = const <Host>[],
    this.selectedHost,
    this.knownHost,
    this.discoveryStatus = ConnectionDiscoveryStatus.idle,
    this.discoveryFailure,
  });

  /// Returns the initial connection state before Host discovery or selection.
  factory ConnectionState.initial() => const ConnectionState();

  /// Returns a copy with selected values replaced. [selectedHost] is an [Option] so an omitted,
  /// cleared, and set value stay distinct.
  /// @param knownHost The observed Known Host to set or clear, or `null` to keep it.
  ConnectionState copyWith({
    List<Host>? hosts,
    Option<Host>? selectedHost,
    Option<Host>? knownHost,
    ConnectionDiscoveryStatus? discoveryStatus,
    Option<ConnectionFailureReason>? discoveryFailure,
  }) => ConnectionState(
    hosts: hosts ?? this.hosts,
    selectedHost: selectedHost == null
        ? this.selectedHost
        : selectedHost.toNullable(),
    knownHost: knownHost == null ? this.knownHost : knownHost.toNullable(),
    discoveryStatus: discoveryStatus ?? this.discoveryStatus,
    discoveryFailure: discoveryFailure == null
        ? this.discoveryFailure
        : discoveryFailure.toNullable(),
  );

  /// See [Equatable.props].
  @override
  List<Object?> get props => [
    hosts,
    selectedHost,
    knownHost,
    discoveryStatus,
    discoveryFailure,
  ];
}
