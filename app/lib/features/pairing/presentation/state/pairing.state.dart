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
    required this.hostVersion,
    required this.error,

    /// Typed Host reason for rejecting a previously stored credential, or `null` when absent.
    this.credentialRejectionReason,
    required this.codeExpiresAt,
    required this.renotifyAvailableAt,
    this.isRenotifyPending = false,
    this.support = PairingSupport.available,
  });

  /// Returns the state before any pairing attempt starts.
  factory PairingState.initial({
    PairingSupport support = PairingSupport.available,
  }) => PairingState(
    phase: PairingPhase.none,
    hostVersion: null,
    error: null,
    credentialRejectionReason: null,
    codeExpiresAt: null,
    renotifyAvailableAt: null,
    isRenotifyPending: false,
    support: support,
  );

  /// The current user-visible pairing phase.
  final PairingPhase phase;

  /// Whether the platform can safely persist the identity needed for pairing.
  final PairingSupport support;

  /// The Host's own release version reported at authentication, or
  /// `null` before it is known.
  final String? hostVersion;

  /// The most recent user-safe pairing error, or `null`.
  final String? error;

  /// The typed Host reason for rejecting a stored credential, or `null` when none was rejected.
  final PairingCredentialRejectionReason? credentialRejectionReason;

  /// The absolute time when the active pairing code expires, or `null` when
  /// no challenge is active.
  final DateTime? codeExpiresAt;

  /// The absolute time when the next manual renotify becomes available, or
  /// `null` when renotify is available immediately or no challenge is active.
  final DateTime? renotifyAvailableAt;

  /// Whether the Host is waiting for Skyrim to acknowledge a code redisplay.
  final bool isRenotifyPending;

  /// Returns a copy with selected values replaced.
  PairingState copyWith({
    PairingPhase? phase,
    Option<String>? hostVersion,
    Option<String>? error,

    /// Replaces the rejection reason; pass `None()` to clear it.
    Option<PairingCredentialRejectionReason>? credentialRejectionReason,
    Option<DateTime>? codeExpiresAt,
    Option<DateTime>? renotifyAvailableAt,
    bool? isRenotifyPending,
    PairingSupport? support,
  }) => PairingState(
    phase: phase ?? this.phase,
    support: support ?? this.support,
    hostVersion: hostVersion == null
        ? this.hostVersion
        : hostVersion.toNullable(),
    error: error == null ? this.error : error.toNullable(),
    credentialRejectionReason: credentialRejectionReason == null
        ? this.credentialRejectionReason
        : credentialRejectionReason.toNullable(),
    codeExpiresAt: codeExpiresAt == null
        ? this.codeExpiresAt
        : codeExpiresAt.toNullable(),
    renotifyAvailableAt: renotifyAvailableAt == null
        ? this.renotifyAvailableAt
        : renotifyAvailableAt.toNullable(),
    isRenotifyPending: isRenotifyPending ?? this.isRenotifyPending,
  );

  /// See [Equatable.props].
  @override
  List<Object?> get props => [
    phase,
    support,
    hostVersion,
    error,
    credentialRejectionReason,
    codeExpiresAt,
    renotifyAvailableAt,
    isRenotifyPending,
  ];
}
