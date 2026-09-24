import 'dart:async';

import 'package:dovahlink_client_sdk/src/internal/requests/request_service.dart';
import 'package:dovahlink_client_sdk/src/protocol/envelope.dart';
import 'package:dovahlink_client_sdk/src/protocol/json_map.dart';
import 'package:dovahlink_client_sdk/src/request_policy.dart';
import 'package:dovahlink_client_sdk/src/shared/enums.dart';

/// A controllable request boundary for snapshot-recovery tests.
class ControlledRequestService implements IRequestService {
  /// Every request made while the fake is active, in send order.
  final List<
    ({
      ProtocolMessageType messageType,
      JsonMap payload,
      ProtocolMessageType expectedType,
      RequestPolicy policy,
      Completer<Envelope> reply,
    })
  >
  requests = [];

  /// Creates a request fake with no pending replies.
  ControlledRequestService();

  /// Records a request and returns its manually completed reply.
  /// @param messageType The protocol message being sent.
  /// @param payload The request payload.
  /// @param expectedType The protocol reply type expected by the caller.
  /// @param policy The request retry, session, and timeout policy.
  /// @return A Future controlled by the recorded request's completer.
  @override
  Future<Envelope> sendAndAwait({
    required ProtocolMessageType messageType,
    required JsonMap payload,
    required ProtocolMessageType expectedType,
    required RequestPolicy policy,
  }) {
    final Completer<Envelope> reply = Completer<Envelope>();
    requests.add((
      messageType: messageType,
      payload: payload,
      expectedType: expectedType,
      policy: policy,
      reply: reply,
    ));
    return reply.future;
  }

  /// Does not route messages in recovery-service tests.
  /// @param raw The serialized message passed to the unused inbound route.
  @override
  void handleIncoming(String raw) {}

  /// Does not fail pending requests in recovery-service tests.
  /// @param reason The failure passed to the unused pending-request cleanup.
  /// @param orphanRetrySafeOperations Whether retry-safe requests would be parked.
  @override
  void failAll(Exception reason, {required bool orphanRetrySafeOperations}) {}

  /// Does not retry operations in recovery-service tests.
  @override
  void retryOrphanedOperations() {}
}
