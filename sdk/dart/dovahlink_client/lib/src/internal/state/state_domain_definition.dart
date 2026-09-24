import 'package:dovahlink_client_sdk/src/dovahlink_protocol_exception.dart';
import 'package:dovahlink_client_sdk/src/internal/protocol_payload_decoder.dart';
import 'package:dovahlink_client_sdk/src/internal/state/state_revision_tracker.dart';
import 'package:dovahlink_client_sdk/src/protocol/envelope.dart';
import 'package:dovahlink_client_sdk/src/protocol/json_map.dart';
import 'package:dovahlink_client_sdk/src/protocol/state_event_payload.dart';
import 'package:dovahlink_client_sdk/src/protocol/state_snapshot_payload.dart';
import 'package:dovahlink_client_sdk/src/shared/enums.dart';

/// Applies decoded messages to the typed tracker registered for one state area.
abstract interface class IStateDomainDefinition {
  /// The canonical state-area name this definition handles.
  String get stateArea;

  /// Decodes and applies a Snapshot for this state area.
  /// @param envelope The envelope carrying authority and play-context identity.
  /// @param payload The decoded Snapshot payload for [stateArea].
  void applySnapshot({
    required Envelope envelope,
    required StateSnapshotPayload payload,
  });

  /// Applies Events or rejects them when this area is Snapshot-only.
  /// @param envelope The envelope carrying authority and play-context identity.
  /// @param payload The decoded Event payload for [stateArea].
  /// @throws [DovahLinkProtocolException] when this area is Snapshot-only.
  void applyEvent({
    required Envelope envelope,
    required StateEventPayload payload,
  });
}

/// Binds one state's decoder and availability rule to its revision tracker.
class StateDomainDefinition<T> implements IStateDomainDefinition {
  /// The canonical state-area name handled by this definition.
  @override
  final String stateArea;

  /// Decodes the state-area-specific data into its typed model.
  final T Function(JsonMap data) _decode;

  /// Applies decoded state and revision changes for this domain.
  final IStateRevisionTracker<T> _tracker;

  /// Identifies typed values that represent legitimate unavailability.
  final bool Function(T value) _isUnavailable;

  /// Whether this state area accepts revisioned Events.
  final bool _supportsEvents;

  /// Creates one typed registration for a state area and tracker.
  /// @param stateArea The canonical area name carried by the protocol payload.
  /// @param decode Converts area data into its typed state model.
  /// @param tracker Applies authoritative revision updates for the area.
  /// @param isUnavailable Identifies an explicit unavailable state value.
  /// @param supportsEvents Whether this area accepts protocol Events.
  StateDomainDefinition({
    required this.stateArea,
    required T Function(JsonMap data) decode,
    required IStateRevisionTracker<T> tracker,
    required bool Function(T value) isUnavailable,
    bool supportsEvents = false,
  }) : _decode = decode,
       _tracker = tracker,
       _isUnavailable = isUnavailable,
       _supportsEvents = supportsEvents;

  /// See [IStateDomainDefinition.applySnapshot].
  @override
  void applySnapshot({
    required Envelope envelope,
    required StateSnapshotPayload payload,
  }) {
    final T value = ProtocolPayloadDecoder.decode(_decode, payload.data);
    _tracker.applySnapshot(
      stateAuthorityId: envelope.stateAuthorityId!,
      playContextId: envelope.playContextId,
      revision: payload.revision,
      value: value,
      isUnavailable: _isUnavailable(value),
    );
  }

  /// See [IStateDomainDefinition.applyEvent].
  @override
  void applyEvent({
    required Envelope envelope,
    required StateEventPayload payload,
  }) {
    if (!_supportsEvents) {
      throw const DovahLinkProtocolException(
        code: ProtocolErrorCode.malformedMessage,
        message: 'Received an Event for a non-Event state area.',
        retryable: false,
      );
    }

    final T value = ProtocolPayloadDecoder.decode(_decode, payload.data);
    _tracker.applyEvent(
      stateAuthorityId: envelope.stateAuthorityId!,
      playContextId: envelope.playContextId,
      baseRevision: payload.baseRevision,
      revision: payload.revision,
      value: value,
      isUnavailable: _isUnavailable(value),
    );
  }
}
