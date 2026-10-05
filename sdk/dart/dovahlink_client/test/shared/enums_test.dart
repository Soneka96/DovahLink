import 'package:test/test.dart';

import 'package:dovahlink_client_sdk/src/shared/enums.dart';

/// Runs [CredentialRejectionReason.fromProtocolErrorCode] behavior tests.
void main() {
  group(
    'Property values in DovahLinkInitialConnectionRetryStatus behaves correctly',
    () {
      test(
        'Property values in DovahLinkInitialConnectionRetryStatus distinguishes inactive and retrying',
        () {
          expect(
            DovahLinkInitialConnectionRetryStatus.values,
            <DovahLinkInitialConnectionRetryStatus>[
              DovahLinkInitialConnectionRetryStatus.inactive,
              DovahLinkInitialConnectionRetryStatus.retrying,
            ],
          );
        },
      );
    },
  );

  group('Property values in DovahLinkHostAvailability behaves correctly', () {
    test(
      'Property values in DovahLinkHostAvailability contains every reachability state',
      () {
        expect(DovahLinkHostAvailability.values, <DovahLinkHostAvailability>[
          DovahLinkHostAvailability.unknown,
          DovahLinkHostAvailability.online,
          DovahLinkHostAvailability.offline,
          DovahLinkHostAvailability.checking,
        ]);
      },
    );
  });

  group(
    'Property values in DovahLinkKnownHostSessionState behaves correctly',
    () {
      test(
        'Property values in DovahLinkKnownHostSessionState lists each session phase',
        () {
          expect(
            DovahLinkKnownHostSessionState.values,
            <DovahLinkKnownHostSessionState>[
              DovahLinkKnownHostSessionState.disconnected,
              DovahLinkKnownHostSessionState.connecting,
              DovahLinkKnownHostSessionState.connected,
              DovahLinkKnownHostSessionState.reconnecting,
              DovahLinkKnownHostSessionState.reauthenticating,
            ],
          );
        },
      );
    },
  );

  group('Property protocolValue behaves correctly', () {
    test(
      'Property protocolValue maps every typed state area to its canonical name',
      () {
        expect(
          DovahLinkStateArea.values.map(
            (DovahLinkStateArea area) => area.protocolValue,
          ),
          <String>[
            'character_xp',
            'character_vitals',
            'character_level',
            'character_identity',
            'character_supernatural_traits',
            'player_location',
            'game_time',
          ],
        );
      },
    );
  });

  group('Property values in PlayerLocationCellKind behaves correctly', () {
    test(
      'Property values in PlayerLocationCellKind preserves its wire vocabulary',
      () {
        expect(PlayerLocationCellKind.values, <PlayerLocationCellKind>[
          PlayerLocationCellKind.interior,
          PlayerLocationCellKind.exterior,
        ]);
      },
    );
  });

  group('Property values in TrackedQuestObjectiveState behaves correctly', () {
    test(
      'Property values in TrackedQuestObjectiveState preserves every engine state',
      () {
        expect(TrackedQuestObjectiveState.values, <TrackedQuestObjectiveState>[
          TrackedQuestObjectiveState.dormant,
          TrackedQuestObjectiveState.displayed,
          TrackedQuestObjectiveState.completed,
          TrackedQuestObjectiveState.completedAndDisplayed,
          TrackedQuestObjectiveState.failed,
          TrackedQuestObjectiveState.failedAndDisplayed,
        ]);
      },
    );
  });

  group('Method fromProtocolValue behaves correctly', () {
    test(
      'Method fromProtocolValue round-trips known areas and rejects unknown names',
      () {
        for (final DovahLinkStateArea area in DovahLinkStateArea.values) {
          expect(
            DovahLinkStateArea.fromProtocolValue(area.protocolValue),
            area,
          );
        }
        expect(DovahLinkStateArea.fromProtocolValue('unknown_area'), isNull);
      },
    );
  });

  group('Method fromProtocolErrorCode behaves correctly', () {
    test(
      'Method fromProtocolErrorCode maps recoverable typed protocol errors',
      () {
        expect(
          CredentialRejectionReason.fromProtocolErrorCode(
            ProtocolErrorCode.revoked,
          ),
          CredentialRejectionReason.revoked,
        );
        expect(
          CredentialRejectionReason.fromProtocolErrorCode(
            ProtocolErrorCode.unauthenticated,
          ),
          CredentialRejectionReason.unrecognized,
        );
        expect(
          CredentialRejectionReason.fromProtocolErrorCode(
            ProtocolErrorCode.blocked,
          ),
          CredentialRejectionReason.blocked,
        );
      },
    );

    test(
      'Method fromProtocolErrorCode returns null for every non-recoverable typed protocol error',
      () {
        const Set<ProtocolErrorCode> recoverable = <ProtocolErrorCode>{
          ProtocolErrorCode.revoked,
          ProtocolErrorCode.unauthenticated,
          ProtocolErrorCode.blocked,
        };
        for (final ProtocolErrorCode code in ProtocolErrorCode.values) {
          if (!recoverable.contains(code)) {
            expect(
              CredentialRejectionReason.fromProtocolErrorCode(code),
              isNull,
              reason: '$code is not a recoverable credential rejection',
            );
          }
        }
      },
    );
  });

  group('Method fromOutcome in PairingRenotifyStatus behaves correctly', () {
    test(
      'Method fromOutcome in PairingRenotifyStatus maps every valid renotify outcome',
      () {
        expect(
          PairingRenotifyStatus.fromOutcome(PairingOutcome.renotified),
          PairingRenotifyStatus.renotified,
        );
        expect(
          PairingRenotifyStatus.fromOutcome(PairingOutcome.renotifyCooldown),
          PairingRenotifyStatus.cooldown,
        );
        expect(
          PairingRenotifyStatus.fromOutcome(PairingOutcome.alreadyIdle),
          PairingRenotifyStatus.alreadyIdle,
        );
      },
    );

    test(
      'Method fromOutcome in PairingRenotifyStatus returns null for every other outcome',
      () {
        const Set<PairingOutcome> validOutcomes = <PairingOutcome>{
          PairingOutcome.renotified,
          PairingOutcome.renotifyCooldown,
          PairingOutcome.alreadyIdle,
        };
        for (final PairingOutcome outcome in PairingOutcome.values) {
          if (!validOutcomes.contains(outcome)) {
            expect(
              PairingRenotifyStatus.fromOutcome(outcome),
              isNull,
              reason: '$outcome is not a renotify outcome',
            );
          }
        }
      },
    );
  });

  group('Method fromOutcome in PairingCancelStatus behaves correctly', () {
    test(
      'Method fromOutcome in PairingCancelStatus maps every valid cancel outcome',
      () {
        expect(
          PairingCancelStatus.fromOutcome(PairingOutcome.cancelled),
          PairingCancelStatus.cancelled,
        );
        expect(
          PairingCancelStatus.fromOutcome(PairingOutcome.alreadyIdle),
          PairingCancelStatus.alreadyIdle,
        );
      },
    );

    test(
      'Method fromOutcome in PairingCancelStatus returns null for every other outcome',
      () {
        const Set<PairingOutcome> validOutcomes = <PairingOutcome>{
          PairingOutcome.cancelled,
          PairingOutcome.alreadyIdle,
        };
        for (final PairingOutcome outcome in PairingOutcome.values) {
          if (!validOutcomes.contains(outcome)) {
            expect(
              PairingCancelStatus.fromOutcome(outcome),
              isNull,
              reason: '$outcome is not a cancel outcome',
            );
          }
        }
      },
    );
  });

  test(
    'PairingOutcome includes pairing_invalidated as a registered outcome value',
    () {
      expect(
        PairingOutcome.values,
        contains(PairingOutcome.pairingInvalidated),
      );
    },
  );

  group('Property values in DovahLinkStateStatus behaves correctly', () {
    test('Property values in DovahLinkStateStatus includes every standing', () {
      expect(DovahLinkStateStatus.values, <DovahLinkStateStatus>[
        DovahLinkStateStatus.notSubscribed,
        DovahLinkStateStatus.unavailable,
        DovahLinkStateStatus.synchronized,
        DovahLinkStateStatus.stale,
        DovahLinkStateStatus.recovering,
        DovahLinkStateStatus.failed,
      ]);
    });
  });

  group('Property values in StateEventApplyResult behaves correctly', () {
    test('Property values in StateEventApplyResult includes every result', () {
      expect(StateEventApplyResult.values, <StateEventApplyResult>[
        StateEventApplyResult.applied,
        StateEventApplyResult.buffered,
        StateEventApplyResult.ignored,
        StateEventApplyResult.recoveryRequired,
      ]);
    });
  });
}
