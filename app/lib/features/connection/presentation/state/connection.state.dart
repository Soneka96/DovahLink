import 'package:equatable/equatable.dart';
import 'package:fpdart/fpdart.dart';
import 'package:meta/meta.dart';

import 'package:dovahlink_client/features/connection/domain/entities/connection_session.entity.dart';
import 'package:dovahlink_client/shared/constants/enums.dart';

/// Immutable Redux state for the bridge connection.
@immutable
class ConnectionState extends Equatable {
  /// Creates connection state with explicit lifecycle values.
  const ConnectionState({
    required this.phase,
    required this.session,
    required this.error,
  });

  /// Returns the state before a connection attempt starts.
  factory ConnectionState.initial() => const ConnectionState(
    phase: ConnectionPhase.disconnected,
    session: null,
    error: null,
  );

  /// The current user-visible connection phase.
  final ConnectionPhase phase;

  /// The active negotiated session, or `null` when none exists.
  final ConnectionSessionEntity? session;

  /// The most recent user-safe connection error, or `null`.
  final String? error;

  /// Returns a copy with selected values replaced.
  ConnectionState copyWith({
    ConnectionPhase? phase,
    Option<ConnectionSessionEntity>? session,
    Option<String>? error,
  }) => ConnectionState(
    phase: phase ?? this.phase,
    session: session == null ? this.session : session.toNullable(),
    error: error == null ? this.error : error.toNullable(),
  );

  /// See [Equatable.props].
  @override
  List<Object?> get props => [phase, session, error];
}
