import 'package:test/test.dart';

import 'package:dovahlink_client_sdk/src/dovahlink_protocol_exception.dart';
import 'package:dovahlink_client_sdk/src/internal/state/character_state_module.dart';
import 'package:dovahlink_client_sdk/src/internal/state/state_domain_definition.dart';
import 'package:dovahlink_client_sdk/src/protocol/envelope.dart';
import 'package:dovahlink_client_sdk/src/protocol/state_event_payload.dart';
import 'package:dovahlink_client_sdk/src/shared/enums.dart';

/// Tests Character domain registrations and their public tracker views.
void main() {
  late ICharacterStateModule module;

  setUp(() {
    module = CharacterStateModule();
  });

  group('Property domains behaves correctly', () {
    test('Property domains contains the three Character registrations', () {
      expect(
        module.domains
            .map((IStateDomainDefinition<Object?> domain) => domain.stateArea)
            .toList(),
        <String>['character_vitals', 'character_xp', 'character_level'],
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
            in module.domains.take(2)) {
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
    });
  });
}
