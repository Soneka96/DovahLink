import 'package:equatable/equatable.dart';
import 'package:fpdart/fpdart.dart';
import 'package:meta/meta.dart';

import 'package:dovahlink_client/shared/constants/enums.dart';

/// Immutable Redux state for the local device pairing flow.
@immutable
class PairingState extends Equatable {
  /// Creates pairing state with explicit lifecycle values.
  const PairingState({
    required this.phase,
    required this.bridgeVersion,
    required this.error,
    required this.codeExpiresAt,
    required this.renotifyAvailableAt,
  });

  /// Returns the state before any pairing attempt starts.
  factory PairingState.initial() => const PairingState(
    phase: PairingPhase.none,
    bridgeVersion: null,
    error: null,
    codeExpiresAt: null,
    renotifyAvailableAt: null,
  );

  /// The current user-visible pairing phase.
  final PairingPhase phase;

  /// The DovahLink Bridge/mod release version reported at authentication, or
  /// `null` before it is known.
  final String? bridgeVersion;

  /// The most recent user-safe pairing error, or `null`.
  final String? error;

  /// The absolute time when the active pairing code expires, or `null` when
  /// no challenge is active.
  final DateTime? codeExpiresAt;

  /// The absolute time when the next manual renotify becomes available, or
  /// `null` when renotify is available immediately or no challenge is active.
  final DateTime? renotifyAvailableAt;

  /// Returns a copy with selected values replaced.
  PairingState copyWith({
    PairingPhase? phase,
    Option<String>? bridgeVersion,
    Option<String>? error,
    Option<DateTime>? codeExpiresAt,
    Option<DateTime>? renotifyAvailableAt,
  }) => PairingState(
    phase: phase ?? this.phase,
    bridgeVersion: bridgeVersion == null
        ? this.bridgeVersion
        : bridgeVersion.toNullable(),
    error: error == null ? this.error : error.toNullable(),
    codeExpiresAt: codeExpiresAt == null
        ? this.codeExpiresAt
        : codeExpiresAt.toNullable(),
    renotifyAvailableAt: renotifyAvailableAt == null
        ? this.renotifyAvailableAt
        : renotifyAvailableAt.toNullable(),
  );

  /// See [Equatable.props].
  @override
  List<Object?> get props => [phase, bridgeVersion, error, codeExpiresAt, renotifyAvailableAt];
}
