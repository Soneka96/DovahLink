import 'package:equatable/equatable.dart';
import 'package:fpdart/fpdart.dart';
import 'package:meta/meta.dart';

import 'package:dovahlink_client/features/connection/domain/entities/bridge.entity.dart';
import 'package:dovahlink_client/features/connection/domain/entities/connection_session.entity.dart';
import 'package:dovahlink_client/shared/constants/constants.dart';
import 'package:dovahlink_client/shared/constants/enums.dart';

/// Immutable Redux state for the bridge connection.
@immutable
class ConnectionState extends Equatable {
  /// Creates connection state with explicit lifecycle values. [bridges] defaults to empty since
  /// only [ConnectionState.initial] needs to populate the real static Bridge list -- every other
  /// construction site exercises a lifecycle transition unrelated to it.
  const ConnectionState({
    required this.phase,
    required this.session,
    required this.error,
    this.bridges = const <BridgeEntity>[],
  });

  /// Returns the state before a connection attempt starts, with the static default Bridge list
  /// until Bridge discovery exists.
  factory ConnectionState.initial() => ConnectionState(
    phase: ConnectionPhase.disconnected,
    session: null,
    error: null,
    bridges: [BridgeEntity(displayName: 'Local Bridge', uri: defaultBridgeUri)],
  );

  /// The current user-visible connection phase.
  final ConnectionPhase phase;

  /// The active negotiated session, or `null` when none exists.
  final ConnectionSessionEntity? session;

  /// The most recent user-safe connection error, or `null`.
  final String? error;

  /// The Bridges available to select.
  final List<BridgeEntity> bridges;

  /// Returns a copy with selected values replaced.
  ConnectionState copyWith({
    ConnectionPhase? phase,
    Option<ConnectionSessionEntity>? session,
    Option<String>? error,
    List<BridgeEntity>? bridges,
  }) => ConnectionState(
    phase: phase ?? this.phase,
    session: session == null ? this.session : session.toNullable(),
    error: error == null ? this.error : error.toNullable(),
    bridges: bridges ?? this.bridges,
  );

  /// See [Equatable.props].
  @override
  List<Object?> get props => [phase, session, error, bridges];
}
