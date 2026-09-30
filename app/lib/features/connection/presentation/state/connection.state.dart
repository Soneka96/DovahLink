import 'package:equatable/equatable.dart';
import 'package:fpdart/fpdart.dart';
import 'package:meta/meta.dart';

import 'package:dovahlink_client/features/connection/domain/entities/host.entity.dart';
import 'package:dovahlink_client/features/connection/domain/entities/known_host.entity.dart';
import 'package:dovahlink_client/shared/constants/enums.dart';

/// Immutable Redux state for the Host connection.
@immutable
class ConnectionState extends Equatable {
  /// The Hosts available to select.
  final List<Host> hosts;

  /// The Host the user most recently selected to pair or connect with, or `null` before any
  /// selection.
  final Host? selectedHost;

  /// Whether [selectedHost] represents a discovery candidate or durable Known Host intent.
  final ConnectionHostSelectionSource selectedHostSource;

  /// Candidate ID whose selection is retained until the SDK confirms a Known Host relationship.
  final String? pendingPairingHostId;

  /// The app-mapped complete projection emitted by the SDK's authoritative Known Host state.
  final List<KnownHost> knownHosts;

  /// Whether [knownHosts] is an observed, currently healthy SDK projection.
  final KnownHostsObservationStatus knownHostsStatus;

  /// The latest discovery operation's state.
  final ConnectionDiscoveryStatus discoveryStatus;

  /// The semantic reason the latest discovery operation failed, or `null` when it did not fail.
  final ConnectionFailureReason? discoveryFailure;

  /// Creates connection state with an explicit Host list and an optional selected Host.
  /// @param pendingPairingHostId The candidate selection awaiting SDK Known Host confirmation.
  /// @param knownHosts The latest complete SDK-observed Known Host projection.
  /// @param knownHostsStatus The health of the SDK Known Hosts observation.
  const ConnectionState({
    this.hosts = const <Host>[],
    this.selectedHost,
    this.selectedHostSource = ConnectionHostSelectionSource.candidate,
    this.pendingPairingHostId,
    this.knownHosts = const <KnownHost>[],
    this.knownHostsStatus = KnownHostsObservationStatus.loading,
    this.discoveryStatus = ConnectionDiscoveryStatus.idle,
    this.discoveryFailure,
  });

  /// Returns the initial connection state before Host discovery or selection.
  factory ConnectionState.initial() => const ConnectionState();

  /// Returns a copy with selected values replaced. [selectedHost] is an [Option] so an omitted,
  /// cleared, and set value stay distinct; Known Hosts are replaced as a complete list.
  /// @param knownHosts The complete observed Known Hosts collection, or `null` to keep it.
  /// @param pendingPairingHostId The pending candidate Host ID, or `null` to keep it.
  ConnectionState copyWith({
    List<Host>? hosts,
    Option<Host>? selectedHost,
    ConnectionHostSelectionSource? selectedHostSource,
    Option<String>? pendingPairingHostId,
    List<KnownHost>? knownHosts,
    KnownHostsObservationStatus? knownHostsStatus,
    ConnectionDiscoveryStatus? discoveryStatus,
    Option<ConnectionFailureReason>? discoveryFailure,
  }) => ConnectionState(
    hosts: hosts ?? this.hosts,
    selectedHost: selectedHost == null
        ? this.selectedHost
        : selectedHost.toNullable(),
    selectedHostSource: selectedHostSource ?? this.selectedHostSource,
    pendingPairingHostId: pendingPairingHostId == null
        ? this.pendingPairingHostId
        : pendingPairingHostId.toNullable(),
    knownHosts: knownHosts ?? this.knownHosts,
    knownHostsStatus: knownHostsStatus ?? this.knownHostsStatus,
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
    selectedHostSource,
    pendingPairingHostId,
    knownHosts,
    knownHostsStatus,
    discoveryStatus,
    discoveryFailure,
  ];
}
