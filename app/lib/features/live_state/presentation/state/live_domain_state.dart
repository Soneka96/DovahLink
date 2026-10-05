import 'package:equatable/equatable.dart';

import 'package:dovahlink_client/features/live_state/presentation/state/live_state_enums.dart';

/// App-owned synchronization metadata and typed value for one live-state domain.
class LiveDomainState<T> extends Equatable {
  /// Creates a domain projection while preserving synchronization and authority metadata.
  /// @param status The latest SDK-reported synchronization status.
  /// @param value The latest typed value, which may be null when unavailable.
  /// @param stateAuthorityId The authority epoch associated with the value.
  /// @param playContextId The loaded play context associated with the value.
  /// @param revision The last accepted authoritative revision.
  const LiveDomainState({
    required this.status,
    required this.value,
    required this.stateAuthorityId,
    required this.playContextId,
    required this.revision,
  });

  /// Creates the initial projection before the SDK reports a domain state.
  const LiveDomainState.notSubscribed()
    : status = LiveStateStatus.notSubscribed,
      value = null,
      stateAuthorityId = null,
      playContextId = null,
      revision = null;

  /// The SDK-reported synchronization status.
  final LiveStateStatus status;

  /// The latest typed domain value, retained even when its status becomes stale.
  final T? value;

  /// The Host continuity epoch associated with the value, when known.
  final String? stateAuthorityId;

  /// The loaded Skyrim play context associated with the value, when known.
  final String? playContextId;

  /// The last accepted authoritative revision, when known.
  final int? revision;

  /// See [Equatable.props].
  @override
  List<Object?> get props => [
    status,
    value,
    stateAuthorityId,
    playContextId,
    revision,
  ];
}
