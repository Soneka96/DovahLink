import 'package:dovahlink_client_sdk/src/dovahlink_connection_exception.dart';
import 'package:dovahlink_client_sdk/src/dovahlink_protocol_exception.dart';
import 'package:dovahlink_client_sdk/src/internal/authentication/authentication_service.dart';
import 'package:dovahlink_client_sdk/src/internal/protocol_payload_decoder.dart';
import 'package:dovahlink_client_sdk/src/internal/reconnect/reconnect_service.dart';
import 'package:dovahlink_client_sdk/src/internal/requests/request_service.dart';
import 'package:dovahlink_client_sdk/src/internal/session/session_service.dart';
import 'package:dovahlink_client_sdk/src/internal/state/subscription_service.dart';
import 'package:dovahlink_client_sdk/src/protocol/envelope.dart';
import 'package:dovahlink_client_sdk/src/protocol/rename_outcome_payload.dart';
import 'package:dovahlink_client_sdk/src/protocol/rename_request_payload.dart';
import 'package:dovahlink_client_sdk/src/request_policy.dart';
import 'package:dovahlink_client_sdk/src/shared/enums.dart';

/// Owns the operations performed on the client's single active session: deliberate disconnect,
/// terminal shutdown, and renaming this device in the Host's trust record, per
/// `ai/context/sdk/architecture.md`'s "Internal composition".
abstract interface class IConnectionService {
  /// Deliberately ends the active session. Cancels pending authentication, initial-connection
  /// retry, and established-session recovery, then clears desired subscription intent, and only
  /// then disconnects the session, so no cancelled continuation can reconnect or restore.
  Future<void> disconnect();

  /// Ends the client's lifetime: the same cancellation sequence as [disconnect], followed by the
  /// session service's terminal close, so the session can never be reused.
  Future<void> shutdown();

  /// Renames this client in the active Host's trust record.
  /// @param displayName The new display name, or an empty string to clear it.
  /// @return The Host's typed rename outcome.
  /// @throws [DovahLinkConnectionException] if no trusted session is active or transport fails.
  /// @throws [DovahLinkProtocolException] if the Host reports a protocol failure or malformed
  ///     reply.
  Future<RenameOutcome> renameDevice(String displayName);
}

/// Implements [IConnectionService] over the shared session, authentication, reconnect,
/// subscription, and request owners. Every collaborator is supplied by the caller per
/// `ai/context/sdk/architecture.md`'s "Dependency injection".
class ConnectionService implements IConnectionService {
  /// Owns transport lifecycle and reports protocol violations.
  final ISessionService _sessionService;

  /// Owns pending authentication.
  final IAuthenticationService _authenticationService;

  /// Owns initial-connection retry and established-session recovery.
  final IReconnectService _reconnectService;

  /// Owns desired state-area subscription intent.
  final ISubscriptionService _subscriptionService;

  /// Sends the correlated rename request.
  final IRequestService _requestService;

  /// Creates a connection service over the existing lifecycle owners.
  /// @param sessionService Owns transport and admitted session state.
  /// @param authenticationService Cancels pending authentication.
  /// @param reconnectService Cancels initial retry and recovery.
  /// @param subscriptionService Clears desired subscription intent.
  /// @param requestService Sends the rename request.
  ConnectionService({
    required ISessionService sessionService,
    required IAuthenticationService authenticationService,
    required IReconnectService reconnectService,
    required ISubscriptionService subscriptionService,
    required IRequestService requestService,
  }) : _sessionService = sessionService,
       _authenticationService = authenticationService,
       _reconnectService = reconnectService,
       _subscriptionService = subscriptionService,
       _requestService = requestService;

  /// Implements [IConnectionService.disconnect].
  @override
  Future<void> disconnect() async {
    _cancelPendingWork();
    await _sessionService.disconnect();
  }

  /// Implements [IConnectionService.shutdown].
  @override
  Future<void> shutdown() async {
    _cancelPendingWork();
    await _sessionService.close();
  }

  /// Implements [IConnectionService.renameDevice].
  @override
  Future<RenameOutcome> renameDevice(String displayName) async {
    final Envelope response = await _requestService.sendAndAwait(
      messageType: ProtocolMessageType.renameRequest,
      payload: RenameRequestPayload(displayName: displayName).toJson(),
      expectedType: ProtocolMessageType.renameOutcome,
      policy: const RequestPolicy(
        retrySafe: false,
        requiredTrustState: DovahLinkTrustState.trusted,
        timeoutClass: TimeoutClass.normal,
      ),
    );
    final RenameOutcomePayload outcome;
    try {
      outcome = ProtocolPayloadDecoder.decode(
        RenameOutcomePayload.fromJson,
        response.payload,
      );
    } on DovahLinkProtocolException catch (error) {
      _sessionService.onProtocolViolation(
        error,
        orphanRetrySafeOperations: false,
      );
      rethrow;
    }
    return outcome.outcome;
  }

  /// Cancels every continuation that could otherwise reconnect or restore after the session ends.
  /// The order is part of the contract: authentication, initial retry, recovery, then intent.
  void _cancelPendingWork() {
    _authenticationService.cancelPendingAuthentication();
    _reconnectService.stopInitialConnectionRetry();
    _reconnectService.stopRecovery();
    _subscriptionService.clearDesiredStateAreas();
  }
}
