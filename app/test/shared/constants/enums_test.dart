import 'package:flutter_test/flutter_test.dart';

import 'package:dovahlink_client/shared/constants/enums.dart';

/// Exercises stable labels for every enum declared in `shared/constants/enums.dart`.
void main() {
  group('Property label in PairingPhase behaves correctly', () {
    test(
      'Property label in PairingPhase returns the concise label for every phase',
      () {
        expect(PairingPhase.none.label, 'Unknown');
        expect(PairingPhase.connecting.label, 'Connecting');
        expect(PairingPhase.disconnected.label, 'Waiting for host');
        expect(PairingPhase.unpaired.label, 'Not paired');
        expect(PairingPhase.requestingCode.label, 'Requesting code');
        expect(PairingPhase.awaitingCode.label, 'Awaiting code');
        expect(PairingPhase.confirming.label, 'Confirming');
        expect(PairingPhase.trusted.label, 'Paired');
        expect(PairingPhase.failed.label, 'Failed');
      },
    );
  });

  group('Property label in DovahThemePreset behaves correctly', () {
    test(
      'Property label in DovahThemePreset returns the concise label for every preset',
      () {
        expect(DovahThemePreset.frostbound.label, isA<String>());
        expect(DovahThemePreset.frostbound.label, 'Frostbound');
        expect(DovahThemePreset.dovah.label, isA<String>());
        expect(DovahThemePreset.dovah.label, 'Dovah');
        expect(DovahThemePreset.hearth.label, isA<String>());
        expect(DovahThemePreset.hearth.label, 'Hearth');
      },
    );
  });

  group('Property label in DovahConnectionCardState behaves correctly', () {
    test(
      'Property label in DovahConnectionCardState returns the concise label for every state',
      () {
        expect(DovahConnectionCardState.available.label, isA<String>());
        expect(DovahConnectionCardState.available.label, 'Connected');
        expect(DovahConnectionCardState.offline.label, isA<String>());
        expect(DovahConnectionCardState.offline.label, 'Offline');
        expect(DovahConnectionCardState.repair.label, isA<String>());
        expect(DovahConnectionCardState.repair.label, 'Pair again');
      },
    );
  });
}
