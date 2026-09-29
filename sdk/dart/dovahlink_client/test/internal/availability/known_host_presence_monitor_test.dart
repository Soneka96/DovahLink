import 'dart:async';

import 'package:mocktail/mocktail.dart';
import 'package:test/test.dart';

import 'package:dovahlink_client_sdk/src/dovahlink_compatibility_exception.dart';
import 'package:dovahlink_client_sdk/src/dovahlink_connection_exception.dart';
import 'package:dovahlink_client_sdk/src/dovahlink_host.dart';
import 'package:dovahlink_client_sdk/src/dovahlink_host_id.dart';
import 'package:dovahlink_client_sdk/src/dovahlink_known_host_state.dart';
import 'package:dovahlink_client_sdk/src/dovahlink_protocol_exception.dart';
import 'package:dovahlink_client_sdk/src/host_presence_probe.dart';
import 'package:dovahlink_client_sdk/src/internal/availability/host_availability_service.dart';
import 'package:dovahlink_client_sdk/src/internal/availability/known_host_presence_monitor.dart';
import 'package:dovahlink_client_sdk/src/internal/persistence/client_state_service.dart';
import 'package:dovahlink_client_sdk/src/internal/session/session_service.dart';
import 'package:dovahlink_client_sdk/src/persistence/persisted_client_state.dart';
import 'package:dovahlink_client_sdk/src/shared/enums.dart';
import '../../fixtures/fixtures.dart';

/// A pending request controlled by a Known Host presence test.
typedef PresenceProbeCall = ({
  Uri endpoint,
  Completer<DovahLinkHost> result,
  Future<void>? cancellation,
});

/// Provides replayable Known Host snapshots for monitor tests.
class FakePresenceClientStateService implements IClientStateService {
  /// The source of later complete Host collections.
  final StreamController<List<DovahLinkHost>> _changes =
      StreamController<List<DovahLinkHost>>.broadcast(sync: true);

  /// The latest complete collection replayed to new observers.
  List<DovahLinkHost>? _snapshot;

  /// Whether a monitor is currently observing the source stream.
  bool get hasListener => _changes.hasListener;

  /// Emits current and later committed Known Host snapshots.
  @override
  Stream<List<DovahLinkHost>> get knownHostsChanges =>
      Stream<List<DovahLinkHost>>.multi((
        MultiStreamController<List<DovahLinkHost>> sink,
      ) {
        final List<DovahLinkHost>? current = _snapshot;
        if (current != null) {
          sink.add(current);
        }
        final StreamSubscription<List<DovahLinkHost>> subscription = _changes
            .stream
            .listen(sink.add);
        sink.onCancel = subscription.cancel;
      }, isBroadcast: true);

  /// Loads persisted state, which is outside the monitor's test boundary.
  @override
  Future<PersistedClientState> load() => throw UnimplementedError();

  /// Writes persisted state, which is outside the monitor's test boundary.
  @override
  Future<void> updateState(
    PersistedClientState Function(PersistedClientState state) update,
  ) => throw UnimplementedError();

  /// Publishes [hosts] as the next committed collection.
  void emit(List<DovahLinkHost> hosts) {
    _snapshot = List<DovahLinkHost>.unmodifiable(hosts);
    _changes.add(_snapshot!);
  }

  /// Closes the test-owned source stream.
  Future<void> close() => _changes.close();
}

/// Records reachability updates without duplicating availability policy.
class FakePresenceAvailabilityService implements IHostAvailabilityService {
  /// Every Host ID and runtime evidence value set by the monitor.
  final List<(String, DovahLinkHostAvailability)> updates =
      <(String, DovahLinkHostAvailability)>[];

  /// Returns no projections; the monitor owns no projection behavior.
  @override
  Stream<List<DovahLinkKnownHostState>> get knownHostStatesChanges =>
      const Stream<List<DovahLinkKnownHostState>>.empty();

  /// Records one presence value for [hostId].
  @override
  void setAvailability(
    DovahLinkHostId hostId,
    DovahLinkHostAvailability availability,
  ) => updates.add((hostId.value, availability));

  /// Ignores session state because this fake isolates presence monitoring.
  @override
  void setSessionState(
    DovahLinkHostId? hostId,
    DovahLinkKnownHostSessionState sessionState,
  ) {}

  /// Completes the no-resource fake's terminal lifecycle.
  @override
  Future<void> close() async {}
}

/// Controls and records the monitor's asynchronous HTTP probe requests.
class ControllableHostPresenceProbe implements IHostPresenceProbe {
  /// Every request started, in call order.
  final List<PresenceProbeCall> calls = <PresenceProbeCall>[];

  /// Whether cancellation should immediately finish the corresponding request.
  bool completeWhenCancelled = true;

  /// The number of cancellation signals delivered by the monitor.
  int cancellationCount = 0;

  /// Starts one controlled probe for [endpoint].
  @override
  Future<DovahLinkHost> probe(Uri endpoint, {Future<void>? cancel}) {
    final Completer<DovahLinkHost> result = Completer<DovahLinkHost>();
    calls.add((endpoint: endpoint, result: result, cancellation: cancel));
    if (cancel != null) {
      unawaited(
        cancel.then((_) {
          cancellationCount++;
          if (completeWhenCancelled && !result.isCompleted) {
            result.completeError(
              const DovahLinkConnectionException('Probe cancelled.'),
            );
          }
        }),
      );
    }
    return result.future;
  }

  /// Completes request [index] with a Host claim for [hostId].
  void succeed(int index, {required String hostId}) {
    final PresenceProbeCall call = calls[index];
    call.result.complete(
      Fixtures.buildDovahLinkHost(
        hostId: hostId,
        endpoint: call.endpoint.toString(),
      ),
    );
  }

  /// Completes request [index] with [error].
  void fail(int index, Exception error) =>
      calls[index].result.completeError(error);
}

/// Mocks only the session facts the presence monitor reads.
class MockPresenceSessionService extends Mock implements ISessionService {}

/// Waits for [probe] to record at least [count] calls, bounded against a stuck test.
Future<void> waitForProbeCount(
  ControllableHostPresenceProbe probe,
  int count,
) async {
  final DateTime deadline = DateTime.now().add(const Duration(seconds: 5));
  while (probe.calls.length < count) {
    if (DateTime.now().isAfter(deadline)) {
      fail('Timed out waiting for $count Host presence probes.');
    }
    await Future<void>.delayed(Duration.zero);
  }
}

/// Waits for [availabilityService] to record at least [count] presence updates.
/// @param availabilityService The monitor's availability contract fake.
/// @param count The minimum number of transitions expected.
Future<void> waitForAvailabilityUpdate(
  FakePresenceAvailabilityService availabilityService,
  int count,
) async {
  for (
    int attempt = 0;
    attempt < 20 && availabilityService.updates.length < count;
    attempt++
  ) {
    await Future<void>.delayed(Duration.zero);
  }
  expect(availabilityService.updates.length, greaterThanOrEqualTo(count));
}

void main() {
  const String hostAId = '81869993-955c-4ba3-a7d0-d35ca86078ea';
  const String hostBId = '81f6cc90-3a88-40c7-8351-104d4a36c971';
  late FakePresenceClientStateService clientStateService;
  late ControllableHostPresenceProbe probe;
  late FakePresenceAvailabilityService availabilityService;
  late MockPresenceSessionService sessionService;
  late StreamController<void> refreshTicks;
  late KnownHostPresenceMonitor monitor;

  setUp(() {
    clientStateService = FakePresenceClientStateService();
    probe = ControllableHostPresenceProbe();
    availabilityService = FakePresenceAvailabilityService();
    sessionService = MockPresenceSessionService();
    refreshTicks = StreamController<void>.broadcast(sync: true);
    when(
      () => sessionService.connectionState,
    ).thenReturn(DovahLinkConnectionState.disconnected);
    when(() => sessionService.currentTrustState).thenReturn(null);
    when(() => sessionService.currentKnownHostId).thenReturn(null);
    monitor = KnownHostPresenceMonitor(
      clientStateService: clientStateService,
      probe: probe,
      availabilityService: availabilityService,
      sessionService: sessionService,
      refreshTicks: refreshTicks.stream,
    );
  });

  tearDown(() async {
    await monitor.close();
    await clientStateService.close();
    await refreshTicks.close();
  });

  group('Method start behaves correctly', () {
    test(
      'Method start immediately checks each restored Known Host and publishes online after a matching claim',
      () async {
        final DovahLinkHost host = Fixtures.buildDovahLinkHost(hostId: hostAId);
        clientStateService.emit(<DovahLinkHost>[host]);

        monitor.start();
        await waitForAvailabilityUpdate(availabilityService, 1);
        await waitForProbeCount(probe, 1);

        expect(probe.calls.single.endpoint, host.endpoint);
        expect(
          availabilityService.updates,
          contains((hostAId, DovahLinkHostAvailability.checking)),
        );
        probe.succeed(0, hostId: hostAId);
        await Future<void>.delayed(Duration.zero);

        expect(availabilityService.updates.last, (
          hostAId,
          DovahLinkHostAvailability.online,
        ));
      },
    );

    test(
      'Method start does not probe a Known Host with an admitted authenticated session',
      () async {
        final DovahLinkHost host = Fixtures.buildDovahLinkHost(hostId: hostAId);
        when(
          () => sessionService.connectionState,
        ).thenReturn(DovahLinkConnectionState.connected);
        when(
          () => sessionService.currentTrustState,
        ).thenReturn(DovahLinkTrustState.trusted);
        when(
          () => sessionService.currentKnownHostId,
        ).thenReturn(DovahLinkHostId(hostAId));
        clientStateService.emit(<DovahLinkHost>[host]);

        monitor.start();

        await waitForAvailabilityUpdate(availabilityService, 1);
        expect(probe.calls, isEmpty);
        expect(
          availabilityService.updates,
          contains((hostAId, DovahLinkHostAvailability.online)),
        );
      },
    );
  });

  group('Method refreshPresence behaves correctly', () {
    test(
      'Method refreshPresence skips an already-running probe for the same Host',
      () async {
        clientStateService.emit(<DovahLinkHost>[
          Fixtures.buildDovahLinkHost(hostId: hostAId),
        ]);
        monitor.start();
        await waitForProbeCount(probe, 1);

        monitor.refreshPresence();
        monitor.refreshPresence();

        expect(probe.calls, hasLength(1));
      },
    );

    test(
      'Method refreshPresence keeps global probe concurrency bounded across Hosts',
      () async {
        monitor = KnownHostPresenceMonitor(
          clientStateService: clientStateService,
          probe: probe,
          availabilityService: availabilityService,
          sessionService: sessionService,
          refreshTicks: refreshTicks.stream,
          maxConcurrentProbes: 2,
        );
        final List<DovahLinkHost> hosts = <DovahLinkHost>[
          Fixtures.buildDovahLinkHost(hostId: hostAId),
          Fixtures.buildDovahLinkHost(hostId: hostBId),
          Fixtures.buildDovahLinkHost(
            hostId: 'a257e41a-9ad3-4900-9a0c-4861eecf6ef6',
          ),
          Fixtures.buildDovahLinkHost(
            hostId: 'b257e41a-9ad3-4900-9a0c-4861eecf6ef6',
          ),
          Fixtures.buildDovahLinkHost(
            hostId: 'c257e41a-9ad3-4900-9a0c-4861eecf6ef6',
          ),
        ];
        clientStateService.emit(hosts);
        monitor.start();
        await waitForProbeCount(probe, 2);
        expect(probe.calls, hasLength(2));

        probe.succeed(0, hostId: hosts.first.hostId);
        await waitForProbeCount(probe, 3);
        expect(probe.calls, hasLength(3));

        for (int index = 1; index < hosts.length; index++) {
          await waitForProbeCount(probe, index + 1);
          probe.succeed(index, hostId: hosts[index].hostId);
          await Future<void>.delayed(Duration.zero);
        }
        expect(probe.calls.length, hosts.length);
      },
    );

    test(
      'Method refreshPresence publishes each injected periodic probe result',
      () async {
        clientStateService.emit(<DovahLinkHost>[
          Fixtures.buildDovahLinkHost(hostId: hostAId),
        ]);
        monitor.start();
        await waitForProbeCount(probe, 1);
        probe.succeed(0, hostId: hostAId);
        await Future<void>.delayed(Duration.zero);
        expect(availabilityService.updates.last, (
          hostAId,
          DovahLinkHostAvailability.online,
        ));

        refreshTicks.add(null);
        await waitForProbeCount(probe, 2);
        probe.fail(
          1,
          const DovahLinkConnectionException('Could not reach the Host.'),
        );
        await Future<void>.delayed(Duration.zero);
        expect(availabilityService.updates.last, (
          hostAId,
          DovahLinkHostAvailability.offline,
        ));
      },
    );
  });

  group('Method handleKnownHostsChanged behaves correctly', () {
    test(
      'Method handleKnownHostsChanged ignores a late result after its Host is removed',
      () async {
        probe.completeWhenCancelled = false;
        clientStateService.emit(<DovahLinkHost>[
          Fixtures.buildDovahLinkHost(hostId: hostAId),
        ]);
        monitor.start();
        await waitForProbeCount(probe, 1);
        clientStateService.emit(const <DovahLinkHost>[]);
        await Future<void>.delayed(Duration.zero);
        final int updatesAfterRemoval = availabilityService.updates.length;
        probe.succeed(0, hostId: hostAId);
        await Future<void>.delayed(Duration.zero);

        expect(probe.cancellationCount, 1);
        expect(availabilityService.updates.length, updatesAfterRemoval);
        expect(clientStateService.hasListener, isTrue);
      },
    );

    test(
      'Method handleKnownHostsChanged ignores an old endpoint result and probes the committed endpoint',
      () async {
        probe.completeWhenCancelled = false;
        final DovahLinkHost oldHost = Fixtures.buildDovahLinkHost(
          hostId: hostAId,
          endpoint: 'ws://127.0.0.1:58231/old',
        );
        final DovahLinkHost newHost = Fixtures.buildDovahLinkHost(
          hostId: hostAId,
          endpoint: 'ws://127.0.0.1:58232/new',
        );
        clientStateService.emit(<DovahLinkHost>[oldHost]);
        monitor.start();
        await waitForProbeCount(probe, 1);
        clientStateService.emit(<DovahLinkHost>[newHost]);
        await Future<void>.delayed(Duration.zero);

        expect(probe.cancellationCount, 1);
        probe.succeed(0, hostId: hostAId);
        await waitForProbeCount(probe, 2);
        expect(probe.calls[1].endpoint, newHost.endpoint);
        expect(availabilityService.updates.last, (
          hostAId,
          DovahLinkHostAvailability.checking,
        ));

        probe.succeed(1, hostId: hostAId);
        await Future<void>.delayed(Duration.zero);
        expect(availabilityService.updates.last, (
          hostAId,
          DovahLinkHostAvailability.online,
        ));
      },
    );

    test(
      'Method handleKnownHostsChanged treats a different Host ID claim as inconclusive',
      () async {
        clientStateService.emit(<DovahLinkHost>[
          Fixtures.buildDovahLinkHost(hostId: hostAId),
        ]);
        monitor.start();
        await waitForProbeCount(probe, 1);
        probe.succeed(0, hostId: hostBId);
        await Future<void>.delayed(Duration.zero);

        expect(availabilityService.updates.last, (
          hostAId,
          DovahLinkHostAvailability.unknown,
        ));
      },
    );

    test(
      'Method handleKnownHostsChanged preserves online evidence when a probe fails after session admission',
      () async {
        clientStateService.emit(<DovahLinkHost>[
          Fixtures.buildDovahLinkHost(hostId: hostAId),
        ]);
        monitor.start();
        await waitForProbeCount(probe, 1);
        when(
          () => sessionService.connectionState,
        ).thenReturn(DovahLinkConnectionState.connected);
        when(
          () => sessionService.currentTrustState,
        ).thenReturn(DovahLinkTrustState.trusted);
        when(
          () => sessionService.currentKnownHostId,
        ).thenReturn(DovahLinkHostId(hostAId));
        probe.fail(
          0,
          const DovahLinkConnectionException('Could not reach the Host.'),
        );
        await Future<void>.delayed(Duration.zero);

        expect(availabilityService.updates.last, (
          hostAId,
          DovahLinkHostAvailability.online,
        ));
        expect(
          availabilityService.updates,
          isNot(contains((hostAId, DovahLinkHostAvailability.offline))),
        );
      },
    );
  });

  group('Method probeKnownHost behaves correctly', () {
    test(
      'Method probeKnownHost treats HTTP, protocol, and compatibility failures as inconclusive',
      () async {
        final List<Exception> inconclusiveFailures = <Exception>[
          const DovahLinkConnectionException(
            'The Host probe returned HTTP 503.',
            httpStatusCode: 503,
          ),
          const DovahLinkProtocolException(
            code: ProtocolErrorCode.malformedMessage,
            message: 'Malformed probe metadata.',
            retryable: false,
          ),
          const DovahLinkCompatibilityException(
            hostVersion: '0.4.0',
            supportedHostVersionRange: '0.5.x',
            failure: HostVersionCompatibilityFailure.hostTooOld,
          ),
        ];
        clientStateService.emit(<DovahLinkHost>[
          Fixtures.buildDovahLinkHost(hostId: hostAId),
        ]);
        monitor.start();

        for (int index = 0; index < inconclusiveFailures.length; index++) {
          await waitForProbeCount(probe, index + 1);
          probe.fail(index, inconclusiveFailures[index]);
          await Future<void>.delayed(Duration.zero);
          expect(availabilityService.updates.last, (
            hostAId,
            DovahLinkHostAvailability.unknown,
          ));
          if (index + 1 < inconclusiveFailures.length) {
            monitor.refreshPresence();
          }
        }
        expect(
          availabilityService.updates,
          isNot(contains((hostAId, DovahLinkHostAvailability.offline))),
        );
      },
    );
  });

  group('Method close behaves correctly', () {
    test(
      'Method close cancels the timer, subscription, and active request',
      () async {
        clientStateService.emit(<DovahLinkHost>[
          Fixtures.buildDovahLinkHost(hostId: hostAId),
        ]);
        monitor.start();
        await waitForProbeCount(probe, 1);

        await monitor.close();
        clientStateService.emit(<DovahLinkHost>[
          Fixtures.buildDovahLinkHost(hostId: hostBId),
        ]);
        await Future<void>.delayed(Duration.zero);

        expect(clientStateService.hasListener, isFalse);
        expect(probe.cancellationCount, 1);
        expect(probe.calls, hasLength(1));
        await monitor.close();
      },
    );
  });
}
