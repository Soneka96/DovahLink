import 'package:dovahlink_client_sdk/src/protocol/protocol_format_exception.dart';
import 'package:dovahlink_client_sdk/src/shared/enums.dart';

/// Validates cross-field invariants of a decoded protocol envelope.
class EnvelopeValidator {
  /// Validates typed envelope fields after generated JSON decoding.
  static void validate({
    required ProtocolMessageType messageType,
    required String messageId,
    required String? sessionId,
    required String? correlationId,
    required String? bridgeInstanceId,
    required String? playContextId,
    required String? clientId,
  }) {
    if (messageId.isEmpty) {
      throw const ProtocolFormatException('messageId must not be empty.');
    }
    if (correlationId?.isEmpty ?? false) {
      throw const ProtocolFormatException(
        'correlationId must be null or non-empty.',
      );
    }
    final bool correlationIsRequired = switch (messageType) {
      ProtocolMessageType.helloAck ||
      ProtocolMessageType.pairingStatus ||
      ProtocolMessageType.pairingOutcome ||
      ProtocolMessageType.renameOutcome ||
      ProtocolMessageType.subscriptionAck ||
      ProtocolMessageType.stateSnapshot ||
      ProtocolMessageType.pong => true,
      _ => false,
    };
    final bool correlationMustBeNull = switch (messageType) {
      ProtocolMessageType.hello ||
      ProtocolMessageType.capabilities ||
      ProtocolMessageType.stateEvent ||
      ProtocolMessageType.sessionInvalidated ||
      ProtocolMessageType.ping ||
      ProtocolMessageType.pairingRequest ||
      ProtocolMessageType.pairingConfirm ||
      ProtocolMessageType.pairingAck ||
      ProtocolMessageType.pairingRenotify ||
      ProtocolMessageType.pairingCancel ||
      ProtocolMessageType.renameRequest ||
      ProtocolMessageType.subscribe ||
      ProtocolMessageType.snapshotRequest => true,
      _ => false,
    };
    if (correlationMustBeNull && correlationId != null) {
      throw ProtocolFormatException(
        'correlationId must be null for $messageType.',
      );
    }
    if (correlationIsRequired && correlationId == null) {
      throw ProtocolFormatException(
        'correlationId must be present for $messageType.',
      );
    }

    switch (messageType) {
      case ProtocolMessageType.hello:
        if (sessionId != null) {
          throw const ProtocolFormatException(
            'sessionId must be null for hello.',
          );
        }
      case ProtocolMessageType.error:
        if (sessionId?.isEmpty ?? false) {
          throw const ProtocolFormatException(
            'sessionId must be null or non-empty for error.',
          );
        }
      default:
        if (sessionId == null || sessionId.isEmpty) {
          throw ProtocolFormatException(
            'sessionId must be non-empty for $messageType.',
          );
        }
    }
    if (bridgeInstanceId?.isEmpty ?? false) {
      throw const ProtocolFormatException(
        'bridgeInstanceId must be null or non-empty.',
      );
    }
    if (playContextId?.isEmpty ?? false) {
      throw const ProtocolFormatException(
        'playContextId must be null or non-empty.',
      );
    }
    if (clientId?.isEmpty ?? false) {
      throw const ProtocolFormatException(
        'clientId must be null or non-empty.',
      );
    }
    final bool clientIdIsRequired = isClientIdRequired(messageType);
    if (clientIdIsRequired && clientId == null) {
      throw ProtocolFormatException(
        'clientId must be non-empty for $messageType.',
      );
    }
    if (messageType != ProtocolMessageType.capabilities &&
        !clientIdIsRequired &&
        clientId != null) {
      throw ProtocolFormatException('clientId must be null for $messageType.');
    }
    if (messageType == ProtocolMessageType.hello &&
        (bridgeInstanceId != null ||
            playContextId != null ||
            clientId != null)) {
      throw const ProtocolFormatException(
        'hello identity fields must be null.',
      );
    }
  }

  /// Returns whether [messageType] carries a non-`null` envelope-level `clientId`, per
  /// `protocol/schema/README.md`. True for the bridge's own `hello_ack`, and for every currently
  /// implemented client-originated request past `hello` (the pairing messages, `rename_request`,
  /// `subscribe`, `snapshot_request`, `ping`). False both for `hello` itself -- whose `clientId`
  /// travels in its payload instead, because envelope-level identity is not yet established -- and
  /// for every session-scoped reply/event type that carries no per-client identity at all
  /// (`pairing_status`, `pairing_outcome`, `rename_outcome`, `subscription_ack`, `state_snapshot`,
  /// `state_event`, `error`, `session_invalidated`, `pong`). `capabilities` is deliberately in
  /// neither branch: it is bidirectional per the schema, so [validate] exempts it from this
  /// method's null/non-null rule above rather than this method encoding a direction-dependent
  /// answer for it.
  static bool isClientIdRequired(ProtocolMessageType messageType) =>
      switch (messageType) {
        ProtocolMessageType.helloAck ||
        ProtocolMessageType.pairingRequest ||
        ProtocolMessageType.pairingConfirm ||
        ProtocolMessageType.pairingAck ||
        ProtocolMessageType.pairingRenotify ||
        ProtocolMessageType.pairingCancel ||
        ProtocolMessageType.renameRequest ||
        ProtocolMessageType.subscribe ||
        ProtocolMessageType.snapshotRequest ||
        ProtocolMessageType.ping => true,
        _ => false,
      };
}
