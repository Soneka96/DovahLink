import 'package:flutter/material.dart';

import 'package:flutter_test/flutter_test.dart';
import 'package:fpdart/fpdart.dart';

import 'package:dovahlink_client/features/connection/domain/entities/host.entity.dart';
import 'package:dovahlink_client/features/connection/presentation/viewdata/host_card.viewdata.dart';
import 'package:dovahlink_client/features/pairing/data/models/pairing_handshake.model.dart';
import 'package:dovahlink_client/features/pairing/domain/entities/pairing_handshake.entity.dart';
import 'package:dovahlink_client/features/pairing/domain/entities/pairing_renotify_result.entity.dart';
import 'package:dovahlink_client/features/pairing/domain/usecases/params/authenticate.params.dart';
import 'package:dovahlink_client/features/session/presentation/viewdata/session_overview_quest.viewdata.dart';
import 'package:dovahlink_client/features/session/presentation/viewdata/session_overview_vitals.viewdata.dart';
import 'package:dovahlink_client/shared/constants/constants.dart';
import 'package:dovahlink_client/shared/constants/enums.dart';
import 'package:dovahlink_client/shared/theme/dovah_theme_tokens.dart';
import 'fixtures.dart';

import 'package:dovahlink_client_sdk/dovahlink_client.dart'
    show
        CharacterIdentityState,
        CharacterSupernaturalTraitsState,
        CharacterVitalsState,
        CredentialRejectionReason,
        DovahLinkStateStatus,
        DovahLinkTrustState,
        GameTimeState,
        PlayerLocationCellKind,
        PlayerLocationState,
        QuestObjective,
        StateSynchronization,
        TrackedQuest,
        TrackedQuestObjectiveState,
        TrackedQuestsState,
        HelloResult;

/// Exercises the Flutter app's representative typed fixture builders.
void main() {
  group('Method buildStateSynchronization behaves correctly', () {
    test('Method buildStateSynchronization keeps value and status', () {
      final StateSynchronization<int> synchronization =
          Fixtures.buildStateSynchronization<int>(value: 43);

      expect(synchronization.status, DovahLinkStateStatus.synchronized);
      expect(synchronization.value, isA<int>());
      expect(synchronization.value, 43);
      expect(synchronization.stateAuthorityId, 'authority-a');
      expect(synchronization.playContextId, 'context-a');
      expect(synchronization.revision, 1);
    });

    test(
      'Method buildStateSynchronization keeps explicit unavailable values',
      () {
        final StateSynchronization<int> synchronization =
            Fixtures.buildStateSynchronization<int>(
              status: DovahLinkStateStatus.failed,
              value: null,
              stateAuthorityId: null,
              playContextId: null,
              revision: null,
            );

        expect(synchronization.status, DovahLinkStateStatus.failed);
        expect(synchronization.value, isNull);
        expect(synchronization.stateAuthorityId, isNull);
        expect(synchronization.playContextId, isNull);
        expect(synchronization.revision, isNull);
      },
    );
  });

  group('Method buildSessionOverviewVitalsViewData behaves correctly', () {
    test(
      'Method buildSessionOverviewVitalsViewData carries ratios and status',
      () {
        final SessionOverviewVitalsViewData viewData =
            Fixtures.buildSessionOverviewVitalsViewData(
              value: Fixtures.buildCharacterVitals(),
              status: DovahLinkStateStatus.stale,
            );

        expect(viewData.status, DovahLinkStateStatus.stale);
        expect(viewData.healthRatio, closeTo(0.8, 0.0001));
        expect(viewData.healthCurrent, 80);
      },
    );
  });

  group('Method buildSessionOverviewQuestViewData behaves correctly', () {
    test(
      'Method buildSessionOverviewQuestViewData carries an empty summary',
      () {
        final SessionOverviewQuestViewData viewData =
            Fixtures.buildSessionOverviewQuestViewData(
              value: Fixtures.buildTrackedQuests(quests: <TrackedQuest>[]),
            );

        expect(viewData.title, 'NO QUEST TRACKED');
        expect(viewData.detail, 'No path is marked.');
      },
    );
  });

  group('Method buildHost behaves correctly', () {
    test('Method buildHost builds representative defaults', () {
      final Host host = Fixtures.buildHost();

      expect(host.displayName, isA<String>());
      expect(host.displayName, 'Local Host');
      expect(host.hostId, isA<String>());
      expect(host.hostId, '81869993-955c-4ba3-a7d0-d35ca86078ea');
      expect(host.uri, defaultHostUri);
    });

    test('Method buildHost preserves named overrides', () {
      final Uri uri = Uri.parse('ws://127.0.0.1:1/');
      final Host host = Fixtures.buildHost(
        hostId: 'aaaaaaaa-aaaa-4aaa-8aaa-aaaaaaaaaaaa',
        displayName: 'Test Host',
        uri: uri,
      );

      expect(host.hostId, isA<String>());
      expect(host.hostId, 'aaaaaaaa-aaaa-4aaa-8aaa-aaaaaaaaaaaa');
      expect(host.displayName, isA<String>());
      expect(host.displayName, 'Test Host');
      expect(host.uri, uri);
    });

    test('Method buildHost returns a fresh value per call', () {
      final Host first = Fixtures.buildHost();
      final Host second = Fixtures.buildHost();

      expect(first, second);
      expect(first.hashCode, second.hashCode);
      expect(identical(first, second), isFalse);
    });
  });

  group('Method buildAuthenticateParams behaves correctly', () {
    test('Method buildAuthenticateParams targets the representative Host', () {
      final AuthenticateParams params = Fixtures.buildAuthenticateParams();

      expect(params.target, Left(defaultHostUri));
    });

    test('Method buildAuthenticateParams preserves the named override', () {
      final Uri uri = Uri.parse('ws://127.0.0.1:2/');

      final AuthenticateParams params = Fixtures.buildAuthenticateParams(
        hostUri: uri,
      );

      expect(params.target, Left(uri));
    });

    test('Method buildAuthenticateParams can target a Known Host ID', () {
      const String hostId = '81869993-955c-4ba3-a7d0-d35ca86078ea';

      final AuthenticateParams params = Fixtures.buildAuthenticateParams(
        hostId: hostId,
      );

      expect(params.target, const Right(hostId));
    });
  });

  group('Method buildSdkHelloResult behaves correctly', () {
    test(
      'Method buildSdkHelloResult uses one representative Host identity',
      () {
        final HelloResult result = Fixtures.buildSdkHelloResult();

        expect(result.hostId, isA<String>());
        expect(result.hostId, '81869993-955c-4ba3-a7d0-d35ca86078ea');
        expect(result.hostName, isA<String>());
        expect(result.hostName, 'Soneka-Desktop');
        expect(result.hostVersion, '1.2.3');
        expect(result.trustState, DovahLinkTrustState.trusted);
        expect(result.recoveredFromRejectedCredential, isNull);
      },
    );

    test('Method buildSdkHelloResult preserves handshake overrides', () {
      final HelloResult result = Fixtures.buildSdkHelloResult(
        hostId: 'aaaaaaaa-aaaa-4aaa-8aaa-aaaaaaaaaaaa',
        hostName: 'LIVINGROOM-PC',
        hostVersion: '2.0.0',
        trustState: DovahLinkTrustState.unpaired,
        recoveredFromRejectedCredential: CredentialRejectionReason.revoked,
      );

      expect(result.hostId, 'aaaaaaaa-aaaa-4aaa-8aaa-aaaaaaaaaaaa');
      expect(result.hostName, 'LIVINGROOM-PC');
      expect(result.hostVersion, '2.0.0');
      expect(result.trustState, DovahLinkTrustState.unpaired);
      expect(
        result.recoveredFromRejectedCredential,
        CredentialRejectionReason.revoked,
      );
    });

    test('Method buildSdkHelloResult returns a fresh result per call', () {
      final HelloResult first = Fixtures.buildSdkHelloResult();
      final HelloResult second = Fixtures.buildSdkHelloResult();

      expect(identical(first, second), isFalse);
    });
  });

  group('Method buildHostCardViewData behaves correctly', () {
    test('Method buildHostCardViewData builds representative defaults', () {
      final HostCardViewData card = Fixtures.buildHostCardViewData();

      expect(card.host, Fixtures.buildHost());
      expect(card.title, isA<String>());
      expect(card.title, 'Local Host');
      expect(card.subtitle, isA<String>());
      expect(card.source, ConnectionHostSelectionSource.candidate);
      expect(card.subtitle, 'Discovered candidate');
      expect(card.detail, isA<String>());
      expect(card.detail, '127.0.0.1:58231');
      expect(card.state, DovahConnectionCardState.unknown);
      expect(card.pairingRequired, isA<bool>());
      expect(card.pairingRequired, isFalse);
    });

    test('Method buildHostCardViewData preserves named overrides', () {
      final Host host = Fixtures.buildHost(displayName: 'Other');
      final HostCardViewData card = Fixtures.buildHostCardViewData(
        host: host,
        title: 'Other',
        subtitle: 'Sub',
        detail: 'Detail',
        state: DovahConnectionCardState.repair,
        pairingRequired: true,
      );

      expect(card.host, host);
      expect(card.source, ConnectionHostSelectionSource.candidate);
      expect(card.title, isA<String>());
      expect(card.title, 'Other');
      expect(card.subtitle, isA<String>());
      expect(card.subtitle, 'Sub');
      expect(card.detail, isA<String>());
      expect(card.detail, 'Detail');
      expect(card.state, DovahConnectionCardState.repair);
      expect(card.pairingRequired, isTrue);
    });

    test('Method buildHostCardViewData returns a fresh value per call', () {
      final HostCardViewData first = Fixtures.buildHostCardViewData();
      final HostCardViewData second = Fixtures.buildHostCardViewData();

      expect(first, second);
      expect(first.hashCode, second.hashCode);
      expect(identical(first, second), isFalse);
    });
  });

  group('Method buildPairingHandshake behaves correctly', () {
    test('Method buildPairingHandshake builds representative defaults', () {
      final PairingHandshake handshake = Fixtures.buildPairingHandshake();

      expect(handshake.hostVersion, isA<String>());
      expect(handshake.hostVersion, '1.2.3');
      expect(handshake.trusted, isA<bool>());
      expect(handshake.trusted, isTrue);
      expect(handshake.credentialRejectionReason, isNull);
      expect(handshake.credentialRejectedMessage, isNull);
    });

    test('Method buildPairingHandshake preserves named overrides', () {
      final PairingHandshake handshake = Fixtures.buildPairingHandshake(
        hostVersion: '2.0.0',
        trusted: false,
        credentialRejectionReason: PairingCredentialRejectionReason.revoked,
        credentialRejectedMessage: 'Pairing is required again.',
      );

      expect(handshake.hostVersion, isA<String>());
      expect(handshake.hostVersion, '2.0.0');
      expect(handshake.trusted, isA<bool>());
      expect(handshake.trusted, isFalse);
      expect(
        handshake.credentialRejectionReason,
        PairingCredentialRejectionReason.revoked,
      );
      expect(handshake.credentialRejectedMessage, isA<String>());
      expect(handshake.credentialRejectedMessage, 'Pairing is required again.');
    });

    test(
      'Method buildPairingHandshake keeps trust and rejection independent',
      () {
        final PairingHandshake untrustedWithoutMessage =
            Fixtures.buildPairingHandshake(trusted: false);
        final PairingHandshake trustedWithMessage =
            Fixtures.buildPairingHandshake(
              credentialRejectedMessage: 'Pairing is required again.',
            );

        expect(untrustedWithoutMessage.trusted, isFalse);
        expect(untrustedWithoutMessage.credentialRejectionReason, isNull);
        expect(untrustedWithoutMessage.credentialRejectedMessage, isNull);
        expect(trustedWithMessage.trusted, isTrue);
        expect(
          trustedWithMessage.credentialRejectedMessage,
          'Pairing is required again.',
        );
      },
    );

    test('Method buildPairingHandshake returns a fresh value per call', () {
      final PairingHandshake first = Fixtures.buildPairingHandshake();
      final PairingHandshake second = Fixtures.buildPairingHandshake();

      expect(first, second);
      expect(first.hashCode, second.hashCode);
      expect(identical(first, second), isFalse);
    });
  });

  group('Method buildPairingHandshakeModel behaves correctly', () {
    test(
      'Method buildPairingHandshakeModel builds representative defaults',
      () {
        final PairingHandshakeModel model =
            Fixtures.buildPairingHandshakeModel();

        expect(model.hostVersion, '1.2.3');
        expect(model.trusted, isTrue);
        expect(model.credentialRejectionReason, isNull);
        expect(model.credentialRejectedMessage, isNull);
      },
    );

    test('Method buildPairingHandshakeModel preserves named overrides', () {
      final PairingHandshakeModel model = Fixtures.buildPairingHandshakeModel(
        hostVersion: '2.0.0',
        trusted: false,
        credentialRejectionReason: PairingCredentialRejectionReason.blocked,
        credentialRejectedMessage: 'Pairing is required again.',
      );

      expect(model.hostVersion, '2.0.0');
      expect(model.trusted, isFalse);
      expect(
        model.credentialRejectionReason,
        PairingCredentialRejectionReason.blocked,
      );
      expect(model.credentialRejectedMessage, 'Pairing is required again.');
    });
  });

  group('Method buildPairingRenotifyResult behaves correctly', () {
    test('Method buildPairingRenotifyResult preserves typed values', () {
      final PairingRenotifyResult result = Fixtures.buildPairingRenotifyResult(
        outcome: PairingRenotifyOutcome.cooldown,
        retryAfterSeconds: 3,
      );

      expect(result.outcome, PairingRenotifyOutcome.cooldown);
      expect(result.retryAfterSeconds, 3);
    });
  });

  group('Method buildDovahThemeTokens behaves correctly', () {
    test('Method buildDovahThemeTokens builds representative defaults', () {
      final DovahThemeTokens tokens = Fixtures.buildDovahThemeTokens();

      expect(tokens.background, isA<Color>());
      expect(tokens.background, const Color(0xFF05090E));
      expect(tokens.surface3, isA<Color>());
      expect(tokens.surface3, const Color(0xFF162735));
      expect(tokens.soft, isA<Color>());
      expect(tokens.soft, const Color(0x2174BDE8));
      expect(tokens.signal, isA<Color>());
      expect(tokens.signal, const Color(0xFF74BDE8));
      expect(tokens.primaryActionForeground, isA<Color>());
      expect(tokens.primaryActionForeground, const Color(0xFF1A0E04));
      expect(tokens.cornerStyle, isA<DovahPanelCornerStyle>());
      expect(tokens.cornerStyle, DovahPanelCornerStyle.doubleBevel);
      expect(tokens.cornerRadius, isA<double>());
      expect(tokens.cornerRadius, 3);
      expect(tokens.displayFontFamily, isA<String>());
      expect(tokens.displayFontFamily, 'Georgia');
      expect(tokens.displayFontFamilyFallback, const ['Times New Roman']);
      expect(tokens.eyebrow, isA<Color>());
      expect(tokens.eyebrow, const Color(0xFFE2A55E));
      expect(tokens.uppercaseLabels, isFalse);
      expect(tokens.rootHeaderRuleFraction, 0.36);
      expect(tokens.pageTitleLineHeight, 1.14);
      expect(tokens.preset, DovahThemePreset.dovah);
      expect(tokens.panelCornerRadius, isA<double>());
      expect(tokens.panelCornerRadius, 0);
      expect(tokens.primaryActionCornerRadius, isA<double>());
      expect(tokens.primaryActionCornerRadius, 0);
      expect(tokens.statusOffline, const Color(0xFF7C8993));
      expect(tokens.brandTagline, const Color(0xFF72899A));
      expect(tokens.brandAccent, const Color(0xFF74BDE8));
      expect(tokens.markIcon, const Color(0xFFE2A55E));
      expect(tokens.iconTileForeground, const Color(0xFF8ED6FF));
      expect(tokens.barTrack, const Color(0xFF202B34));
      expect(tokens.panelNote, const Color(0xFF667C8B));
      expect((tokens.heroScrim as LinearGradient).stops, const [0, 0.52, 1]);
      expect((tokens.heroFloorScrim as LinearGradient).stops, const [0, 0.72]);
    });

    test('Method buildDovahThemeTokens preserves named overrides', () {
      final DovahThemeTokens tokens = Fixtures.buildDovahThemeTokens(
        background: const Color(0xFF000000),
        surface3: const Color(0xFF010203),
        soft: const Color(0x04050607),
        cornerStyle: DovahPanelCornerStyle.rounded,
        cornerRadius: 13,
        eyebrow: const Color(0xFF010203),
        uppercaseLabels: true,
        preset: DovahThemePreset.hearth,
        panelCornerRadius: 14,
        primaryActionCornerRadius: 9,
      );

      expect(tokens.preset, DovahThemePreset.hearth);
      expect(tokens.panelCornerRadius, 14);
      expect(tokens.primaryActionCornerRadius, 9);
      expect(tokens.eyebrow, isA<Color>());
      expect(tokens.eyebrow, const Color(0xFF010203));
      expect(tokens.uppercaseLabels, isTrue);
      expect(tokens.background, isA<Color>());
      expect(tokens.background, const Color(0xFF000000));
      expect(tokens.surface3, isA<Color>());
      expect(tokens.surface3, const Color(0xFF010203));
      expect(tokens.soft, isA<Color>());
      expect(tokens.soft, const Color(0x04050607));
      expect(tokens.cornerStyle, isA<DovahPanelCornerStyle>());
      expect(tokens.cornerStyle, DovahPanelCornerStyle.rounded);
      expect(tokens.cornerRadius, isA<double>());
      expect(tokens.cornerRadius, 13);
    });

    test('Method buildDovahThemeTokens returns a fresh value per call', () {
      final DovahThemeTokens first = Fixtures.buildDovahThemeTokens();
      final DovahThemeTokens second = Fixtures.buildDovahThemeTokens();

      expect(first, second);
      expect(first.hashCode, second.hashCode);
      expect(identical(first, second), isFalse);
    });
  });

  group('Method buildCharacterVitals behaves correctly', () {
    test(
      'buildCharacterVitals builds the coherent representative resources',
      () {
        final CharacterVitalsState state = Fixtures.buildCharacterVitals();

        expect(state.health?.current, 80);
        expect(state.health?.max, 100);
        expect(state.magicka?.current, 40);
        expect(state.magicka?.max, 80);
        expect(state.stamina?.current, 50);
        expect(state.stamina?.max, 90);
      },
    );

    test('buildCharacterVitals can build explicit unavailability', () {
      final CharacterVitalsState state = Fixtures.buildCharacterVitals(
        isAvailable: false,
      );

      expect(state.isUnavailable, isTrue);
    });
  });

  group('Method buildCharacterXp behaves correctly', () {
    test('buildCharacterXp builds the representative XP value', () {
      expect(Fixtures.buildCharacterXp().value, 63.25);
    });

    test('buildCharacterXp preserves nullable unavailability', () {
      expect(Fixtures.buildCharacterXp(value: null).value, isNull);
    });
  });

  group('Method buildCharacterLevel behaves correctly', () {
    test('buildCharacterLevel builds the representative level', () {
      expect(Fixtures.buildCharacterLevel().value, 43);
    });

    test('buildCharacterLevel preserves nullable unavailability', () {
      expect(Fixtures.buildCharacterLevel(value: null).value, isNull);
    });
  });

  group('Method buildCharacterIdentity behaves correctly', () {
    test('buildCharacterIdentity builds the complete identity', () {
      final CharacterIdentityState identity = Fixtures.buildCharacterIdentity();

      expect(identity.name, 'Player');
      expect(identity.race, 'Nord');
      expect(
        Fixtures.buildCharacterIdentity(name: 'Aela', race: 'Nord').name,
        'Aela',
      );
    });
  });

  group('Method buildSupernaturalTraits behaves correctly', () {
    test('buildSupernaturalTraits keeps the predicates independent', () {
      final CharacterSupernaturalTraitsState defaults =
          Fixtures.buildSupernaturalTraits();
      final CharacterSupernaturalTraitsState customized =
          Fixtures.buildSupernaturalTraits(
            isVampire: true,
            hasWerewolfForm: true,
          );

      expect(defaults.isVampire, isFalse);
      expect(defaults.hasVampireLordForm, isFalse);
      expect(defaults.hasWerewolfForm, isFalse);
      expect(customized.isVampire, isTrue);
      expect(customized.hasVampireLordForm, isFalse);
      expect(customized.hasWerewolfForm, isTrue);
    });
  });

  group('Method buildPlayerLocation behaves correctly', () {
    test('buildPlayerLocation preserves separate optional location facts', () {
      final PlayerLocationState location = Fixtures.buildPlayerLocation();

      expect(location.cellId, 22);
      expect(location.cellKind, PlayerLocationCellKind.interior);
      expect(location.cellName, 'Whiterun');
      expect(location.locationId, 23);
      expect(location.locationName, 'The Bannered Mare');
      expect(
        Fixtures.buildPlayerLocation(
          locationId: null,
          locationName: null,
        ).locationName,
        isNull,
      );
    });
  });

  group('Method buildGameTime behaves correctly', () {
    test('buildGameTime builds the representative Skyrim calendar values', () {
      final GameTimeState time = Fixtures.buildGameTime();

      expect(time.year, 4);
      expect(time.month, 8);
      expect(time.monthName, 'Last Seed');
      expect(time.day, 12);
      expect(time.hour, 14);
      expect(time.minute, 30);
    });
  });

  group('Method buildQuestObjective behaves correctly', () {
    test('buildQuestObjective builds a representative objective', () {
      final QuestObjective objective = Fixtures.buildQuestObjective();

      expect(objective.index, 0);
      expect(objective.instanceId, 1);
      expect(objective.text, 'Complete the objective');
      expect(objective.state, TrackedQuestObjectiveState.displayed);
    });
  });

  group('Method buildTrackedQuest behaves correctly', () {
    test('buildTrackedQuest builds the quest and its objective instance', () {
      final TrackedQuest quest = Fixtures.buildTrackedQuest();

      expect(quest.questId, 1);
      expect(quest.title, 'Test Quest');
      expect(quest.type, 0);
      expect(quest.objectives.single.text, 'Complete the objective');
      expect(
        Fixtures.buildTrackedQuest(objectives: const []).objectives,
        isEmpty,
      );
    });
  });

  group('Method buildTrackedQuests behaves correctly', () {
    test(
      'buildTrackedQuests distinguishes populated and empty collections',
      () {
        final TrackedQuestsState populated = Fixtures.buildTrackedQuests();
        final TrackedQuestsState empty = Fixtures.buildTrackedQuests(
          quests: const [],
        );

        expect(populated.quests.single.title, 'Test Quest');
        expect(empty.quests, isEmpty);
      },
    );
  });
}
