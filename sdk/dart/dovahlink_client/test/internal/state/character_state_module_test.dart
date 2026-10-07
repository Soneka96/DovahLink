import 'package:test/test.dart';

import 'package:dovahlink_client_sdk/src/dovahlink_protocol_exception.dart';
import 'package:dovahlink_client_sdk/src/internal/state/character_state_module.dart';
import 'package:dovahlink_client_sdk/src/internal/state/state_domain_definition.dart';
import 'package:dovahlink_client_sdk/src/protocol/envelope.dart';
import 'package:dovahlink_client_sdk/src/protocol/state_event_payload.dart';
import 'package:dovahlink_client_sdk/src/protocol/state_snapshot_payload.dart';
import 'package:dovahlink_client_sdk/src/shared/enums.dart';
import 'package:dovahlink_client_sdk/src/state/character_identity_state.dart';
import 'package:dovahlink_client_sdk/src/state/character_supernatural_traits_state.dart';
import 'package:dovahlink_client_sdk/src/state/state_synchronization.dart';

/// Tests Character domain registrations and their public tracker views.
void main() {
  late ICharacterStateModule module;

  setUp(() {
    module = CharacterStateModule();
  });

  group('Property domains behaves correctly', () {
    test('Property domains contains the five Character registrations', () {
      expect(
        module.domains
            .map((IStateDomainDefinition<Object?> domain) => domain.stateArea)
            .toList(),
        <String>[
          'character_vitals',
          'character_xp',
          'character_identity',
          'character_supernatural_traits',
          'character_level',
        ],
      );
    });

    test(
      'Property domains shares the recovery registration with Level dispatch',
      () {
        expect(module.levelDomain, same(module.domains.last));
        expect(module.levelDomain.stateArea, 'character_level');
      },
    );

    test(
      'Property domains keeps Vitals and XP Snapshot-only and Level Event-capable',
      () {
        const Envelope envelope = Envelope(
          messageType: ProtocolMessageType.stateEvent,
          messageId: 'event-1',
          sessionId: 'session-1',
          correlationId: null,
          payload: <String, dynamic>{},
          stateAuthorityId: 'authority-1',
          playContextId: 'context-1',
          clientId: null,
        );
        const StateEventPayload payload = StateEventPayload(
          stateArea: 'character_level',
          baseRevision: 1,
          revision: 2,
          occurredAt: '2026-09-23T12:00:00Z',
          data: <String, dynamic>{'value': 11},
        );

        for (final IStateDomainDefinition<Object?> domain
            in module.domains.where(
              (IStateDomainDefinition<Object?> domain) =>
                  domain.stateArea != 'character_level',
            )) {
          expect(
            () => domain.applyEvent(envelope: envelope, payload: payload),
            throwsA(isA<DovahLinkProtocolException>()),
          );
        }
        expect(
          () => module.levelDomain.applyEvent(
            envelope: envelope,
            payload: payload,
          ),
          returnsNormally,
        );
      },
    );
  });

  group('Property character behaves correctly', () {
    test('Property character streams start notSubscribed', () async {
      expect(
        (await module.character.vitalsChanges.first).status,
        DovahLinkStateStatus.notSubscribed,
      );
      expect(
        (await module.character.xpChanges.first).status,
        DovahLinkStateStatus.notSubscribed,
      );
      expect(
        (await module.character.levelChanges.first).status,
        DovahLinkStateStatus.notSubscribed,
      );
      expect(
        (await module.character.identityChanges.first).status,
        DovahLinkStateStatus.notSubscribed,
      );
      expect(
        (await module.character.supernaturalTraitsChanges.first).status,
        DovahLinkStateStatus.notSubscribed,
      );
    });
  });

  group('Method applySnapshot behaves correctly', () {
    test(
      'Method applySnapshot synchronizes and recovers metadata domains with typed values or unavailability',
      () async {
        const Envelope envelope = Envelope(
          messageType: ProtocolMessageType.stateSnapshot,
          messageId: 'snapshot-1',
          sessionId: 'session-1',
          correlationId: null,
          payload: <String, dynamic>{},
          stateAuthorityId: 'authority-1',
          playContextId: 'context-1',
          clientId: null,
        );
        final IStateDomainDefinition<Object?> identityDomain = module.domains
            .singleWhere(
              (IStateDomainDefinition<Object?> domain) =>
                  domain.stateArea == 'character_identity',
            );
        identityDomain.applySnapshot(
          envelope: envelope,
          payload: const StateSnapshotPayload(
            stateArea: 'character_identity',
            revision: 1,
            occurredAt: '2026-10-04T12:00:00Z',
            data: <String, dynamic>{
              'value': <String, dynamic>{'name': 'Gonçalo', 'race': 'Nord'},
            },
          ),
        );

        StateSynchronization<CharacterIdentityState?> identity =
            await module.character.identityChanges.first;
        expect(identity.status, DovahLinkStateStatus.synchronized);
        expect(identity.value?.name, 'Gonçalo');
        expect(identity.value?.race, 'Nord');
        expect(identity.stateAuthorityId, 'authority-1');
        expect(identity.playContextId, 'context-1');
        expect(identity.revision, 1);

        identityDomain.tracker.beginRecovery();
        identity = await module.character.identityChanges.first;
        expect(identity.status, DovahLinkStateStatus.recovering);
        expect(identity.value?.name, 'Gonçalo');

        identityDomain.applySnapshot(
          envelope: envelope,
          payload: const StateSnapshotPayload(
            stateArea: 'character_identity',
            revision: 2,
            occurredAt: '2026-10-04T12:00:01Z',
            data: <String, dynamic>{'value': null},
          ),
        );
        identity = await module.character.identityChanges.first;
        expect(identity.status, DovahLinkStateStatus.unavailable);
        expect(identity.value?.name, 'Gonçalo');
        expect(identity.value?.race, 'Nord');
        expect(identity.revision, 2);

        final IStateDomainDefinition<Object?> traitsDomain = module.domains
            .singleWhere(
              (IStateDomainDefinition<Object?> domain) =>
                  domain.stateArea == 'character_supernatural_traits',
            );
        traitsDomain.applySnapshot(
          envelope: envelope,
          payload: const StateSnapshotPayload(
            stateArea: 'character_supernatural_traits',
            revision: 1,
            occurredAt: '2026-10-04T12:00:00Z',
            data: <String, dynamic>{
              'value': <String, dynamic>{
                'isVampire': false,
                'hasVampireLordForm': false,
                'hasWerewolfForm': false,
              },
            },
          ),
        );
        final StateSynchronization<CharacterSupernaturalTraitsState?> traits =
            await module.character.supernaturalTraitsChanges.first;
        expect(traits.status, DovahLinkStateStatus.synchronized);
        expect(traits.value?.isVampire, isFalse);
        expect(traits.value?.hasVampireLordForm, isFalse);
        expect(traits.value?.hasWerewolfForm, isFalse);
        expect(traits.stateAuthorityId, 'authority-1');
        expect(traits.playContextId, 'context-1');
        expect(traits.revision, 1);

        traitsDomain.tracker.beginRecovery();
        traitsDomain.applySnapshot(
          envelope: envelope,
          payload: const StateSnapshotPayload(
            stateArea: 'character_supernatural_traits',
            revision: 2,
            occurredAt: '2026-10-04T12:00:01Z',
            data: <String, dynamic>{'value': null},
          ),
        );
        final StateSynchronization<CharacterSupernaturalTraitsState?>
        unavailableTraits =
            await module.character.supernaturalTraitsChanges.first;
        expect(unavailableTraits.status, DovahLinkStateStatus.unavailable);
        expect(unavailableTraits.value?.isVampire, isFalse);
        expect(unavailableTraits.value?.hasVampireLordForm, isFalse);
        expect(unavailableTraits.value?.hasWerewolfForm, isFalse);
        expect(unavailableTraits.revision, 2);
      },
    );
  });
}
