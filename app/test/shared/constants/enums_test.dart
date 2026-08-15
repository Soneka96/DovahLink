import 'package:flutter_test/flutter_test.dart';

import 'package:dovahlink_client/shared/constants/enums.dart';

/// Exercises stable labels for every connection phase.
void main() {
  group('ConnectionPhase label', () {
    test('returns the concise label for every phase', () {
      expect(ConnectionPhase.none.label, 'Unknown');
      expect(ConnectionPhase.disconnected.label, 'Disconnected');
      expect(ConnectionPhase.connecting.label, 'Connecting');
      expect(ConnectionPhase.negotiating.label, 'Negotiating');
      expect(ConnectionPhase.connected.label, 'Connected');
      expect(ConnectionPhase.stale.label, 'Stale');
      expect(ConnectionPhase.recovering.label, 'Recovering');
      expect(ConnectionPhase.unavailable.label, 'Unavailable');
      expect(ConnectionPhase.incompatible.label, 'Incompatible');
    });
  });
}
