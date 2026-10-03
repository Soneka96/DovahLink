import 'package:flutter_test/flutter_test.dart';

import 'package:dovahlink_client/shared/constants/enums.dart';

import 'package:dovahlink_client_sdk/dovahlink_client.dart'
    show
        DovahLinkCompatibilityException,
        DovahLinkConnectionException,
        DovahLinkProtocolException,
        HostVersionCompatibilityFailure,
        ProtocolErrorCode;

class _UnknownErrorWithUnsafeToString implements Exception {
  @override
  String toString() => throw StateError('toString must not be called');
}

/// Exercises stable labels for every enum declared in `shared/constants/enums.dart`.
void main() {
  group('PairingFailureOutcome exposes prototype copy', () {
    test('invalid code includes the Host-reported attempts remaining', () {
      expect(
        PairingFailureOutcome.invalid.message(attemptsRemaining: 1),
        'That code isn’t correct. 1 attempt remaining.',
      );
      expect(
        PairingFailureOutcome.invalid.message(),
        'That code isn’t correct.',
      );
    });

    test('expired and attempt-limit outcomes use actionable copy', () {
      expect(
        PairingFailureOutcome.expired.message(),
        'The code is no longer valid. Ask Skyrim for a new one to continue.',
      );
      expect(
        PairingFailureOutcome.hardLimitReached.message(),
        'This pairing code can no longer be used. Request a new code from Skyrim to try again.',
      );
    });
  });

  group('PairingRenotifyOutcome exposes prototype copy', () {
    test('renotified outcome reports the Host-provided retry interval', () {
      expect(
        PairingRenotifyOutcome.renotified.message(retryAfterSeconds: 5),
        'Shown in Skyrim · 5s',
      );
      expect(PairingRenotifyOutcome.renotified.message(), 'Shown in Skyrim');
    });
  });

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

  group('Behavior values in ConnectionDiscoveryStatus behave correctly', () {
    test(
      'ConnectionDiscoveryStatus values include every discovery result state',
      () {
        expect(ConnectionDiscoveryStatus.values, [
          ConnectionDiscoveryStatus.idle,
          ConnectionDiscoveryStatus.discovering,
          ConnectionDiscoveryStatus.available,
          ConnectionDiscoveryStatus.empty,
          ConnectionDiscoveryStatus.failed,
        ]);
      },
    );
  });

  group('Behavior values in ConnectionHostSelectionSource behave correctly', () {
    test(
      'ConnectionHostSelectionSource values distinguish candidates from Known Hosts',
      () {
        expect(ConnectionHostSelectionSource.values, [
          ConnectionHostSelectionSource.candidate,
          ConnectionHostSelectionSource.knownHost,
        ]);
      },
    );
  });

  group('Behavior values in HostAvailability behave correctly', () {
    test('HostAvailability values contain every runtime state', () {
      expect(HostAvailability.values, [
        HostAvailability.unknown,
        HostAvailability.online,
        HostAvailability.offline,
        HostAvailability.checking,
      ]);
    });
  });

  group('Behavior values in KnownHostSessionState behave correctly', () {
    test('KnownHostSessionState lists each session lifecycle phase', () {
      expect(KnownHostSessionState.values, [
        KnownHostSessionState.disconnected,
        KnownHostSessionState.connecting,
        KnownHostSessionState.connected,
        KnownHostSessionState.reconnecting,
        KnownHostSessionState.reauthenticating,
      ]);
    });
  });

  group('Behavior values in KnownHostsObservationStatus behave correctly', () {
    test(
      'KnownHostsObservationStatus values include every observation state',
      () {
        expect(KnownHostsObservationStatus.values, [
          KnownHostsObservationStatus.loading,
          KnownHostsObservationStatus.ready,
          KnownHostsObservationStatus.failed,
        ]);
      },
    );
  });

  group('ConnectionFailureReason maps discovery errors', () {
    test(
      'ConnectionFailureReason maps SDK discovery exception types to app meanings',
      () {
        expect(
          ConnectionFailureReason.fromDiscoveryError(
            const DovahLinkConnectionException('unsupported version text'),
          ),
          ConnectionFailureReason.hostUnavailable,
        );
        expect(
          ConnectionFailureReason.fromDiscoveryError(
            const DovahLinkCompatibilityException(
              hostVersion: 'unsupported',
              supportedHostVersionRange: 'supported',
              failure: HostVersionCompatibilityFailure.hostTooNew,
            ),
          ),
          ConnectionFailureReason.incompatibleHost,
        );
        expect(
          ConnectionFailureReason.fromDiscoveryError(
            const DovahLinkProtocolException(
              code: ProtocolErrorCode.malformedMessage,
              message: 'connection refused text',
              retryable: false,
            ),
          ),
          ConnectionFailureReason.invalidResponse,
        );
      },
    );

    test(
      'ConnectionFailureReason maps unknown errors without calling toString',
      () {
        expect(
          ConnectionFailureReason.fromDiscoveryError(
            _UnknownErrorWithUnsafeToString(),
          ),
          ConnectionFailureReason.unknown,
        );
      },
    );

    test('ConnectionFailureReason exposes centralized user-facing copy', () {
      expect(
        ConnectionFailureReason.hostUnavailable.message,
        'Could not reach the local Host. Check that it is running and try again.',
      );
      expect(
        ConnectionFailureReason.incompatibleHost.message,
        'This local Host version is not compatible with the app.',
      );
      expect(
        ConnectionFailureReason.invalidResponse.message,
        'The local Host returned an invalid response. Try again.',
      );
      expect(
        ConnectionFailureReason.unknown.message,
        'Host discovery failed. Try again.',
      );
    });
  });

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
        expect(DovahConnectionCardState.checking.label, isA<String>());
        expect(DovahConnectionCardState.checking.label, 'Checking…');
        expect(DovahConnectionCardState.available.label, isA<String>());
        expect(DovahConnectionCardState.available.label, 'Online');
        expect(DovahConnectionCardState.unknown.label, isA<String>());
        expect(DovahConnectionCardState.unknown.label, 'Unknown');
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

  group(
    'Property summary and materials in DovahThemePreset behave correctly',
    () {
      test('Property summary in DovahThemePreset returns the card summary', () {
        expect(DovahThemePreset.frostbound.summary, isA<String>());
        expect(DovahThemePreset.frostbound.summary, 'Cold, severe and compact');
        expect(
          DovahThemePreset.dovah.summary,
          'The balanced DovahLink identity',
        );
        expect(
          DovahThemePreset.hearth.summary,
          'Warm, spacious and storybook-like',
        );
      });

      test(
        'Property materials in DovahThemePreset returns the card materials line',
        () {
          expect(DovahThemePreset.frostbound.materials, isA<String>());
          expect(
            DovahThemePreset.frostbound.materials,
            'Frozen stone · iron · warning red',
          );
          expect(
            DovahThemePreset.dovah.materials,
            'Midnight steel · ember · ice',
          );
          expect(
            DovahThemePreset.hearth.materials,
            'Parchment · walnut · bronze',
          );
        },
      );
    },
  );

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
        expect(DovahButtonVariant.quiet.label, isA<String>());
        expect(DovahButtonVariant.quiet.label, 'Quiet');
      },
    );
  });
}
