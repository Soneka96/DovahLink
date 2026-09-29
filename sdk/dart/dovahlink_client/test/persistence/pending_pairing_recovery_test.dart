import 'package:test/test.dart';

import 'package:dovahlink_client_sdk/src/persistence/pending_pairing_recovery.dart';
import 'package:dovahlink_client_sdk/src/shared/enums.dart';

/// Runs Host-owned pairing-recovery value behavior tests.
void main() {
  test('PendingPairingRecovery compares its owner and phase', () {
    const PendingPairingRecovery recovery = PendingPairingRecovery(
      hostId: 'host-a',
      state: PairingRecoveryState.confirming,
    );
    const PendingPairingRecovery equivalent = PendingPairingRecovery(
      hostId: 'host-a',
      state: PairingRecoveryState.confirming,
    );
    const PendingPairingRecovery otherHost = PendingPairingRecovery(
      hostId: 'host-b',
      state: PairingRecoveryState.confirming,
    );

    expect(recovery, equivalent);
    expect(recovery.hashCode, equivalent.hashCode);
    expect(recovery, isNot(otherHost));
  });
}
