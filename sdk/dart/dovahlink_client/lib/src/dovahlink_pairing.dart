import 'package:dovahlink_client_sdk/src/dovahlink_compatibility_exception.dart';
import 'package:dovahlink_client_sdk/src/dovahlink_connection_exception.dart';
import 'package:dovahlink_client_sdk/src/dovahlink_host.dart';
import 'package:dovahlink_client_sdk/src/dovahlink_host_id.dart';
import 'package:dovahlink_client_sdk/src/dovahlink_host_identity_mismatch_exception.dart';
import 'package:dovahlink_client_sdk/src/dovahlink_pairing_exception.dart';
import 'package:dovahlink_client_sdk/src/dovahlink_pairing_handshake.dart';
import 'package:dovahlink_client_sdk/src/dovahlink_protocol_exception.dart';
import 'package:dovahlink_client_sdk/src/dovahlink_storage_exception.dart';
import 'package:dovahlink_client_sdk/src/internal/pairing/pairing_service.dart';
import 'package:dovahlink_client_sdk/src/internal/state/subscription_service.dart';
import 'package:dovahlink_client_sdk/src/pairing_cancel_outcome.dart';
import 'package:dovahlink_client_sdk/src/pairing_challenge_status.dart';
import 'package:dovahlink_client_sdk/src/pairing_renotify_result.dart';
import 'package:dovahlink_client_sdk/src/shared/enums.dart';

/// Exposes candidate discovery and pairing operations owned by the SDK.
abstract interface class IDovahLinkPairing {
  /// The current runtime-only, Known Host-reconciled candidate collection.
  /// The stream immediately replays its latest complete value to each subscriber.
  Stream<List<DovahLinkHost>> get candidates;

  /// Discovers local Hosts and publishes the complete reconciled candidate collection.
  /// @return Candidates sorted by normalized Host ID; Known Host claims are excluded.
  /// @throws [DovahLinkConnectionException] if the endpoint rejects discovery.
  /// @throws [DovahLinkProtocolException] if discovery metadata is malformed.
  /// @throws [DovahLinkCompatibilityException] if the Host version is unsupported.
  /// @throws [DovahLinkStorageException] if authoritative Known Host state cannot be read.
  Future<List<DovahLinkHost>> discoverHosts();

  /// Authenticates a discovered candidate and recovers interrupted pairing confirmation.
  /// Candidate discovery claims never authorize Known Host credential use.
  Future<DovahLinkPairingHandshake> authenticateCandidate(Uri uri);

  /// Authenticates the specified Known Host and recovers interrupted pairing confirmation.
  Future<DovahLinkPairingHandshake> authenticateKnownHost(
    DovahLinkHostId hostId,
  );

  /// Starts or queries the current pairing challenge.
  /// @return Host-reported pairing availability and challenge expiry.
  /// @throws [DovahLinkConnectionException] if there is no active session.
  /// @throws [DovahLinkProtocolException] if the Host reply is malformed.
  Future<PairingChallengeStatus> requestCode();

  /// Requests the Host to redisplay the current pairing code.
  /// @return The Host's typed result and retry cooldown.
  /// @throws [DovahLinkConnectionException] if there is no active session.
  /// @throws [DovahLinkPairingException] if the request is no longer valid.
  /// @throws [DovahLinkProtocolException] if the Host reply is malformed.
  Future<PairingRenotifyResult> renotify();

  /// Cancels the active pairing challenge or pending credential.
  /// @return The Host's typed cancellation outcome.
  /// @throws [DovahLinkConnectionException] if there is no active session.
  /// @throws [DovahLinkPairingException] if the operation is rejected.
  /// @throws [DovahLinkProtocolException] if the Host reply is malformed.
  Future<PairingCancelOutcome> cancel();

  /// Confirms the six-digit code, persists the issued credential, and acknowledges it with the Host.
  /// @param code The six-digit code shown by Skyrim.
  /// @param displayName The optional Client display name.
  /// @throws [DovahLinkConnectionException] if there is no admitted Host session.
  /// @throws [DovahLinkPairingException] if the code or challenge is rejected.
  /// @throws [DovahLinkProtocolException] if the Host reply is malformed.
  /// @throws [DovahLinkStorageException] if the credential cannot be persisted.
  Future<void> confirmCode({required String code, String? displayName});

  /// Recovers an interrupted confirmation and reports the resulting session trust state.
  /// A no-op returns `unpaired` when there is no pending confirmation.
  /// @throws [DovahLinkConnectionException] if there is no admitted Host session.
  /// @throws [DovahLinkHostIdentityMismatchException] if pending recovery belongs to another Host.
  /// @throws [DovahLinkPairingException] if pending confirmation cannot be recovered.
  /// @throws [DovahLinkProtocolException] if the Host reply is malformed.
  /// @throws [DovahLinkStorageException] if recovery state cannot be read or written.
  Future<DovahLinkTrustState> recoverPendingPairing();
}

/// Implements [IDovahLinkPairing] over the client's existing discovery and pairing owners.
class DovahLinkPairing implements IDovahLinkPairing {
  /// Runs discovery through the client-owned Known Host reconciliation flow.
  final Future<List<DovahLinkHost>> Function() _discoverHosts;

  /// The client-owned candidate projection stream.
  final Stream<List<DovahLinkHost>> _candidates;

  /// Owns pairing protocol operations and credential recovery.
  final IPairingService _pairingService;

  /// Restores desired game-state subscriptions after trusted pairing recovery.
  final ISubscriptionService _subscriptionService;

  /// Creates a pairing view over the existing client discovery and state owners.
  /// @param discoverHosts Executes the client's authoritative discovery/reconciliation operation.
  /// @param candidates The client's replaying runtime candidate projection.
  /// @param pairingService Owns pairing protocol operations.
  /// @param subscriptionService Owns desired state subscription intent.
  DovahLinkPairing({
    required Future<List<DovahLinkHost>> Function() discoverHosts,
    required Stream<List<DovahLinkHost>> candidates,
    required IPairingService pairingService,
    required ISubscriptionService subscriptionService,
  }) : _discoverHosts = discoverHosts,
       _candidates = candidates,
       _pairingService = pairingService,
       _subscriptionService = subscriptionService;

  /// Implements [IDovahLinkPairing.candidates].
  @override
  Stream<List<DovahLinkHost>> get candidates => _candidates;

  /// Implements [IDovahLinkPairing.discoverHosts].
  @override
  Future<List<DovahLinkHost>> discoverHosts() => _discoverHosts();

  /// Implements [IDovahLinkPairing.authenticateCandidate].
  @override
  Future<DovahLinkPairingHandshake> authenticateCandidate(Uri uri) async {
    final DovahLinkPairingHandshake result = await _pairingService
        .authenticateCandidate(uri);
    if (result.hello.trustState == DovahLinkTrustState.unpaired &&
        result.trustState == DovahLinkTrustState.trusted) {
      _subscriptionService.restoreDesiredStateAreas();
    }
    return result;
  }

  /// Implements [IDovahLinkPairing.authenticateKnownHost].
  @override
  Future<DovahLinkPairingHandshake> authenticateKnownHost(
    DovahLinkHostId hostId,
  ) async {
    final DovahLinkPairingHandshake result = await _pairingService
        .authenticateKnownHost(hostId);
    if (result.hello.trustState == DovahLinkTrustState.unpaired &&
        result.trustState == DovahLinkTrustState.trusted) {
      _subscriptionService.restoreDesiredStateAreas();
    }
    return result;
  }

  /// Implements [IDovahLinkPairing.requestCode].
  @override
  Future<PairingChallengeStatus> requestCode() =>
      _pairingService.requestPairing();

  /// Implements [IDovahLinkPairing.renotify].
  @override
  Future<PairingRenotifyResult> renotify() =>
      _pairingService.requestPairingRenotify();

  /// Implements [IDovahLinkPairing.cancel].
  @override
  Future<PairingCancelOutcome> cancel() => _pairingService.cancelPairing();

  /// Implements [IDovahLinkPairing.confirmCode].
  @override
  Future<void> confirmCode({required String code, String? displayName}) async {
    await _pairingService.confirmPairingCodeAndAcknowledge(
      code: code,
      displayName: displayName,
    );
    _subscriptionService.restoreDesiredStateAreas();
  }

  /// Implements [IDovahLinkPairing.recoverPendingPairing].
  @override
  Future<DovahLinkTrustState> recoverPendingPairing() async {
    final DovahLinkTrustState trustState = await _pairingService
        .recoverPendingPairing();
    if (trustState == DovahLinkTrustState.trusted) {
      _subscriptionService.restoreDesiredStateAreas();
    }
    return trustState;
  }
}
