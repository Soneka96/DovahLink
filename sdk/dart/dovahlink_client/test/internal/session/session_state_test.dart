import 'dart:async';

import 'package:test/test.dart';

import 'package:dovahlink_client_sdk/src/dovahlink_host.dart';
import 'package:dovahlink_client_sdk/src/dovahlink_host_id.dart';
import 'package:dovahlink_client_sdk/src/dovahlink_known_host_invalidation.dart';
import 'package:dovahlink_client_sdk/src/internal/session/session_state.dart';
import 'package:dovahlink_client_sdk/src/shared/enums.dart';

/// Builds the Host context admitted with a session.
DovahLinkHost _currentHost({String hostName = 'LOCAL-HOST'}) => DovahLinkHost(
  hostId: '81869993-955c-4ba3-a7d0-d35ca86078ea',
  hostName: hostName,
  endpoint: Uri.parse('ws://127.0.0.1:58231/'),
);

/// Moves [state] into `reconnecting` the only way production does: an ordinary teardown that begins
/// recovery. Leaves the connection generation untouched.
void _enterRecovery(SessionState state) {
  state.resetAfterTeardown(preserveReconnecting: true, beginRecovery: true);
}

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
        expect(state.currentHost, isNull);
        expect(state.currentEndpoint, isNull);
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
        expect(state.currentEndpoint, isNull);
      },
    );

    test(
      'Method beginConnectAttempt stays reconnecting when already reconnecting',
      () {
        _enterRecovery(state);
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
      'Method beginConnectAttempt clears the prior Host session context',
      () {
        state.admit(
          sessionId: 'session-1',
          trustState: DovahLinkTrustState.trusted,
          currentHost: _currentHost(),
        );

        state.beginConnectAttempt(Uri.parse('ws://127.0.0.1:58231/'));

        expect(state.currentHost, isNull);
        expect(state.currentEndpoint, isNull);
      },
    );

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
      final Uri endpoint = Uri.parse('ws://127.0.0.1:58231/');
      state.beginConnectAttempt(endpoint);
      state.markConnected();

      expect(state.connectionState, DovahLinkConnectionState.connected);
      expect(state.currentEndpoint, endpoint);
    });

    test(
      'Method markConnected transitions from reconnecting to reauthenticating, not connected',
      () {
        _enterRecovery(state);
        state.markConnected();

        expect(
          state.connectionState,
          DovahLinkConnectionState.reauthenticating,
        );
      },
    );

    test(
      'Method markConnected transitions from reauthenticating to connected when called again',
      () {
        _enterRecovery(state);
        state.markConnected();
        state.markConnected();

        expect(state.connectionState, DovahLinkConnectionState.connected);
      },
    );
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
        _enterRecovery(state);
        state.markConnectFailed();

        expect(state.connectionState, DovahLinkConnectionState.reconnecting);
      },
    );
  });

  group('Method admit behaves correctly', () {
    test('Method admit records the session id and trust state', () {
      state.admit(
        sessionId: 'session-1',
        trustState: DovahLinkTrustState.unpaired,
        currentHost: _currentHost(),
      );

      expect(state.sessionId, 'session-1');
      expect(state.trustState, DovahLinkTrustState.unpaired);
      expect(state.currentHost, _currentHost());
      expect(state.knownHostId, isNull);
    });

    test(
      'Method admit overwrites a previously admitted session id and trust state',
      () {
        state.admit(
          sessionId: 'session-1',
          trustState: DovahLinkTrustState.unpaired,
          currentHost: _currentHost(),
        );
        state.admit(
          sessionId: 'session-2',
          trustState: DovahLinkTrustState.trusted,
          currentHost: _currentHost(hostName: 'OTHER-HOST'),
        );

        expect(state.sessionId, 'session-2');
        expect(state.trustState, DovahLinkTrustState.trusted);
        expect(state.currentHost, _currentHost(hostName: 'OTHER-HOST'));
        expect(state.knownHostId, isNull);
      },
    );

    test(
      'Method admit promotes a reauthenticating recovery attempt to connected',
      () {
        _enterRecovery(state);
        state.markConnected();

        state.admit(
          sessionId: 'session-1',
          trustState: DovahLinkTrustState.trusted,
          currentHost: _currentHost(),
        );

        expect(state.connectionState, DovahLinkConnectionState.connected);
      },
    );

    test('Method admit leaves an ordinary connected session at connected', () {
      state.beginConnectAttempt(Uri.parse('ws://127.0.0.1:58231/'));
      state.markConnected();

      state.admit(
        sessionId: 'session-1',
        trustState: DovahLinkTrustState.trusted,
        currentHost: _currentHost(),
      );

      expect(state.connectionState, DovahLinkConnectionState.connected);
    });
  });

  group('Property knownHostId behaves correctly', () {
    test(
      'Property knownHostId retains the selected relation through recovery teardown',
      () {
        final DovahLinkHostId hostId = DovahLinkHostId(_currentHost().hostId);
        state.beginConnectAttempt(_currentHost().endpoint);
        state.markConnected();
        state.admit(
          sessionId: 'session-1',
          trustState: DovahLinkTrustState.trusted,
          currentHost: _currentHost(),
        );
        state.associateKnownHost(hostId);

        _enterRecovery(state);
        state.beginConnectAttempt(_currentHost().endpoint);
        state.markConnected();
        state.resetAfterTeardown(preserveReconnecting: true);

        expect(state.knownHostId, hostId);
        expect(state.currentHost, isNull);
        expect(state.connectionState, DovahLinkConnectionState.reconnecting);
      },
    );

    test(
      'Property knownHostId is cleared after deliberate teardown and invalidation',
      () {
        final DovahLinkHostId hostId = DovahLinkHostId(_currentHost().hostId);
        state.admit(
          sessionId: 'session-1',
          trustState: DovahLinkTrustState.trusted,
          currentHost: _currentHost(),
        );
        state.associateKnownHost(hostId);

        state.resetAfterTeardown(preserveReconnecting: false);
        expect(state.knownHostId, isNull);

        state.admit(
          sessionId: 'session-2',
          trustState: DovahLinkTrustState.trusted,
          currentHost: _currentHost(),
        );
        state.associateKnownHost(hostId);
        state.invalidate(AdministrativeInvalidationReason.revoked);
        expect(state.knownHostId, isNull);
      },
    );

    test(
      'Property knownHostId does not carry into a later explicit connect attempt',
      () {
        final DovahLinkHostId hostId = DovahLinkHostId(_currentHost().hostId);
        state.beginConnectAttempt(_currentHost().endpoint);
        state.markConnected();
        state.admit(
          sessionId: 'session-1',
          trustState: DovahLinkTrustState.trusted,
          currentHost: _currentHost(),
        );
        state.associateKnownHost(hostId);
        state.resetAfterTeardown(preserveReconnecting: false);

        state.beginConnectAttempt(_currentHost().endpoint);

        expect(state.knownHostId, isNull);
      },
    );
  });

  group('Method associateKnownHost behaves correctly', () {
    test('Method associateKnownHost adds the relationship after pairing', () {
      final DovahLinkHostId hostId = DovahLinkHostId(_currentHost().hostId);
      state.admit(
        sessionId: 'session-1',
        trustState: DovahLinkTrustState.unpaired,
        currentHost: _currentHost(),
      );

      state.associateKnownHost(hostId);

      expect(state.knownHostId, hostId);
    });
  });

  group('Method markTrusted behaves correctly', () {
    test('Method markTrusted upgrades trust standing to trusted', () {
      state.admit(
        sessionId: 'session-1',
        trustState: DovahLinkTrustState.unpaired,
        currentHost: _currentHost(),
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
          currentHost: _currentHost(),
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
        expect(state.currentHost, isNull);
        expect(state.connectionGeneration, generationBefore + 1);
      },
    );

    test('Method invalidate transitions from reconnecting', () {
      _enterRecovery(state);

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
      'Method resetAfterTeardown resolves to disconnected after a previously admitted session '
      'even when preserveReconnecting is true, clearing its identity',
      () {
        _enterRecovery(state);
        state.admit(
          sessionId: 'session-1',
          trustState: DovahLinkTrustState.trusted,
          currentHost: _currentHost(),
        );

        state.resetAfterTeardown(preserveReconnecting: true);

        expect(state.connectionState, DovahLinkConnectionState.disconnected);
        expect(state.sessionId, isNull);
        expect(state.trustState, isNull);
        expect(state.currentHost, isNull);
      },
    );

    test(
      'Method resetAfterTeardown stays reconnecting for an in-flight re-authentication attempt '
      'that has not yet been admitted',
      () {
        _enterRecovery(state);
        state.beginConnectAttempt(Uri.parse('ws://127.0.0.1:58231/'));
        state.markConnected();

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
        _enterRecovery(state);

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
      'Method resetAfterTeardown resolves to disconnected from reauthenticating when '
      'preserveReconnecting is false',
      () {
        _enterRecovery(state);
        state.markConnected();

        state.resetAfterTeardown(preserveReconnecting: false);

        expect(state.connectionState, DovahLinkConnectionState.disconnected);
      },
    );

    test(
      'Method resetAfterTeardown with beginRecovery resolves a connected session directly to '
      'reconnecting, never publishing disconnected, and reports it entered recovery',
      () async {
        final DovahLinkHostId hostId = DovahLinkHostId(_currentHost().hostId);
        state.beginConnectAttempt(_currentHost().endpoint);
        state.markConnected();
        state.admit(
          sessionId: 'session-1',
          trustState: DovahLinkTrustState.trusted,
          currentHost: _currentHost(),
        );
        state.associateKnownHost(hostId);
        final List<DovahLinkConnectionState> states =
            <DovahLinkConnectionState>[];
        final List<KnownHostSessionSnapshot> snapshots =
            <KnownHostSessionSnapshot>[];
        final StreamSubscription<DovahLinkConnectionState> stateSubscription =
            state.connectionStateChanges.listen(states.add);
        final StreamSubscription<KnownHostSessionSnapshot> hostSubscription =
            state.knownHostSessionChanges.listen(snapshots.add);
        addTearDown(stateSubscription.cancel);
        addTearDown(hostSubscription.cancel);
        await Future<void>.delayed(Duration.zero);
        states.clear();
        snapshots.clear();

        final bool entered = state.resetAfterTeardown(
          preserveReconnecting: true,
          beginRecovery: true,
        );
        await Future<void>.delayed(Duration.zero);

        expect(entered, isTrue);
        expect(states, <DovahLinkConnectionState>[
          DovahLinkConnectionState.reconnecting,
        ]);
        expect(snapshots, <KnownHostSessionSnapshot>[
          (hostId: hostId, state: DovahLinkKnownHostSessionState.reconnecting),
        ]);
        expect(state.knownHostId, hostId);
        expect(state.sessionId, isNull);
        expect(state.trustState, isNull);
        expect(state.currentHost, isNull);
      },
    );

    test(
      'Method resetAfterTeardown with beginRecovery reports false and stays reconnecting for a '
      'session that was already recovering',
      () {
        _enterRecovery(state);

        final bool entered = state.resetAfterTeardown(
          preserveReconnecting: true,
          beginRecovery: true,
        );

        expect(entered, isFalse);
        expect(state.connectionState, DovahLinkConnectionState.reconnecting);
      },
    );

    test(
      'Method resetAfterTeardown ignores beginRecovery when preserveReconnecting is false',
      () {
        state.beginConnectAttempt(_currentHost().endpoint);
        state.markConnected();

        final bool entered = state.resetAfterTeardown(
          preserveReconnecting: false,
          beginRecovery: true,
        );

        expect(entered, isFalse);
        expect(state.connectionState, DovahLinkConnectionState.disconnected);
      },
    );

    test(
      'Method resetAfterTeardown without beginRecovery still resolves a connected session to '
      'disconnected and reports false',
      () {
        state.beginConnectAttempt(_currentHost().endpoint);
        state.markConnected();

        final bool entered = state.resetAfterTeardown(
          preserveReconnecting: true,
        );

        expect(entered, isFalse);
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

    test('Property connectionStateChanges does not emit when markConnectFailed leaves '
        'connectionState unchanged', () async {
      _enterRecovery(state);

      // If the unchanged-value no-op were broken, the second element here would be a
      // duplicate reconnecting instead of the real transition to reauthenticating.
      final Future<void> expectation = expectLater(
        state.connectionStateChanges,
        emitsInOrder(<Object>[
          DovahLinkConnectionState.reconnecting,
          DovahLinkConnectionState.reauthenticating,
        ]),
      );

      state.markConnectFailed();
      state.markConnected();

      await expectation;
    });

    test(
      'Property connectionStateChanges does not emit when beginConnectAttempt leaves '
      'connectionState at reconnecting',
      () async {
        _enterRecovery(state);

        final Future<void> expectation = expectLater(
          state.connectionStateChanges,
          emitsInOrder(<Object>[
            DovahLinkConnectionState.reconnecting,
            DovahLinkConnectionState.reauthenticating,
          ]),
        );

        state.beginConnectAttempt(Uri.parse('ws://127.0.0.1:58231/'));
        state.markConnected();

        await expectation;
      },
    );

    test(
      'Property connectionStateChanges does not emit when recovery is entered while '
      'already reconnecting',
      () async {
        _enterRecovery(state);

        final Future<void> expectation = expectLater(
          state.connectionStateChanges,
          emitsInOrder(<Object>[
            DovahLinkConnectionState.reconnecting,
            DovahLinkConnectionState.reauthenticating,
          ]),
        );

        _enterRecovery(state);
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
        _enterRecovery(state);

        final Future<void> expectation = expectLater(
          state.connectionStateChanges,
          emitsInOrder(<Object>[
            DovahLinkConnectionState.reconnecting,
            DovahLinkConnectionState.reauthenticating,
          ]),
        );

        state.resetAfterTeardown(preserveReconnecting: true);
        state.markConnected();

        await expectation;
      },
    );

    test(
      'Property connectionStateChanges emits connected when admit resolves a reauthenticating '
      'recovery attempt',
      () async {
        _enterRecovery(state);
        state.markConnected();

        final Future<void> expectation = expectLater(
          state.connectionStateChanges,
          emitsInOrder(<Object>[
            DovahLinkConnectionState.reauthenticating,
            DovahLinkConnectionState.connected,
          ]),
        );

        state.admit(
          sessionId: 'session-1',
          trustState: DovahLinkTrustState.trusted,
          currentHost: _currentHost(),
        );

        await expectation;
      },
    );

    test(
      'Property connectionStateChanges does not emit when admit is called on an already '
      'connected session',
      () async {
        state.beginConnectAttempt(Uri.parse('ws://127.0.0.1:58231/'));
        state.markConnected();

        final Future<void> expectation = expectLater(
          state.connectionStateChanges,
          emitsInOrder(<Object>[
            DovahLinkConnectionState.connected,
            DovahLinkConnectionState.administrativelyInvalidated,
          ]),
        );

        state.admit(
          sessionId: 'session-1',
          trustState: DovahLinkTrustState.trusted,
          currentHost: _currentHost(),
        );
        state.invalidate(AdministrativeInvalidationReason.revoked);

        await expectation;
      },
    );
  });

  group('Property knownHostSessionChanges behaves correctly', () {
    test(
      'Property knownHostSessionChanges stays connecting until admission and follows recovery',
      () async {
        final DovahLinkHostId hostId = DovahLinkHostId(_currentHost().hostId);
        final Future<void> expectation = expectLater(
          state.knownHostSessionChanges,
          emitsInOrder(<Object>[
            (hostId: null, state: DovahLinkKnownHostSessionState.disconnected),
            (hostId: hostId, state: DovahLinkKnownHostSessionState.connecting),
            (hostId: hostId, state: DovahLinkKnownHostSessionState.connected),
            (
              hostId: hostId,
              state: DovahLinkKnownHostSessionState.reconnecting,
            ),
            (
              hostId: hostId,
              state: DovahLinkKnownHostSessionState.reauthenticating,
            ),
            (
              hostId: hostId,
              state: DovahLinkKnownHostSessionState.reconnecting,
            ),
            (
              hostId: hostId,
              state: DovahLinkKnownHostSessionState.reauthenticating,
            ),
            (hostId: hostId, state: DovahLinkKnownHostSessionState.connected),
            (
              hostId: hostId,
              state: DovahLinkKnownHostSessionState.disconnected,
            ),
          ]),
        );

        state.beginConnectAttempt(
          Uri.parse('ws://127.0.0.1:58231/'),
          knownHostId: hostId,
        );
        state.markConnected();
        expect(
          state.knownHostSessionState,
          DovahLinkKnownHostSessionState.connecting,
        );
        state.admit(
          sessionId: 'session-1',
          trustState: DovahLinkTrustState.trusted,
          currentHost: _currentHost(),
        );
        _enterRecovery(state);
        state.beginConnectAttempt(Uri.parse('ws://127.0.0.1:58231/'));
        state.markConnected();
        state.resetAfterTeardown(preserveReconnecting: true);
        _enterRecovery(state);
        state.markConnected();
        state.admit(
          sessionId: 'session-2',
          trustState: DovahLinkTrustState.trusted,
          currentHost: _currentHost(),
        );
        state.resetAfterTeardown(preserveReconnecting: false);

        await expectation;
      },
    );

    test(
      'Property knownHostSessionChanges leaves candidate claims unassociated',
      () async {
        final Future<void> expectation = expectLater(
          state.knownHostSessionChanges,
          emits((
            hostId: null,
            state: DovahLinkKnownHostSessionState.disconnected,
          )),
        );
        state.beginConnectAttempt(Uri.parse('ws://127.0.0.1:58231/'));
        state.markConnected();
        state.admit(
          sessionId: 'session-candidate',
          trustState: DovahLinkTrustState.unpaired,
          currentHost: _currentHost(),
        );

        expect(state.knownHostId, isNull);
        expect(
          state.knownHostSessionState,
          DovahLinkKnownHostSessionState.disconnected,
        );
        await expectation;
      },
    );
  });

  group('Property knownHostInvalidations behaves correctly', () {
    test(
      'Property knownHostInvalidations preserves each Host and reason',
      () async {
        final DovahLinkHostId firstHostId = DovahLinkHostId(
          '81869993-955c-4ba3-a7d0-d35ca86078ea',
        );
        final DovahLinkHostId secondHostId = DovahLinkHostId(
          '91869993-955c-4ba3-a7d0-d35ca86078ea',
        );
        final Future<void> events = expectLater(
          state.knownHostInvalidations,
          emitsInOrder(<Object>[
            isA<DovahLinkKnownHostInvalidation>()
                .having((event) => event.hostId, 'hostId', firstHostId)
                .having(
                  (event) => event.reason,
                  'reason',
                  AdministrativeInvalidationReason.revoked,
                ),
            isA<DovahLinkKnownHostInvalidation>()
                .having((event) => event.hostId, 'hostId', secondHostId)
                .having(
                  (event) => event.reason,
                  'reason',
                  AdministrativeInvalidationReason.blocked,
                ),
          ]),
        );

        for (final (
              DovahLinkHostId hostId,
              AdministrativeInvalidationReason reason,
            )
            in <(DovahLinkHostId, AdministrativeInvalidationReason)>[
              (firstHostId, AdministrativeInvalidationReason.revoked),
              (secondHostId, AdministrativeInvalidationReason.blocked),
            ]) {
          state.beginConnectAttempt(
            Uri.parse('ws://127.0.0.1:58231/'),
            knownHostId: hostId,
          );
          state.markConnected();
          state.admit(
            sessionId: 'session-${hostId.value}',
            trustState: DovahLinkTrustState.trusted,
            currentHost: _currentHost(),
          );
          state.invalidate(reason);
        }

        await events;
      },
    );

    test('Property knownHostInvalidations omits candidate sessions', () async {
      final List<DovahLinkKnownHostInvalidation> events =
          <DovahLinkKnownHostInvalidation>[];
      final StreamSubscription<DovahLinkKnownHostInvalidation> subscription =
          state.knownHostInvalidations.listen(events.add);
      addTearDown(subscription.cancel);

      state.admit(
        sessionId: 'candidate-session',
        trustState: DovahLinkTrustState.unpaired,
        currentHost: _currentHost(),
      );
      state.invalidate(AdministrativeInvalidationReason.factoryReset);
      await Future<void>.delayed(Duration.zero);

      expect(events, isEmpty);
    });
  });
}
