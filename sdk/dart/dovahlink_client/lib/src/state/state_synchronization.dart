import 'package:dovahlink_client_sdk/src/shared/enums.dart';

/// The current typed value and synchronization metadata for one state domain [T].
class StateSynchronization<T> {
  /// The domain's current synchronization standing.
  final DovahLinkStateStatus status;

  /// The latest accepted typed state, or `null` before an authoritative baseline exists.
  final T? value;

  /// The Host continuity epoch that owns [revision], when a baseline exists.
  final String? stateAuthorityId;

  /// The loaded play context that owns [revision], or `null` outside a loaded game.
  final String? playContextId;

  /// The last accepted authoritative revision, or `null` before a baseline exists.
  final int? revision;

  /// Creates a state synchronization view.
  /// @param status The current domain synchronization standing.
  /// @param value The latest typed state, or `null` before a baseline exists.
  /// @param stateAuthorityId The authority identity associated with the baseline.
  /// @param playContextId The play-context identity associated with the baseline.
  /// @param revision The last accepted revision, or `null` before a baseline exists.
  const StateSynchronization({
    required this.status,
    required this.value,
    required this.stateAuthorityId,
    required this.playContextId,
    required this.revision,
  });

  /// Creates a view for a domain not accepted by the current Host session.
  const StateSynchronization.notSubscribed()
    : status = DovahLinkStateStatus.notSubscribed,
      value = null,
      stateAuthorityId = null,
      playContextId = null,
      revision = null;
}
