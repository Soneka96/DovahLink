import 'package:flutter_test/flutter_test.dart';

import 'package:dovahlink_client/shared/constants/enums.dart';

/// Exercises stable labels for every enum declared in `shared/constants/enums.dart`.
void main() {
  group(
    'Behavior values in PairingCredentialRejectionReason behave correctly',
    () {
      test(
        'Behavior values in PairingCredentialRejectionReason include every Host rejection reason',
        () {
          expect(PairingCredentialRejectionReason.values, [
            PairingCredentialRejectionReason.revoked,
            PairingCredentialRejectionReason.unrecognized,
            PairingCredentialRejectionReason.blocked,
          ]);
        },
      );
    },
  );

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
        expect(DovahConnectionCardState.unknown.label, isA<String>());
        expect(DovahConnectionCardState.unknown.label, 'Not connected');
        expect(DovahConnectionCardState.offline.label, isA<String>());
        expect(DovahConnectionCardState.offline.label, 'Offline');
        expect(DovahConnectionCardState.repair.label, isA<String>());
        expect(DovahConnectionCardState.repair.label, 'Pair again');
      },
    );
  });

  group('Property followsThemeOutline in DovahMaterialRole behaves correctly', () {
    test(
      'Property followsThemeOutline in DovahMaterialRole is true for clipped roles',
      () {
        expect(DovahMaterialRole.surface.followsThemeOutline, isA<bool>());
        expect(DovahMaterialRole.surface.followsThemeOutline, true);
        expect(DovahMaterialRole.raised.followsThemeOutline, true);
        expect(DovahMaterialRole.primaryAction.followsThemeOutline, true);
      },
    );

    test(
      'Property followsThemeOutline in DovahMaterialRole is false for plain boxes',
      () {
        expect(DovahMaterialRole.control.followsThemeOutline, isA<bool>());
        expect(DovahMaterialRole.control.followsThemeOutline, false);
        expect(DovahMaterialRole.icon.followsThemeOutline, false);
      },
    );
  });

  group('Property label in DovahPanelCornerStyle behaves correctly', () {
    test(
      'Property label in DovahPanelCornerStyle returns the concise label for every style',
      () {
        expect(DovahPanelCornerStyle.singleBevel.label, isA<String>());
        expect(DovahPanelCornerStyle.singleBevel.label, 'Single bevel');
        expect(DovahPanelCornerStyle.doubleBevel.label, isA<String>());
        expect(DovahPanelCornerStyle.doubleBevel.label, 'Double bevel');
        expect(DovahPanelCornerStyle.rounded.label, isA<String>());
        expect(DovahPanelCornerStyle.rounded.label, 'Rounded');
      },
    );
  });

  group('Property label in DovahButtonVariant behaves correctly', () {
    test(
      'Property label in DovahButtonVariant returns the concise label for every variant',
      () {
        expect(DovahButtonVariant.primary.label, isA<String>());
        expect(DovahButtonVariant.primary.label, 'Primary');
        expect(DovahButtonVariant.secondary.label, isA<String>());
        expect(DovahButtonVariant.secondary.label, 'Secondary');
      },
    );
  });
}
