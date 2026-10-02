import 'package:flutter_test/flutter_test.dart';
import 'package:mocktail/mocktail.dart';

import 'package:dovahlink_client/features/connection/presentation/state/connection.actions.dart';
import 'package:dovahlink_client/features/pairing/presentation/state/pairing.actions.dart';
import 'package:dovahlink_client/features/session/presentation/state/session_shell.actions.dart';
import 'package:dovahlink_client/features/session/presentation/state/session_shell.middleware.dart';
import 'package:dovahlink_client/injection_container.dart';
import 'package:dovahlink_client/shared/constants/enums.dart';
import 'package:dovahlink_client/shared/navigation/app_routes.dart';
import 'package:dovahlink_client/shared/navigation/navigator_service.dart';
import 'package:dovahlink_client/shared/state/create_store.dart';
import '../../../../fixtures/fixtures.dart';

/// Mocks the application's navigation boundary.
class MockNavigatorService extends Mock implements NavigatorService {}

/// Exercises trusted-session admission and Session Shell navigation.
void main() {
  late MockNavigatorService navigator;
  late SessionShellMiddleware middleware;

  setUp(() async {
    await sl.reset();
    navigator = MockNavigatorService();
    when(() => navigator.go(any())).thenAnswer((_) {});
    middleware = SessionShellMiddleware(navigator);
  });

  tearDown(() async {
    await sl.reset();
  });

  group('SessionShellMiddleware enters an admitted session', () {
    test(
      'SessionShellMiddleware waits for the selected Host to be connected',
      () {
        final store = const CreateStore()(middleware: [middleware.call]);
        final host = Fixtures.buildHost(hostId: 'selected-host');
        store.dispatch(
          ConnectionHostSelectedAction(
            host,
            source: ConnectionHostSelectionSource.knownHost,
          ),
        );
        store.dispatch(
          ConnectionKnownHostsChangedAction([
            Fixtures.buildKnownHost(
              host: host,
              availability: HostAvailability.online,
              sessionState: KnownHostSessionState.disconnected,
            ),
          ]),
        );
        store.dispatch(
          const PairingAuthenticatedAction(hostVersion: '1.0.0', trusted: true),
        );
        store.dispatch(const PairingSessionTrustedAction());

        verifyNever(() => navigator.go(any()));

        store.dispatch(
          ConnectionKnownHostsChangedAction([
            Fixtures.buildKnownHost(
              host: Fixtures.buildHost(hostId: 'other-host'),
              availability: HostAvailability.online,
              sessionState: KnownHostSessionState.connected,
            ),
            Fixtures.buildKnownHost(
              host: host,
              availability: HostAvailability.online,
              sessionState: KnownHostSessionState.connected,
            ),
          ]),
        );

        verify(
          () => navigator.go(AppRoutes.sessionFor('selected-host')),
        ).called(1);
      },
    );

    test(
      'SessionShellMiddleware waits for trusted intent when connected arrives first',
      () {
        final store = const CreateStore()(middleware: [middleware.call]);
        final host = Fixtures.buildHost(hostId: 'selected-host');
        store.dispatch(
          ConnectionHostSelectedAction(
            host,
            source: ConnectionHostSelectionSource.knownHost,
          ),
        );
        store.dispatch(
          ConnectionKnownHostsChangedAction([
            Fixtures.buildKnownHost(
              host: host,
              availability: HostAvailability.online,
              sessionState: KnownHostSessionState.connected,
            ),
          ]),
        );

        verifyNever(() => navigator.go(any()));

        store.dispatch(
          const PairingAuthenticatedAction(hostVersion: '1.0.0', trusted: true),
        );
        store.dispatch(const PairingSessionTrustedAction());

        verify(
          () => navigator.go(AppRoutes.sessionFor('selected-host')),
        ).called(1);
      },
    );

    test(
      'SessionShellMiddleware cancels pending entry after pairing fails',
      () {
        final store = const CreateStore()(middleware: [middleware.call]);
        final host = Fixtures.buildHost(hostId: 'selected-host');
        store.dispatch(ConnectionHostSelectedAction(host));
        store.dispatch(
          const PairingAuthenticatedAction(hostVersion: '1.0.0', trusted: true),
        );
        store.dispatch(const PairingSessionTrustedAction());
        store.dispatch(const PairingFailedAction('Host session ended.'));
        store.dispatch(
          ConnectionKnownHostsChangedAction([
            Fixtures.buildKnownHost(
              host: host,
              availability: HostAvailability.online,
              sessionState: KnownHostSessionState.connected,
            ),
          ]),
        );

        verifyNever(() => navigator.go(any()));
      },
    );

    for (final String cancellation in [
      'host changes',
      'a new pairing starts',
      'untrusted pairing closes',
    ]) {
      test(
        'SessionShellMiddleware clears a pending handoff when $cancellation',
        () {
          final store = const CreateStore()(middleware: [middleware.call]);
          final host = Fixtures.buildHost(hostId: 'selected-host');
          store.dispatch(ConnectionHostSelectedAction(host));
          store.dispatch(
            const PairingAuthenticatedAction(
              hostVersion: '1.0.0',
              trusted: true,
            ),
          );
          store.dispatch(const PairingSessionTrustedAction());
          switch (cancellation) {
            case 'host changes':
              store.dispatch(
                ConnectionHostSelectedAction(
                  Fixtures.buildHost(hostId: 'other-host'),
                ),
              );
            case 'a new pairing starts':
              store.dispatch(const PairingStartedAction());
            case 'untrusted pairing closes':
              store.dispatch(const PairingDisposedAction(wasTrusted: false));
          }
          store.dispatch(
            ConnectionKnownHostsChangedAction([
              Fixtures.buildKnownHost(
                host: host,
                availability: HostAvailability.online,
                sessionState: KnownHostSessionState.connected,
              ),
            ]),
          );

          verifyNever(() => navigator.go(any()));
        },
      );
    }

    test(
      'SessionShellMiddleware keeps a trusted handoff pending after pairing closes',
      () {
        final store = const CreateStore()(middleware: [middleware.call]);
        final host = Fixtures.buildHost(hostId: 'selected-host');
        store.dispatch(ConnectionHostSelectedAction(host));
        store.dispatch(
          const PairingAuthenticatedAction(hostVersion: '1.0.0', trusted: true),
        );
        store.dispatch(const PairingSessionTrustedAction());
        store.dispatch(const PairingDisposedAction(wasTrusted: true));
        store.dispatch(
          ConnectionKnownHostsChangedAction([
            Fixtures.buildKnownHost(
              host: host,
              availability: HostAvailability.online,
              sessionState: KnownHostSessionState.connected,
            ),
          ]),
        );

        verify(
          () => navigator.go(AppRoutes.sessionFor('selected-host')),
        ).called(1);
      },
    );
  });

  group(
    'SessionShellMiddleware processes ConnectionHostReentryRequestedAction correctly',
    () {
      test(
        'ConnectionHostReentryRequestedAction opens only the already-connected Host shell',
        () {
          final store = const CreateStore()(middleware: [middleware.call]);
          final host = Fixtures.buildHost(hostId: 'selected-host');
          store.dispatch(
            ConnectionKnownHostsChangedAction([
              Fixtures.buildKnownHost(
                host: host,
                availability: HostAvailability.online,
                sessionState: KnownHostSessionState.connected,
              ),
            ]),
          );

          store.dispatch(ConnectionHostReentryRequestedAction(host.hostId));

          verify(
            () => navigator.go(AppRoutes.sessionFor(host.hostId)),
          ).called(1);
          expect(store.state.connection.selectedHost, isNull);
          expect(
            store.state.connection.knownHosts.single.sessionState,
            KnownHostSessionState.connected,
          );
          expect(store.state.pairing.phase, PairingPhase.none);
        },
      );

      test(
        'ConnectionHostReentryRequestedAction does not open a Host without a connected session',
        () {
          final store = const CreateStore()(middleware: [middleware.call]);
          final host = Fixtures.buildHost(hostId: 'selected-host');
          store.dispatch(
            ConnectionKnownHostsChangedAction([
              Fixtures.buildKnownHost(
                host: host,
                availability: HostAvailability.online,
                sessionState: KnownHostSessionState.disconnected,
              ),
              Fixtures.buildKnownHost(
                host: Fixtures.buildHost(hostId: 'other-host'),
                availability: HostAvailability.online,
                sessionState: KnownHostSessionState.connected,
              ),
            ]),
          );

          store.dispatch(ConnectionHostReentryRequestedAction(host.hostId));

          verifyNever(() => navigator.go(any()));
          expect(store.state.connection.selectedHost, isNull);
        },
      );
    },
  );

  group(
    'SessionShellMiddleware processes SessionShellBackRequestedAction correctly',
    () {
      test(
        'SessionShellBackRequestedAction reaches the navigator after forwarding',
        () {
          final List<String> events = [];
          when(() => navigator.go(any())).thenAnswer((invocation) {
            events.add(invocation.positionalArguments.single as String);
          });
          final store = const CreateStore()();

          middleware.call(
            store,
            const SessionShellBackRequestedAction(),
            (_) => events.add('forwarded'),
          );

          expect(events, ['forwarded', AppRoutes.home]);
        },
      );
    },
  );

  group('SessionShellMiddleware returns to Connections', () {
    test(
      'Back returns to Connections while leaving the SDK session connected',
      () {
        final host = Fixtures.buildHost(hostId: 'selected-host');
        final store = const CreateStore()(middleware: [middleware.call]);
        store.dispatch(
          ConnectionKnownHostsChangedAction([
            Fixtures.buildKnownHost(
              host: host,
              availability: HostAvailability.online,
              sessionState: KnownHostSessionState.connected,
            ),
          ]),
        );

        store.dispatch(const SessionShellBackRequestedAction());

        verify(() => navigator.go(AppRoutes.home)).called(1);
        expect(
          store.state.connection.knownHosts.single.sessionState,
          KnownHostSessionState.connected,
        );
      },
    );

    test('Back clears a pending handoff before the Host becomes connected', () {
      final host = Fixtures.buildHost(hostId: 'selected-host');
      final store = const CreateStore()(middleware: [middleware.call]);
      store.dispatch(ConnectionHostSelectedAction(host));
      store.dispatch(
        const PairingAuthenticatedAction(hostVersion: '1.0.0', trusted: true),
      );
      store.dispatch(const PairingSessionTrustedAction());
      store.dispatch(const SessionShellBackRequestedAction());
      store.dispatch(
        ConnectionKnownHostsChangedAction([
          Fixtures.buildKnownHost(
            host: host,
            availability: HostAvailability.online,
            sessionState: KnownHostSessionState.connected,
          ),
        ]),
      );

      verify(() => navigator.go(AppRoutes.home)).called(1);
      verifyNever(() => navigator.go(AppRoutes.sessionFor('selected-host')));
    });
  });
}
