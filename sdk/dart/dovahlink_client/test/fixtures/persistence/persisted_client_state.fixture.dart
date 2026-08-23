import 'package:dovahlink_client_sdk/src/persistence/persisted_client_state.dart';
import 'package:dovahlink_client_sdk/src/shared/enums.dart';

/// Builds a persisted client state with a representative, already-resolved clientId.
PersistedClientState buildPersistedClientState({
  String? clientId = 'client-1',
  String? credential,
  PairingRecoveryState recoveryState = PairingRecoveryState.none,
}) => PersistedClientState(
  clientId: clientId,
  credential: credential,
  recoveryState: recoveryState,
);
