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
  });

  /// Returns the state before any pairing attempt starts.
  factory PairingState.initial() => const PairingState(
    phase: PairingPhase.none,
    bridgeVersion: null,
    error: null,
  );

  /// The current user-visible pairing phase.
  final PairingPhase phase;

  /// The DovahLink Bridge/mod release version reported at authentication, or
  /// `null` before it is known.
  final String? bridgeVersion;

  /// The most recent user-safe pairing error, or `null`.
  final String? error;

  /// Returns a copy with selected values replaced.
  PairingState copyWith({
    PairingPhase? phase,
    Option<String>? bridgeVersion,
    Option<String>? error,
  }) => PairingState(
    phase: phase ?? this.phase,
    bridgeVersion: bridgeVersion == null
        ? this.bridgeVersion
        : bridgeVersion.toNullable(),
    error: error == null ? this.error : error.toNullable(),
  );

  /// See [Equatable.props].
  @override
  List<Object?> get props => [phase, bridgeVersion, error];
}
