import 'dart:async';

import 'package:test/test.dart';

import 'package:dovahlink_client_sdk/src/internal/readiness/gameplay_readiness_service.dart';
import 'package:dovahlink_client_sdk/src/shared/enums.dart';
import '../../fixtures/fixtures.dart';

/// Delivers one readiness message to [service].
void _observe(
  IGameplayReadinessService service, {
  String sessionId = 'session-1',
  String authority = 'authority-1',
  required bool ready,
  required int sequence,
}) => service.observe(
  sessionId: sessionId,
  stateAuthorityId: authority,
  payload: Fixtures.buildGameplayReadinessPayload(
    ready: ready,
    sequence: sequence,
  ),
);

/// Admits [sessionId] on [service] under [authority].
void _admit(
  IGameplayReadinessService service, {
  String sessionId = 'session-1',
  String authority = 'authority-1',
}) => service.onSessionAdmitted(
  sessionId: sessionId,
  stateAuthorityId: authority,
);

/// The most superseded authorities one session remembers.
const int _bound = GameplayReadinessService.maxRememberedSupersededAuthorities;

/// Rotates [service] through [count] authorities, each revealed by a state message.
void _rotate(IGameplayReadinessService service, int count) {
  for (int i = 1; i <= count; i++) {
    service.onStateAuthorityObserved('authority-$i');
  }
}

/// Runs [GameplayReadinessService] behavior tests.
void main() {
  late GameplayReadinessService service;

  setUp(() {
    service = GameplayReadinessService();
  });

  group('Property current behaves correctly', () {
    test('Property current starts unknown', () {
      expect(service.current, DovahLinkGameplayReadiness.unknown);
    });

    test('Property current maps ready false to notReady and true to ready', () {
      _admit(service);

      _observe(service, ready: false, sequence: 1);
      expect(service.current, DovahLinkGameplayReadiness.notReady);

      _observe(service, ready: true, sequence: 2);
      expect(service.current, DovahLinkGameplayReadiness.ready);
    });

    test('Property current moves ready to notReady and back without reset', () {
      _admit(service);
      _observe(service, ready: true, sequence: 1);

      _observe(service, ready: false, sequence: 2);
      expect(service.current, DovahLinkGameplayReadiness.notReady);

      _observe(service, ready: true, sequence: 3);
      expect(service.current, DovahLinkGameplayReadiness.ready);
    });
  });

  group('Property changes behaves correctly', () {
    test(
      'Property changes replays the current value to a late subscriber',
      () async {
        _admit(service);
        _observe(service, ready: true, sequence: 1);

        expect(await service.changes.first, DovahLinkGameplayReadiness.ready);
      },
    );

    test(
      'Property changes replays unknown to a subscriber before any report',
      () async {
        expect(await service.changes.first, DovahLinkGameplayReadiness.unknown);
      },
    );

    test(
      'Property changes emits each real transition once and no duplicate',
      () async {
        final List<DovahLinkGameplayReadiness> seen =
            <DovahLinkGameplayReadiness>[];
        final StreamSubscription<DovahLinkGameplayReadiness> subscription =
            service.changes.listen(seen.add);
        _admit(service);

        _observe(service, ready: true, sequence: 1);
        _observe(service, ready: true, sequence: 2);
        _observe(service, ready: false, sequence: 3);
        service.reset();
        await Future<void>.delayed(Duration.zero);
        await subscription.cancel();

        expect(seen, <DovahLinkGameplayReadiness>[
          DovahLinkGameplayReadiness.unknown,
          DovahLinkGameplayReadiness.ready,
          DovahLinkGameplayReadiness.notReady,
          DovahLinkGameplayReadiness.unknown,
        ]);
      },
    );
  });

  group('Method observe behaves correctly', () {
    test('Method observe ignores a duplicate sequence', () {
      _admit(service);
      _observe(service, ready: true, sequence: 4);

      _observe(service, ready: false, sequence: 4);

      expect(service.current, DovahLinkGameplayReadiness.ready);
    });

    test('Method observe ignores a lower sequence', () {
      _admit(service);
      _observe(service, ready: true, sequence: 4);

      _observe(service, ready: false, sequence: 3);

      expect(service.current, DovahLinkGameplayReadiness.ready);
    });

    test('Method observe accepts a higher sequence that skips values', () {
      _admit(service);
      _observe(service, ready: true, sequence: 2);

      _observe(service, ready: false, sequence: 9);

      expect(service.current, DovahLinkGameplayReadiness.notReady);
    });

    test('Method observe rejects a message from a different session', () {
      _admit(service);

      _observe(service, sessionId: 'session-2', ready: true, sequence: 1);

      expect(service.current, DovahLinkGameplayReadiness.unknown);
    });

    test('Method observe does nothing after close', () {
      _admit(service);
      service.close();

      _observe(service, ready: true, sequence: 1);
      _admit(service);
      _observe(service, ready: true, sequence: 1);

      expect(service.current, DovahLinkGameplayReadiness.unknown);
    });

    test(
      'Method observe applies a newer authority as a fresh epoch and keeps the sequence',
      () {
        _admit(service);
        _observe(service, ready: true, sequence: 1);

        _observe(service, authority: 'authority-2', ready: false, sequence: 2);
        expect(service.current, DovahLinkGameplayReadiness.notReady);

        _observe(service, authority: 'authority-2', ready: true, sequence: 2);
        expect(service.current, DovahLinkGameplayReadiness.notReady);
      },
    );

    test('Method observe discards a message from a superseded authority', () {
      _admit(service);
      _observe(service, ready: true, sequence: 1);
      _observe(service, authority: 'authority-2', ready: false, sequence: 2);

      _observe(service, authority: 'authority-1', ready: true, sequence: 3);

      expect(service.current, DovahLinkGameplayReadiness.notReady);
    });

    test(
      'Method observe does not infer authority order from the identifiers',
      () {
        _admit(service, authority: 'z-authority');

        _observe(service, authority: 'a-authority', ready: true, sequence: 1);

        expect(service.current, DovahLinkGameplayReadiness.ready);
      },
    );
  });

  group('Method onSessionAdmitted behaves correctly', () {
    test(
      'Method onSessionAdmitted applies an observation staged before admission',
      () {
        _observe(service, ready: true, sequence: 1);
        expect(service.current, DovahLinkGameplayReadiness.unknown);

        _admit(service);

        expect(service.current, DovahLinkGameplayReadiness.ready);
      },
    );

    test(
      'Method onSessionAdmitted keeps only the newest staged observation',
      () {
        _observe(service, ready: true, sequence: 2);
        _observe(service, ready: false, sequence: 1);
        _observe(service, ready: false, sequence: 3);

        _admit(service);

        expect(service.current, DovahLinkGameplayReadiness.notReady);
      },
    );

    test(
      'Method onSessionAdmitted discards an observation staged for another session',
      () {
        _observe(service, sessionId: 'old-session', ready: true, sequence: 1);

        _admit(service, sessionId: 'new-session');

        expect(service.current, DovahLinkGameplayReadiness.unknown);
        _observe(service, sessionId: 'new-session', ready: false, sequence: 1);
        expect(service.current, DovahLinkGameplayReadiness.notReady);
      },
    );

    test(
      'Method onSessionAdmitted lets a newer connection replace a stale staged session',
      () {
        _observe(service, sessionId: 'old-session', ready: true, sequence: 9);
        _observe(service, sessionId: 'new-session', ready: false, sequence: 1);

        _admit(service, sessionId: 'new-session');

        expect(service.current, DovahLinkGameplayReadiness.notReady);
      },
    );

    test(
      'Method onSessionAdmitted does not publish a staged value to the next admission',
      () {
        _observe(service, ready: true, sequence: 1);
        _admit(service);
        service.reset();

        _admit(service);

        expect(service.current, DovahLinkGameplayReadiness.unknown);
      },
    );

    test('Method onSessionAdmitted starts a new session at sequence zero', () {
      _admit(service);
      _observe(service, ready: true, sequence: 7);

      _admit(service, sessionId: 'session-2');
      _observe(service, sessionId: 'session-2', ready: false, sequence: 1);

      expect(service.current, DovahLinkGameplayReadiness.notReady);
    });

    test(
      'Method onSessionAdmitted rejects an old session message after replacement',
      () {
        _admit(service);
        _observe(service, ready: true, sequence: 1);
        _admit(service, sessionId: 'session-2');

        _observe(service, sessionId: 'session-1', ready: true, sequence: 99);

        expect(service.current, DovahLinkGameplayReadiness.unknown);
      },
    );

    test(
      'Method onSessionAdmitted accepts a staged observation under a newer authority',
      () {
        _observe(service, authority: 'authority-2', ready: true, sequence: 1);

        _admit(service, authority: 'authority-1');

        expect(service.current, DovahLinkGameplayReadiness.ready);
      },
    );
  });

  group('Method observe authority adoption behaves correctly', () {
    test(
      'Method observe does not adopt the authority of a rejected message',
      () {
        _admit(service);
        _observe(service, ready: true, sequence: 5);

        _observe(service, authority: 'authority-2', ready: false, sequence: 5);
        _observe(
          service,
          sessionId: 'other',
          authority: 'authority-3',
          ready: false,
          sequence: 9,
        );
        _observe(service, authority: 'authority-1', ready: false, sequence: 6);

        expect(service.current, DovahLinkGameplayReadiness.notReady);
      },
    );

    test(
      'Method observe rejects the admitted authority once a staged newer one is applied',
      () {
        _observe(service, authority: 'authority-2', ready: true, sequence: 1);
        _admit(service, authority: 'authority-1');

        _observe(service, authority: 'authority-1', ready: false, sequence: 2);

        expect(service.current, DovahLinkGameplayReadiness.ready);
      },
    );

    test(
      'Method observe keeps the first staged observation of an equal sequence',
      () {
        _observe(service, ready: true, sequence: 1);
        _observe(service, ready: false, sequence: 1);

        _admit(service);

        expect(service.current, DovahLinkGameplayReadiness.ready);
      },
    );

    test(
      'Method observe stages a message from an ended session and lets admission discard it',
      () {
        _admit(service);
        service.reset();
        _observe(service, ready: true, sequence: 3);

        _admit(service, sessionId: 'session-2');

        expect(service.current, DovahLinkGameplayReadiness.unknown);
      },
    );
  });

  group('Property changes for lifecycle events behaves correctly', () {
    test(
      'Property changes emits unknown then ready for a staged observation applied at admission',
      () async {
        final List<DovahLinkGameplayReadiness> seen =
            <DovahLinkGameplayReadiness>[];
        final StreamSubscription<DovahLinkGameplayReadiness> subscription =
            service.changes.listen(seen.add);
        _observe(service, ready: true, sequence: 1);

        _admit(service);
        await Future<void>.delayed(Duration.zero);
        await subscription.cancel();

        expect(seen, <DovahLinkGameplayReadiness>[
          DovahLinkGameplayReadiness.unknown,
          DovahLinkGameplayReadiness.ready,
        ]);
      },
    );

    test(
      'Property changes emits unknown once for an authority rotation',
      () async {
        _admit(service);
        _observe(service, ready: true, sequence: 1);
        final List<DovahLinkGameplayReadiness> seen =
            <DovahLinkGameplayReadiness>[];
        final StreamSubscription<DovahLinkGameplayReadiness> subscription =
            service.changes.listen(seen.add);

        service.onStateAuthorityObserved('authority-2');
        service.onStateAuthorityObserved('authority-2');
        await Future<void>.delayed(Duration.zero);
        await subscription.cancel();

        expect(seen, <DovahLinkGameplayReadiness>[
          DovahLinkGameplayReadiness.ready,
          DovahLinkGameplayReadiness.unknown,
        ]);
      },
    );
  });

  group('Method onStateAuthorityObserved behaves correctly', () {
    test(
      'Method onStateAuthorityObserved invalidates readiness on a rotation',
      () {
        _admit(service);
        _observe(service, ready: true, sequence: 1);

        service.onStateAuthorityObserved('authority-2');

        expect(service.current, DovahLinkGameplayReadiness.unknown);
      },
    );

    test(
      'Method onStateAuthorityObserved keeps readiness for the unchanged authority',
      () {
        _admit(service);
        _observe(service, ready: true, sequence: 1);

        service.onStateAuthorityObserved('authority-1');

        expect(service.current, DovahLinkGameplayReadiness.ready);
      },
    );

    test(
      'Method onStateAuthorityObserved stops a stale old-authority message restoring readiness',
      () {
        _admit(service);
        _observe(service, ready: true, sequence: 1);
        service.onStateAuthorityObserved('authority-2');

        _observe(service, authority: 'authority-1', ready: true, sequence: 2);

        expect(service.current, DovahLinkGameplayReadiness.unknown);
      },
    );

    test(
      'Method onStateAuthorityObserved lets the new epoch establish readiness',
      () {
        _admit(service);
        _observe(service, ready: true, sequence: 1);
        service.onStateAuthorityObserved('authority-2');

        _observe(service, authority: 'authority-2', ready: false, sequence: 2);

        expect(service.current, DovahLinkGameplayReadiness.notReady);
      },
    );

    test(
      'Method onStateAuthorityObserved ignores a superseded authority seen late',
      () {
        _admit(service);
        _observe(service, authority: 'authority-2', ready: true, sequence: 1);

        service.onStateAuthorityObserved('authority-1');

        expect(service.current, DovahLinkGameplayReadiness.ready);
      },
    );

    test(
      'Method onStateAuthorityObserved is ignored without an admitted session',
      () {
        service.onStateAuthorityObserved('authority-2');
        _admit(service, authority: 'authority-1');
        _observe(service, ready: true, sequence: 1);

        expect(service.current, DovahLinkGameplayReadiness.ready);
      },
    );

    test(
      'Method onStateAuthorityObserved still rejects the oldest authority at exactly the bound',
      () {
        _admit(service, authority: 'authority-0');
        _rotate(
          service,
          GameplayReadinessService.maxRememberedSupersededAuthorities,
        );

        _observe(service, authority: 'authority-0', ready: true, sequence: 1);
        expect(service.current, DovahLinkGameplayReadiness.unknown);

        _observe(
          service,
          authority: 'authority-$_bound',
          ready: true,
          sequence: 1,
        );
        expect(service.current, DovahLinkGameplayReadiness.ready);
      },
    );
  });

  group('Behavior authority history exhaustion fails closed', () {
    test(
      'Method onStateAuthorityObserved keeps the oldest authority unusable after the bound is exceeded',
      () {
        _admit(service, authority: 'authority-0');
        _rotate(service, _bound + 1);

        _observe(service, authority: 'authority-0', ready: true, sequence: 1);

        expect(service.current, DovahLinkGameplayReadiness.unknown);
      },
    );

    test(
      'Method onStateAuthorityObserved leaves readiness unknown for every authority once exhausted',
      () {
        _admit(service, authority: 'authority-0');
        _observe(service, authority: 'authority-0', ready: true, sequence: 1);
        _rotate(service, _bound + 1);

        _observe(
          service,
          authority: 'authority-${_bound + 1}',
          ready: true,
          sequence: 2,
        );
        _observe(service, authority: 'authority-5', ready: true, sequence: 3);
        _observe(service, authority: 'never-seen', ready: true, sequence: 4);

        expect(service.current, DovahLinkGameplayReadiness.unknown);
      },
    );

    test(
      'Method observe rotating past the bound through readiness ends at unknown, not ready',
      () {
        _admit(service, authority: 'authority-0');
        _rotate(service, _bound);

        _observe(
          service,
          authority: 'fresh-authority',
          ready: true,
          sequence: 1,
        );

        expect(service.current, DovahLinkGameplayReadiness.unknown);
      },
    );

    test(
      'Method onStateAuthorityObserved emits unknown once on exhaustion',
      () async {
        final List<DovahLinkGameplayReadiness> seen =
            <DovahLinkGameplayReadiness>[];
        _admit(service, authority: 'authority-0');
        _observe(service, authority: 'authority-0', ready: true, sequence: 1);
        final StreamSubscription<DovahLinkGameplayReadiness> subscription =
            service.changes.listen(seen.add);

        _rotate(service, _bound + 3);
        await Future<void>.delayed(Duration.zero);
        await subscription.cancel();

        expect(seen, <DovahLinkGameplayReadiness>[
          DovahLinkGameplayReadiness.ready,
          DovahLinkGameplayReadiness.unknown,
        ]);
      },
    );

    test(
      'Method observe emits unknown when readiness rotation exhausts the history',
      () async {
        final List<DovahLinkGameplayReadiness> seen =
            <DovahLinkGameplayReadiness>[];
        _admit(service, authority: 'authority-0');
        _rotate(service, _bound);
        _observe(
          service,
          authority: 'authority-$_bound',
          ready: true,
          sequence: 1,
        );
        final StreamSubscription<DovahLinkGameplayReadiness> subscription =
            service.changes.listen(seen.add);

        _observe(
          service,
          authority: 'fresh-authority',
          ready: true,
          sequence: 2,
        );
        await Future<void>.delayed(Duration.zero);
        await subscription.cancel();

        expect(seen, <DovahLinkGameplayReadiness>[
          DovahLinkGameplayReadiness.ready,
          DovahLinkGameplayReadiness.unknown,
        ]);
      },
    );

    test(
      'Property changes replays unknown to a late subscriber after exhaustion',
      () async {
        _admit(service, authority: 'authority-0');
        _observe(service, authority: 'authority-0', ready: true, sequence: 1);
        _rotate(service, _bound + 1);

        expect(await service.changes.first, DovahLinkGameplayReadiness.unknown);
      },
    );

    test(
      'Method observe rejecting a message at a full history does not exhaust it',
      () {
        _admit(service, authority: 'authority-0');
        _rotate(service, _bound);
        _observe(
          service,
          authority: 'authority-$_bound',
          ready: true,
          sequence: 2,
        );

        _observe(service, authority: 'authority-0', ready: false, sequence: 9);
        _observe(
          service,
          authority: 'authority-$_bound',
          ready: false,
          sequence: 1,
        );
        _observe(
          service,
          sessionId: 'other-session',
          authority: 'fresh-authority',
          ready: false,
          sequence: 9,
        );

        expect(service.current, DovahLinkGameplayReadiness.ready);
      },
    );

    test(
      'Method onSessionAdmitted clears exhaustion for the replacement session',
      () {
        _admit(service, sessionId: 'session-1', authority: 'authority-0');
        _rotate(service, _bound + 1);

        _admit(service, sessionId: 'session-2', authority: 'authority-0');
        _observe(
          service,
          sessionId: 'session-2',
          authority: 'authority-0',
          ready: true,
          sequence: 1,
        );

        expect(service.current, DovahLinkGameplayReadiness.ready);
      },
    );

    test(
      'Method reset clears exhaustion so a reconnect starts without old evidence',
      () {
        _admit(service, authority: 'authority-0');
        _rotate(service, _bound + 1);
        service.reset();

        _admit(service, authority: 'authority-new');
        _observe(
          service,
          authority: 'authority-new',
          ready: false,
          sequence: 1,
        );

        expect(service.current, DovahLinkGameplayReadiness.notReady);
      },
    );

    test('Method close keeps an exhausted service inert', () {
      _admit(service, authority: 'authority-0');
      _rotate(service, _bound + 1);
      service.close();

      _observe(service, authority: 'authority-0', ready: true, sequence: 1);

      expect(service.current, DovahLinkGameplayReadiness.unknown);
    });
  });

  group('Behavior authority provenance behaves correctly', () {
    test(
      'Method onStateAuthorityObserved is idempotent for the same authority',
      () {
        _admit(service);
        _observe(service, ready: true, sequence: 1);

        service.onStateAuthorityObserved('authority-1');
        service.onStateAuthorityObserved('authority-1');

        expect(service.current, DovahLinkGameplayReadiness.ready);
      },
    );

    test(
      'Method onStateAuthorityObserved cannot replace a newer authority with a superseded one',
      () {
        _admit(service);
        service.onStateAuthorityObserved('authority-2');
        _observe(service, authority: 'authority-2', ready: true, sequence: 1);

        service.onStateAuthorityObserved('authority-1');

        expect(service.current, DovahLinkGameplayReadiness.ready);
      },
    );

    test(
      'Method observe rejects a higher sequence from a superseded authority after rotation',
      () {
        _admit(service);
        _observe(service, ready: true, sequence: 1);
        service.onStateAuthorityObserved('authority-2');
        _observe(service, authority: 'authority-2', ready: false, sequence: 2);

        _observe(service, authority: 'authority-1', ready: true, sequence: 9);

        expect(service.current, DovahLinkGameplayReadiness.notReady);
      },
    );

    test(
      'Method observe rejects an older sequence after a newer authority result',
      () {
        _admit(service);
        service.onStateAuthorityObserved('authority-2');
        _observe(service, authority: 'authority-2', ready: false, sequence: 5);

        _observe(service, authority: 'authority-2', ready: true, sequence: 4);

        expect(service.current, DovahLinkGameplayReadiness.notReady);
      },
    );
  });

  group('Method reset behaves correctly', () {
    test('Method reset returns to unknown and rejects the old session', () {
      _admit(service);
      _observe(service, ready: true, sequence: 1);

      service.reset();

      expect(service.current, DovahLinkGameplayReadiness.unknown);
      _observe(service, ready: true, sequence: 2);
      _admit(service, sessionId: 'session-2');
      expect(service.current, DovahLinkGameplayReadiness.unknown);
    });

    test('Method reset discards a staged observation', () {
      _observe(service, ready: true, sequence: 1);

      service.reset();
      _admit(service);

      expect(service.current, DovahLinkGameplayReadiness.unknown);
    });
  });

  group('Method close behaves correctly', () {
    test('Method close makes reset and authority observation inert', () {
      _admit(service);
      service.close();

      service.reset();
      service.onStateAuthorityObserved('authority-2');
      _observe(service, ready: true, sequence: 1);

      expect(service.current, DovahLinkGameplayReadiness.unknown);
    });

    test('Method close returns to unknown', () {
      _admit(service);
      _observe(service, ready: true, sequence: 1);

      service.close();

      expect(service.current, DovahLinkGameplayReadiness.unknown);
    });
  });

  group('Behavior instance isolation behaves correctly', () {
    test('Behavior two services keep independent readiness', () {
      final GameplayReadinessService other = GameplayReadinessService();
      _admit(service);
      _admit(other);

      _observe(service, ready: true, sequence: 1);

      expect(service.current, DovahLinkGameplayReadiness.ready);
      expect(other.current, DovahLinkGameplayReadiness.unknown);
    });
  });
}
