import 'package:flutter_test/flutter_test.dart';

import 'package:dovahlink_client/shared/constants/enums.dart';

/// Exercises stable labels for every pairing phase.
void main() {
  group('PairingPhase label', () {
    test('returns the concise label for every phase', () {
      expect(PairingPhase.none.label, 'Unknown');
      expect(PairingPhase.connecting.label, 'Connecting');
      expect(PairingPhase.disconnected.label, 'Waiting for bridge');
      expect(PairingPhase.unpaired.label, 'Not paired');
      expect(PairingPhase.requestingCode.label, 'Requesting code');
      expect(PairingPhase.awaitingCode.label, 'Awaiting code');
      expect(PairingPhase.confirming.label, 'Confirming');
      expect(PairingPhase.trusted.label, 'Paired');
      expect(PairingPhase.failed.label, 'Failed');
    });
  });
}
