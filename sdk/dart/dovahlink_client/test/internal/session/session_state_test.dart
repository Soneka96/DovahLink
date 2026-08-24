import 'dart:async';

import 'package:test/test.dart';

import 'package:dovahlink_client_sdk/src/internal/session/session_state.dart';
import 'package:dovahlink_client_sdk/src/shared/enums.dart';

/// Runs session-state behavior tests.
void main() {
  late SessionState state;

  setUp(() {
    state = SessionState();
  });

  group('Method constructor behaves correctly', () {
    test(
      'Method constructor starts disconnected with no identity or generation advanced',
      () {
        expect(state.connectionState, DovahLinkConnectionState.disconnected);
        expect(state.sessionId, isNull);
        expect(state.trustState, isNull);
        expect(state.invalidationReason, isNull);
        expect(state.connectionGeneration, 0);
        expect(state.lastConnectedUri, isNull);
        expect(state.isAdministrativelyInvalidated, isFalse);
      },
    );
  });

  group('Method beginConnectAttempt behaves correctly', () {
    test(
      'Method beginConnectAttempt transitions to connecting when not already reconnecting',
      () {
        final Uri uri = Uri.parse('ws://127.0.0.1:58231/');
        state.beginConnectAttempt(uri);

        expect(state.connectionState, DovahLinkConnectionState.connecting);
        expect(state.lastConnectedUri, uri);
        expect(state.connectionGeneration, 1);
      },
    );

    test(
      'Method beginConnectAttempt stays reconnecting when already reconnecting',
      () {
        state.markReconnecting();
        final Uri uri = Uri.parse('ws://127.0.0.1:58231/');
        state.beginConnectAttempt(uri);

        expect(state.connectionState, DovahLinkConnectionState.reconnecting);
        expect(state.lastConnectedUri, uri);
        expect(state.connectionGeneration, 1);
      },
    );

    test('Method beginConnectAttempt clears a prior invalidationReason', () {
      state.invalidate(AdministrativeInvalidationReason.revoked);
      state.beginConnectAttempt(Uri.parse('ws://127.0.0.1:58231/'));

      expect(state.invalidationReason, isNull);
    });

    test(
      'Method beginConnectAttempt advances the generation on every call',
      () {
        final Uri uri = Uri.parse('ws://127.0.0.1:58231/');
        state.beginConnectAttempt(uri);
        state.beginConnectAttempt(uri);

        expect(state.connectionGeneration, 2);
      },
    );
  });

  group('Method markConnected behaves correctly', () {
    test('Method markConnected transitions from connecting to connected', () {
      state.beginConnectAttempt(Uri.parse('ws://127.0.0.1:58231/'));
      state.markConnected();

      expect(state.connectionState, DovahLinkConnectionState.connected);
    });

    test('Method markConnected transitions from reconnecting to connected', () {
      state.markReconnecting();
      state.markConnected();

      expect(state.connectionState, DovahLinkConnectionState.connected);
    });
  });

  group('Method markConnectFailed behaves correctly', () {
    test(
      'Method markConnectFailed transitions to disconnected when not reconnecting',
      () {
        state.beginConnectAttempt(Uri.parse('ws://127.0.0.1:58231/'));
        state.markConnectFailed();

        expect(state.connectionState, DovahLinkConnectionState.disconnected);
      },
    );

    test(
      'Method markConnectFailed transitions to disconnected from the initial state',
      () {
        state.markConnectFailed();

        expect(state.connectionState, DovahLinkConnectionState.disconnected);
      },
    );

    test(
      'Method markConnectFailed leaves reconnecting untouched when already reconnecting',
      () {
        state.markReconnecting();
        state.markConnectFailed();

        expect(state.connectionState, DovahLinkConnectionState.reconnecting);
      },
    );
  });

  group('Method markReconnecting behaves correctly', () {
    test(
      'Method markReconnecting transitions to reconnecting from any prior state',
      () {
        state.beginConnectAttempt(Uri.parse('ws://127.0.0.1:58231/'));
        state.markConnected();
        state.markReconnecting();

        expect(state.connectionState, DovahLinkConnectionState.reconnecting);
      },
    );
  });

  group('Method admit behaves correctly', () {
    test('Method admit records the session id and trust state', () {
      state.admit(
        sessionId: 'session-1',
        trustState: DovahLinkTrustState.unpaired,
      );

      expect(state.sessionId, 'session-1');
      expect(state.trustState, DovahLinkTrustState.unpaired);
    });

    test(
      'Method admit overwrites a previously admitted session id and trust state',
      () {
        state.admit(
          sessionId: 'session-1',
          trustState: DovahLinkTrustState.unpaired,
        );
        state.admit(
          sessionId: 'session-2',
          trustState: DovahLinkTrustState.trusted,
        );

        expect(state.sessionId, 'session-2');
        expect(state.trustState, DovahLinkTrustState.trusted);
      },
    );
  });

  group('Method markTrusted behaves correctly', () {
    test('Method markTrusted upgrades trust standing to trusted', () {
      state.admit(
        sessionId: 'session-1',
        trustState: DovahLinkTrustState.unpaired,
      );
      state.markTrusted();

      expect(state.trustState, DovahLinkTrustState.trusted);
    });

    test(
      'Method markTrusted sets trust standing to trusted even with no prior admit',
      () {
        state.markTrusted();

        expect(state.trustState, DovahLinkTrustState.trusted);
      },
    );
  });

  group('Method invalidate behaves correctly', () {
    test(
      'Method invalidate sets the reason, transitions state, clears identity, and advances '
      'generation',
      () {
        state.admit(
          sessionId: 'session-1',
          trustState: DovahLinkTrustState.trusted,
        );
        final int generationBefore = state.connectionGeneration;

        state.invalidate(AdministrativeInvalidationReason.blocked);

        expect(
          state.invalidationReason,
          AdministrativeInvalidationReason.blocked,
        );
        expect(
          state.connectionState,
          DovahLinkConnectionState.administrativelyInvalidated,
        );
        expect(state.sessionId, isNull);
        expect(state.trustState, isNull);
        expect(state.connectionGeneration, generationBefore + 1);
      },
    );

    test('Method invalidate transitions from reconnecting', () {
      state.markReconnecting();

      state.invalidate(AdministrativeInvalidationReason.revoked);

      expect(
        state.connectionState,
        DovahLinkConnectionState.administrativelyInvalidated,
      );
    });

    test('Method invalidate is safe to call with no session ever admitted', () {
      state.invalidate(AdministrativeInvalidationReason.factoryReset);

      expect(state.sessionId, isNull);
      expect(state.trustState, isNull);
    });

    test('Method invalidate leaves lastConnectedUri unaffected', () {
      final Uri uri = Uri.parse('ws://127.0.0.1:58231/');
      state.beginConnectAttempt(uri);

      state.invalidate(AdministrativeInvalidationReason.trustReset);

      expect(state.lastConnectedUri, uri);
    });
  });

  group('Method bumpGeneration behaves correctly', () {
    test(
      'Method bumpGeneration advances the generation by one on each call',
      () {
        state.bumpGeneration();
        state.bumpGeneration();

        expect(state.connectionGeneration, 2);
      },
    );
  });

  group('Method resetAfterTeardown behaves correctly', () {
    test(
      'Method resetAfterTeardown stays reconnecting when preserveReconnecting is true and '
      'already reconnecting',
      () {
        state.markReconnecting();
        state.admit(
          sessionId: 'session-1',
          trustState: DovahLinkTrustState.trusted,
        );

        state.resetAfterTeardown(preserveReconnecting: true);

        expect(state.connectionState, DovahLinkConnectionState.reconnecting);
        expect(state.sessionId, isNull);
        expect(state.trustState, isNull);
      },
    );

    test(
      'Method resetAfterTeardown resolves to disconnected when preserveReconnecting is true but '
      'not currently reconnecting',
      () {
        state.beginConnectAttempt(Uri.parse('ws://127.0.0.1:58231/'));
        state.markConnected();

        state.resetAfterTeardown(preserveReconnecting: true);

        expect(state.connectionState, DovahLinkConnectionState.disconnected);
      },
    );

    test(
      'Method resetAfterTeardown resolves to disconnected when preserveReconnecting is false '
      'even while reconnecting',
      () {
        state.markReconnecting();

        state.resetAfterTeardown(preserveReconnecting: false);

        expect(state.connectionState, DovahLinkConnectionState.disconnected);
      },
    );

    test(
      'Method resetAfterTeardown resolves to disconnected when preserveReconnecting is false '
      'from a connected session',
      () {
        state.beginConnectAttempt(Uri.parse('ws://127.0.0.1:58231/'));
        state.markConnected();

        state.resetAfterTeardown(preserveReconnecting: false);

        expect(state.connectionState, DovahLinkConnectionState.disconnected);
      },
    );

    test(
      'Method resetAfterTeardown leaves the message subscription untouched',
      () {
        final StreamSubscription<String> subscription =
            const Stream<String>.empty().listen((_) {});
        addTearDown(subscription.cancel);
        state.attachMessageSubscription(subscription);

        state.resetAfterTeardown(preserveReconnecting: false);

        expect(state.detachMessageSubscription(), same(subscription));
      },
    );
  });

  group('Property isAdministrativelyInvalidated behaves correctly', () {
    test(
      'Property isAdministrativelyInvalidated is false before invalidation',
      () {
        expect(state.isAdministrativelyInvalidated, isFalse);
      },
    );

    test('Property isAdministrativelyInvalidated is true after invalidate', () {
      state.invalidate(AdministrativeInvalidationReason.trustReset);

      expect(state.isAdministrativelyInvalidated, isTrue);
    });

    test(
      'Property isAdministrativelyInvalidated returns to false once a new connect attempt begins',
      () {
        state.invalidate(AdministrativeInvalidationReason.trustReset);

        state.beginConnectAttempt(Uri.parse('ws://127.0.0.1:58231/'));

        expect(state.isAdministrativelyInvalidated, isFalse);
      },
    );
  });

  group('Method attachMessageSubscription behaves correctly', () {
    test(
      'Method attachMessageSubscription records the subscription for later detachment',
      () {
        final StreamSubscription<String> subscription =
            const Stream<String>.empty().listen((_) {});
        addTearDown(subscription.cancel);

        state.attachMessageSubscription(subscription);

        expect(state.detachMessageSubscription(), same(subscription));
      },
    );

    test(
      'Method attachMessageSubscription overwrites a previously attached subscription without '
      'cancelling it',
      () {
        final StreamSubscription<String> first = const Stream<String>.empty()
            .listen((_) {});
        final StreamSubscription<String> second = const Stream<String>.empty()
            .listen((_) {});
        addTearDown(first.cancel);
        addTearDown(second.cancel);

        state.attachMessageSubscription(first);
        state.attachMessageSubscription(second);

        expect(state.detachMessageSubscription(), same(second));
      },
    );
  });

  group('Method detachMessageSubscription behaves correctly', () {
    test(
      'Method detachMessageSubscription returns null when none is attached',
      () {
        expect(state.detachMessageSubscription(), isNull);
      },
    );

    test(
      'Method detachMessageSubscription clears the subscription so it is returned only once',
      () {
        final StreamSubscription<String> subscription =
            const Stream<String>.empty().listen((_) {});
        addTearDown(subscription.cancel);
        state.attachMessageSubscription(subscription);

        state.detachMessageSubscription();

        expect(state.detachMessageSubscription(), isNull);
      },
    );
  });

  group('Property connectionStateChanges behaves correctly', () {
    test(
      'Property connectionStateChanges replays the current connectionState immediately to a '
      'new subscriber',
      () async {
        await expectLater(
          state.connectionStateChanges,
          emits(DovahLinkConnectionState.disconnected),
        );
      },
    );

    test(
      'Property connectionStateChanges emits in order across connect, admit, and invalidate',
      () async {
        final Future<void> expectation = expectLater(
          state.connectionStateChanges,
          emitsInOrder(<Object>[
            DovahLinkConnectionState.disconnected,
            DovahLinkConnectionState.connecting,
            DovahLinkConnectionState.connected,
            DovahLinkConnectionState.administrativelyInvalidated,
          ]),
        );

        state.beginConnectAttempt(Uri.parse('ws://127.0.0.1:58231/'));
        state.markConnected();
        state.invalidate(AdministrativeInvalidationReason.revoked);

        await expectation;
      },
    );

    test(
      'Property connectionStateChanges does not emit when markConnectFailed leaves '
      'connectionState unchanged',
      () async {
        state.markReconnecting();

        // If the unchanged-value no-op were broken, the second element here would be a
        // duplicate reconnecting instead of the real transition to connected.
        final Future<void> expectation = expectLater(
          state.connectionStateChanges,
          emitsInOrder(<Object>[
            DovahLinkConnectionState.reconnecting,
            DovahLinkConnectionState.connected,
          ]),
        );

        state.markConnectFailed();
        state.markConnected();

        await expectation;
      },
    );

    test(
      'Property connectionStateChanges does not emit when beginConnectAttempt leaves '
      'connectionState at reconnecting',
      () async {
        state.markReconnecting();

        final Future<void> expectation = expectLater(
          state.connectionStateChanges,
          emitsInOrder(<Object>[
            DovahLinkConnectionState.reconnecting,
            DovahLinkConnectionState.connected,
          ]),
        );

        state.beginConnectAttempt(Uri.parse('ws://127.0.0.1:58231/'));
        state.markConnected();

        await expectation;
      },
    );

    test(
      'Property connectionStateChanges does not emit when markReconnecting is called while '
      'already reconnecting',
      () async {
        state.markReconnecting();

        final Future<void> expectation = expectLater(
          state.connectionStateChanges,
          emitsInOrder(<Object>[
            DovahLinkConnectionState.reconnecting,
            DovahLinkConnectionState.connected,
          ]),
        );

        state.markReconnecting();
        state.markConnected();

        await expectation;
      },
    );

    test(
      'Property connectionStateChanges emits disconnected when resetAfterTeardown resolves out '
      'of a connected session',
      () async {
        state.beginConnectAttempt(Uri.parse('ws://127.0.0.1:58231/'));
        state.markConnected();

        final Future<void> expectation = expectLater(
          state.connectionStateChanges,
          emitsInOrder(<Object>[
            DovahLinkConnectionState.connected,
            DovahLinkConnectionState.disconnected,
          ]),
        );

        state.resetAfterTeardown(preserveReconnecting: false);

        await expectation;
      },
    );

    test(
      'Property connectionStateChanges does not emit when resetAfterTeardown stays reconnecting',
      () async {
        state.markReconnecting();

        final Future<void> expectation = expectLater(
          state.connectionStateChanges,
          emitsInOrder(<Object>[
            DovahLinkConnectionState.reconnecting,
            DovahLinkConnectionState.connected,
          ]),
        );

        state.resetAfterTeardown(preserveReconnecting: true);
        state.markConnected();

        await expectation;
      },
    );
  });
}
