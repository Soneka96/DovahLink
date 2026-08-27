import 'package:dovahlink_client_sdk/src/dovahlink_connection_exception.dart';
import 'package:dovahlink_client_sdk/src/protocol/envelope.dart';
import 'package:dovahlink_client_sdk/src/protocol/json_map.dart';
import 'package:dovahlink_client_sdk/src/request_policy.dart';
import 'package:dovahlink_client_sdk/src/shared/enums.dart';

/// Owns pending requests, timeouts, retry behavior, and inbound message routing, per
/// `ai/context/sdk/architecture.md`'s "Internal composition". The SDK owns exactly one of these
/// per `IDovahLinkTransport` connection; see `ai/context/sdk/architecture.md`'s "Inbound message
/// handling" for the correlation model this implements, and "Request/session boundary" for why
/// [sendAndAwait] checks `ISessionService.connectionState` instead of the eliminated
/// `ensureReceiving` mechanism.
abstract interface class RequestService {
  /// Sends [messageType] with [payload] under [policy] and awaits [expectedType]. Fails
  /// immediately with a [DovahLinkConnectionException] -- without registering or transmitting
  /// anything -- unless the connection is currently `connected`.
  Future<Envelope> sendAndAwait({
    required ProtocolMessageType messageType,
    required JsonMap payload,
    required ProtocolMessageType expectedType,
    required RequestPolicy policy,
  });

  /// Decodes and routes one inbound message: correlated replies, unsolicited pushes, and protocol
  /// violations.
  void handleIncoming(String raw);

  /// Resolves every pending operation. A `retrySafe` operation that has not already been retried
  /// once is parked for [retryOrphanedOperations] instead of being failed immediately when
  /// [orphanRetrySafeOperations] is `true`.
  void failAll(Exception reason, {required bool orphanRetrySafeOperations});

  /// Retransmits, at most once each, every operation an earlier ordinary transport loss orphaned.
  void retryOrphanedOperations();
}
