import 'dart:async';
import 'dart:convert';
import 'dart:io';

import 'package:mocktail/mocktail.dart';
import 'package:test/test.dart';

import 'package:dovahlink_client_sdk/dovahlink_client.dart';
import 'package:dovahlink_client_sdk/src/dovahlink_client.dart'
    show buildDovahLinkClientForTesting;
import 'package:dovahlink_client_sdk/src/persistence/in_memory_client_storage.dart';
import 'package:dovahlink_client_sdk/src/persistence/persisted_known_host.dart';
import 'package:dovahlink_client_sdk/src/protocol/json_map.dart';
import 'package:dovahlink_client_sdk/src/shared/enums.dart' show TimeoutClass;
import 'package:dovahlink_client_sdk/src/transport/websocket_transport.dart'
    show IDovahLinkTransport, WebSocketTransport;
import 'fixtures/fixtures.dart';
import 'support/fake_websocket_server.dart';
import 'support/pending_reply.dart';

/// Builds one persisted relationship through the test fixture catalog.
/// @param clientId The local Client ID to represent.
/// @param credential The Host-owned bearer credential, if any.
/// @param recoveryState The Host-owned pairing recovery phase.
/// @param knownHost The relationship metadata to store, if any.
/// @return A fresh persisted-state fixture.
PersistedClientState _persistedState({
  String? clientId = 'client-1',
  String? credential,
  PairingRecoveryState recoveryState = PairingRecoveryState.none,
  DovahLinkHost? knownHost,
}) => Fixtures.buildPersistedClientState(
  clientId: clientId,
  credential: credential,
  recoveryState: recoveryState,
  host: knownHost,
);

/// Owns ordered release and correlation rewriting for fake-transport replies.
class PendingReplyQueue {
  /// Replies waiting for an immediate release or a matching request message ID.
  final List<PendingReply> _replies = <PendingReply>[];

  /// Adds [reply] and releases any newly leading uncorrelated replies.
  void enqueue(PendingReply reply, StreamController<String> incoming) {
    _replies.add(reply);
    while (_replies.isNotEmpty && !_replies.first.needsCorrelation) {
      incoming.add(_replies.removeAt(0).resolve());
    }
  }

  /// Releases leading uncorrelated replies and at most one correlated reply for [messageId].
  void releaseFor(String messageId, StreamController<String> incoming) {
    bool consumedThisSend = false;
    while (_replies.isNotEmpty) {
      final PendingReply next = _replies.first;
      if (!next.needsCorrelation) {
        _replies.removeAt(0);
        incoming.add(next.resolve());
        continue;
      }
      if (consumedThisSend) {
        return;
      }
      _replies.removeAt(0);
      incoming.add(next.resolve(messageId));
      consumedThisSend = true;
    }
  }
}

/// A controllable [IDovahLinkTransport] with [WebSocketTransport]'s per-connection,
/// single-subscription stream semantics. Each [IDovahLinkTransport.connect] creates a fresh inbound
/// stream; [IDovahLinkTransport.messages] waits for frames.
///
/// [FakeDovahLinkTransport.queueResponse] rewrites correlated reply IDs to the next
/// [IDovahLinkTransport.send] message ID while preserving FIFO order. Replies with no correlation,
/// including malformed text, release when they reach the queue head. Use
/// [FakeDovahLinkTransport.queueRawResponse] to test mismatched correlations without rewriting or
/// queue ordering.
class FakeDovahLinkTransport implements IDovahLinkTransport {
  /// Every raw text message sent, in order.
  final List<String> sent = <String>[];

  /// Queued [FakeDovahLinkTransport.queueResponse] replies not yet released, in queue order.
  final PendingReplyQueue _pendingReplies = PendingReplyQueue();

  /// The current connection's inbound stream, or `null` before
  /// [IDovahLinkTransport.connect] or after [IDovahLinkTransport.close].
  StreamController<String>? _incoming;

  /// The URI passed to [IDovahLinkTransport.connect], or `null` if not yet called.
  Uri? connectedUri;

  /// Every URI passed to [IDovahLinkTransport.connect], in order -- unlike
  /// [FakeDovahLinkTransport.connectedUri], proves
  /// how many times and with what arguments it was called across a retry.
  final List<Uri> connectCalls = <Uri>[];

  /// Whether [IDovahLinkTransport.close] was called.
  bool closeCalled = false;

  /// The number of times [IDovahLinkTransport.close] was called -- unlike
  /// [FakeDovahLinkTransport.closeCalled], distinguishes one real
  /// teardown from a duplicate one that should have been deduplicated.
  int closeCallCount = 0;

  /// Optional gate that keeps transport teardown pending until a test releases it.
  Completer<void>? closeGate;

  /// Makes the next [IDovahLinkTransport.connect] call throw [error] instead of succeeding.
  Object? failConnectWith;

  /// Makes every [IDovahLinkTransport.send] call throw [error] instead of succeeding.
  Object? failSendWith;

  /// Makes the next [IDovahLinkTransport.close] call throw [error] instead of succeeding.
  Object? failCloseWith;

  /// Queues one raw JSON response; see the class doc for correlation and release-order behavior.
  void queueResponse(String rawJson) {
    JsonMap? decoded;
    try {
      decoded = jsonDecode(rawJson) as JsonMap;
    } on Object {
      decoded = null;
    }
    _pendingReplies.enqueue(
      decoded == null || decoded['correlationId'] == null
          ? PendingReply.immediate(rawJson)
          : PendingReply.correlated(decoded),
      _requireIncoming(),
    );
  }

  /// Delivers [rawJson] exactly as given, bypassing auto-correlation and queue ordering -- for a
  /// test that deliberately wants an unrewritten or mismatched `correlationId`.
  void queueRawResponse(String rawJson) => _requireIncoming().add(rawJson);

  /// Delivers [error] on the current connection's inbound stream, simulating a transport-level
  /// failure (e.g. a dropped socket) while a request may be pending.
  void failMessagesWith(Object error) => _requireIncoming().addError(error);

  /// Delivers [error] then immediately closes the current connection's inbound stream,
  /// simulating a real socket's `onError` and `onDone` both firing for one dead connection --
  /// for a test proving duplicate teardown signals are deduplicated rather than double-run.
  void failMessagesWithBoth(Object error) {
    final StreamController<String> incoming = _requireIncoming();
    incoming.addError(error);
    unawaited(incoming.close());
  }

  /// Implements [IDovahLinkTransport.connect].
  @override
  Future<void> connect(Uri uri) async {
    final Object? failure = failConnectWith;
    if (failure != null) {
      throw failure;
    }
    connectedUri = uri;
    connectCalls.add(uri);
    // A fresh single-subscription stream per connection, matching the real transport: an old
    // connection's stream is simply discarded, never reused by a new one.
    _incoming = StreamController<String>();
  }

  /// Implements [IDovahLinkTransport.send].
  @override
  Future<void> send(String text) async {
    final Object? failure = failSendWith;
    if (failure != null) {
      throw failure;
    }
    sent.add(text);
    _pendingReplies.releaseFor(
      (jsonDecode(text) as JsonMap)['messageId'] as String,
      _requireIncoming(),
    );
  }

  /// Implements [IDovahLinkTransport.messages].
  @override
  Stream<String> get messages => _requireIncoming().stream;

  /// Implements [IDovahLinkTransport.close].
  @override
  Future<void> close() async {
    closeCalled = true;
    closeCallCount++;
    await closeGate?.future;
    _incoming = null;
    final Object? failure = failCloseWith;
    if (failure != null) {
      throw failure;
    }
  }

  /// Returns the current inbound stream, creating one on first use if
  /// [IDovahLinkTransport.connect] was never
  /// explicitly called -- many tests below exercise a single request/reply exchange directly
  /// without a preceding [IDovahLinkTransport.connect], the same way the fake this replaces always
  /// allowed. [IDovahLinkTransport.connect]
  /// itself always installs a genuinely fresh one, which is what matters for this fake to
  /// correctly model one connection's stream being independent of the next.
  StreamController<String> _requireIncoming() =>
      _incoming ??= StreamController<String>();
}

/// Tracks persistence writes for composition-root invalidation tests.
class TrackingClientStorage implements IClientStorage {
  /// Creates storage seeded with [state].
  TrackingClientStorage(this._state);

  /// State returned by [IClientStorage.load].
  PersistedClientState _state;

  /// Optional error thrown by [IClientStorage.save].
  Object? saveError;

  /// Optional error thrown by [IClientStorage.load].
  Object? loadError;

  /// Optional gate held until a test releases an [IClientStorage.load] call.
  Completer<void>? loadGate;

  /// Optional signal completed when [IClientStorage.load] begins.
  Completer<void>? loadStarted;

  /// Number of attempted [IClientStorage.load] calls.
  int loadCount = 0;

  /// Optional gate held until a test releases an [IClientStorage.save] call.
  Completer<void>? saveGate;

  /// Optional signal completed when [IClientStorage.save] begins.
  Completer<void>? saveStarted;

  /// Number of attempted [IClientStorage.save] calls.
  int saveCount = 0;

  /// See [IClientStorage.load].
  @override
  Future<PersistedClientState> load() async {
    loadCount++;
    if (!(loadStarted?.isCompleted ?? true)) {
      loadStarted!.complete();
    }
    await loadGate?.future;
    final Object? error = loadError;
    if (error != null) {
      throw error;
    }
    return _state;
  }

  /// See [IClientStorage.save].
  @override
  Future<void> save(PersistedClientState state) async {
    saveCount++;
    if (!(saveStarted?.isCompleted ?? true)) {
      saveStarted!.complete();
    }
    await saveGate?.future;
    final Object? error = saveError;
    if (error != null) {
      throw error;
    }
    _state = state;
  }

  /// See [IClientStorage.clear].
  @override
  Future<void> clear() async {
    _state = Fixtures.buildPersistedClientState(clientId: null);
  }
}

/// Reads one canonical protocol fixture as raw JSON text, relative to `protocol/fixtures/`.
String _rawFixture(String relativePath) =>
    File('../../../protocol/fixtures/$relativePath').readAsStringSync();

/// Builds an unsolicited `session_invalidated` envelope for [reason] (a raw wire value, e.g.
/// `'revoked'`).
String _rawSessionInvalidated(
  String reason, {
  String sessionId = 'session-1',
}) => jsonEncode(<String, dynamic>{
  'messageType': 'session_invalidated',
  'messageId': 'message-session-invalidated-1',
  'sessionId': sessionId,
  'correlationId': null,
  'payload': <String, dynamic>{'reason': reason},
  'playContextId': null,
  'clientId': null,
});

/// Returns the decoded subscription updates sent by [transport].
/// @param transport The fake transport whose writes are inspected.
/// @return The client-originated `subscribe` envelopes, in send order.
List<JsonMap> _sentSubscriptionUpdates(FakeDovahLinkTransport transport) =>
    transport.sent
        .map((String raw) => jsonDecode(raw) as JsonMap)
        .where((JsonMap envelope) => envelope['messageType'] == 'subscribe')
        .toList();

/// Builds a correlated `subscription_ack` for the fake Host.
/// @param accepted The state areas the Host accepted.
/// @param rejected The state areas the Host rejected.
/// @return The raw acknowledgement envelope.
String _rawSubscriptionAck({
  required List<String> accepted,
  List<String> rejected = const <String>[],
  String sessionId = 'session-paired-1',
}) => jsonEncode(<String, dynamic>{
  'messageType': 'subscription_ack',
  'messageId': 'message-subscription-ack-1',
  'sessionId': sessionId,
  'correlationId': 'subscribe-placeholder',
  'payload': <String, dynamic>{
    'acceptedStateAreas': accepted,
    'rejectedStateAreas': rejected,
  },
  'playContextId': null,
  'clientId': null,
});

/// Builds one uncorrelated canonical state Snapshot message for fake-host delivery.
/// @param stateArea The registered state area.
/// @param revision The authoritative area revision.
/// @param value The typed wire value, or `null` when unavailable.
/// @return The raw protocol envelope.
String _rawStateSnapshot({
  required String stateArea,
  required int revision,
  required Object? value,
  String? correlationId,
}) => jsonEncode(<String, dynamic>{
  'messageType': 'state_snapshot',
  'messageId': 'snapshot-$stateArea-$revision',
  'sessionId': 'session-1',
  'correlationId': correlationId,
  'payload': <String, dynamic>{
    'stateArea': stateArea,
    'revision': revision,
    'occurredAt': '2026-09-23T12:00:00Z',
    'data': <String, dynamic>{'value': value},
  },
  'stateAuthorityId': 'authority-1',
  'playContextId': 'context-1',
  'clientId': null,
});

/// Builds one uncorrelated canonical state Event message for fake-host delivery.
/// @param stateArea The registered Event state area.
/// @param baseRevision The revision this Event expects the client to hold.
/// @param revision The Event's resulting revision.
/// @param value The complete post-change wire value.
/// @return The raw protocol envelope.
String _rawStateEvent({
  required String stateArea,
  required int baseRevision,
  required int revision,
  required Object? value,
}) => jsonEncode(<String, dynamic>{
  'messageType': 'state_event',
  'messageId': 'event-$stateArea-$revision',
  'sessionId': 'session-1',
  'correlationId': null,
  'payload': <String, dynamic>{
    'stateArea': stateArea,
    'baseRevision': baseRevision,
    'revision': revision,
    'occurredAt': '2026-09-23T12:00:01Z',
    'data': <String, dynamic>{'value': value},
  },
  'stateAuthorityId': 'authority-1',
  'playContextId': 'context-1',
  'clientId': null,
});

/// Maps each typed administrative invalidation reason to its canonical wire value.
const Map<AdministrativeInvalidationReason, String> _invalidationWireValues =
    <AdministrativeInvalidationReason, String>{
      AdministrativeInvalidationReason.revoked: 'revoked',
      AdministrativeInvalidationReason.blocked: 'blocked',
      AdministrativeInvalidationReason.trustReset: 'trust_reset',
      AdministrativeInvalidationReason.factoryReset: 'factory_reset',
    };

/// Connects [client] to the fake transport and admits a [DovahLinkTrustState.unpaired] session for
/// a public-client test.
Future<void> _connectAndHello(
  FakeDovahLinkTransport transport,
  DovahLinkClient client,
) async {
  await client.connect(Uri.parse('ws://127.0.0.1:58231/'));
  transport.queueResponse(_rawFixture('connection/hello-ack.json'));
  transport.queueResponse(_rawFixture('capabilities/capabilities-host.json'));
  await client.hello();
}

/// Connects [client] to the fake transport and admits a trusted session.
/// @param transport The fake transport supplying Host replies.
/// @param client The client to authenticate.
/// @param storage The storage owning the represented Known Host.
/// @param credential The Host-scoped credential to use.
/// @param recoveryState The pairing recovery phase to persist before authentication.
Future<void> _connectAndTrustedHello(
  FakeDovahLinkTransport transport,
  DovahLinkClient client,
  IClientStorage storage, {
  String credential = 'trusted-credential',
  PairingRecoveryState recoveryState = PairingRecoveryState.none,
}) async {
  await storage.save(
    Fixtures.buildPersistedClientState(
      clientId: 'client-1',
      hostId: '81869993-955c-4ba3-a7d0-d35ca86078ea',
      hostName: 'Soneka-Desktop',
      credential: credential,
      recoveryState: recoveryState,
    ),
  );
  transport.queueResponse(_rawFixture('connection/hello-ack-paired.json'));
  transport.queueResponse(_rawFixture('capabilities/capabilities-host.json'));
  await client.authenticateKnownHost(
    DovahLinkHostId('81869993-955c-4ba3-a7d0-d35ca86078ea'),
  );
}

/// Requests each [areas] entry cumulatively and confirms the complete accepted set.
/// @param transport The fake transport supplying subscription acknowledgements.
/// @param client The trusted client whose desired set is updated.
/// @param areas The areas to add in order.
Future<void> _subscribeStateAreas(
  FakeDovahLinkTransport transport,
  DovahLinkClient client,
  Iterable<DovahLinkStateArea> areas,
) async {
  final Set<DovahLinkStateArea> desiredAreas = <DovahLinkStateArea>{};
  for (final DovahLinkStateArea area in areas) {
    desiredAreas.add(area);
    transport.queueResponse(
      _rawSubscriptionAck(
        accepted: <String>[
          for (final DovahLinkStateArea value in DovahLinkStateArea.values)
            if (desiredAreas.contains(value)) value.protocolValue,
        ],
      ),
    );
    await client.subscribeStateArea(area);
  }
}

/// Builds a public client whose reconnect attempts run without production-scale delays.
DovahLinkClient _buildFastReconnectClient(
  FakeDovahLinkTransport transport,
  InMemoryClientStorage storage,
) => buildDovahLinkClientForTesting(
  transport: transport,
  storage: storage,
  reconnectAttemptDelays: const <Duration>[
    Duration.zero,
    Duration.zero,
    Duration.zero,
    Duration.zero,
  ],
  reconnectDeadline: const Duration(seconds: 30),
);

/// Controls sessionless probe results while exercising the composed SDK client.
class ControllableClientPresenceProbe implements IHostPresenceProbe {
  /// Pending probe endpoint and result pairs, in request order.
  final List<({Uri endpoint, Completer<DovahLinkHost> response})> requests =
      <({Uri endpoint, Completer<DovahLinkHost> response})>[];

  /// The number of probes cancelled by terminal client close.
  int cancellationCount = 0;

  /// Holds cancellation completion to prove client close starts session teardown independently.
  Completer<void>? cancellationGate;

  /// Records one probe and completes it only when the test supplies a claim or close cancels it.
  @override
  Future<DovahLinkHost> probe(Uri endpoint, {Future<void>? cancel}) {
    final Completer<DovahLinkHost> response = Completer<DovahLinkHost>();
    requests.add((endpoint: endpoint, response: response));
    if (cancel != null) {
      unawaited(
        cancel.then((_) async {
          await cancellationGate?.future;
          cancellationCount++;
          if (!response.isCompleted) {
            response.completeError(
              const DovahLinkConnectionException('Probe cancelled.'),
            );
          }
        }),
      );
    }
    return response.future;
  }

  /// Completes request [index] with a valid Host claim.
  /// @param index The recorded probe request to complete.
  /// @param hostId The Host ID asserted in its response.
  void succeed(int index, {required String hostId}) {
    final ({Uri endpoint, Completer<DovahLinkHost> response}) request =
        requests[index];
    request.response.complete(
      Fixtures.buildDovahLinkHost(
        hostId: hostId,
        endpoint: request.endpoint.toString(),
      ),
    );
  }
}

/// Controls discovery results while exercising the composed SDK client.
class MockDovahLinkDiscoveryService extends Mock
    implements IDovahLinkDiscoveryService {}

/// Waits for the composed client to start [count] presence probes.
/// @param probe The probe that records each request.
/// @param count The minimum number of requests expected.
Future<void> waitForClientPresenceProbes(
  ControllableClientPresenceProbe probe,
  int count,
) async {
  for (
    int attempt = 0;
    attempt < 20 && probe.requests.length < count;
    attempt++
  ) {
    await pumpEventQueue();
  }
  expect(probe.requests.length, greaterThanOrEqualTo(count));
}

/// Runs public-client behavior tests.
void main() {
  late FakeDovahLinkTransport transport;
  late InMemoryClientStorage storage;
  late DovahLinkClient client;

  setUp(() {
    transport = FakeDovahLinkTransport();
    storage = InMemoryClientStorage();
    client = buildDovahLinkClientForTesting(
      transport: transport,
      storage: storage,
    );
    addTearDown(client.close);
  });

  group('Behavior grouped API composition behaves correctly', () {
    test(
      'Behavior grouped API pairing authentication recovers pending confirmation in the SDK',
      () async {
        await storage.save(
          Fixtures.buildPersistedClientState(
            clientId: 'client-1',
            credential: 'stored-credential',
            recoveryState: PairingRecoveryState.confirming,
          ),
        );
        transport.queueResponse(_rawFixture('connection/hello-ack.json'));
        transport.queueResponse(
          _rawFixture('pairing/pairing-outcome-trusted.json'),
        );

        final DovahLinkPairingHandshake handshake = await client.pairing
            .authenticateCandidate(Uri.parse('ws://127.0.0.1:58231/'));

        expect(handshake.hello.trustState, DovahLinkTrustState.unpaired);
        expect(handshake.trustState, DovahLinkTrustState.trusted);
        expect(client.trustState, DovahLinkTrustState.trusted);
        expect((await storage.load()).pendingPairingRecovery, isNull);
        expect(
          transport.sent.map(
            (String frame) => (jsonDecode(frame) as JsonMap)['messageType'],
          ),
          containsAllInOrder(<String>['hello', 'pairing_ack']),
        );
      },
    );

    test(
      'Behavior grouped API composition exposes one admitted candidate session',
      () async {
        final Uri endpoint = Uri.parse('ws://127.0.0.1:58231/');
        transport.queueResponse(_rawFixture('connection/hello-ack.json'));
        transport.queueResponse(
          _rawFixture('capabilities/capabilities-host.json'),
        );

        final HelloResult result = await client.connections.connectCandidate(
          endpoint,
        );

        expect(client.connections.state, DovahLinkConnectionState.connected);
        expect(client.currentHost.host?.hostId, result.hostId);
        expect(client.currentHost.host?.hostName, result.hostName);
        expect(client.currentHost.trustState, DovahLinkTrustState.unpaired);
        expect(client.currentHost.sessionId, 'session-1');
        expect(
          (await client.currentHost.characterXpChanges.first).status,
          DovahLinkStateStatus.notSubscribed,
        );
        expect(await client.hosts.loadKnownHosts(), isEmpty);

        await client.connections.disconnect();

        expect(client.connections.state, DovahLinkConnectionState.disconnected);
        expect(client.currentHost.host, isNull);
        expect(client.currentHost.trustState, isNull);
        expect(client.currentHost.sessionId, isNull);
      },
    );

    test(
      'Behavior grouped API composition stops an initial retry on deliberate disconnect',
      () async {
        final FakeDovahLinkTransport retryTransport = FakeDovahLinkTransport()
          ..failConnectWith = StateError('Host unavailable');
        final DovahLinkClient retryClient = buildDovahLinkClientForTesting(
          transport: retryTransport,
          storage: InMemoryClientStorage(),
          initialConnectionRetryDelay: const Duration(seconds: 30),
        );
        addTearDown(retryClient.close);
        final StreamIterator<DovahLinkInitialConnectionRetryStatus>
        retryStates = StreamIterator(
          retryClient.connections.initialConnectionRetryChanges,
        );
        addTearDown(retryStates.cancel);
        expect(await retryStates.moveNext(), isTrue);
        expect(
          retryStates.current,
          DovahLinkInitialConnectionRetryStatus.inactive,
        );

        final Future<HelloResult> connection = retryClient.connections
            .connectCandidate(Uri.parse('ws://127.0.0.1:58231/'));
        expect(await retryStates.moveNext(), isTrue);
        expect(
          retryStates.current,
          DovahLinkInitialConnectionRetryStatus.retrying,
        );
        final Future<void> canceledConnection = expectLater(
          connection,
          throwsA(isA<DovahLinkConnectionException>()),
        );

        await retryClient.connections.disconnect();
        await canceledConnection;
        expect(await retryStates.moveNext(), isTrue);
        expect(
          retryStates.current,
          DovahLinkInitialConnectionRetryStatus.inactive,
        );
        expect(retryTransport.connectCalls, isEmpty);
      },
    );

    test(
      'Behavior grouped API composition cancels an active initial retry on close',
      () async {
        final FakeDovahLinkTransport retryTransport = FakeDovahLinkTransport()
          ..failConnectWith = StateError('Host unavailable');
        final DovahLinkClient retryClient = buildDovahLinkClientForTesting(
          transport: retryTransport,
          storage: InMemoryClientStorage(),
          initialConnectionRetryDelay: const Duration(seconds: 30),
        );
        addTearDown(retryClient.close);
        final StreamIterator<DovahLinkInitialConnectionRetryStatus>
        retryStates = StreamIterator(
          retryClient.connections.initialConnectionRetryChanges,
        );
        addTearDown(retryStates.cancel);
        expect(await retryStates.moveNext(), isTrue);
        expect(
          retryStates.current,
          DovahLinkInitialConnectionRetryStatus.inactive,
        );

        final Future<HelloResult> connection = retryClient.connections
            .connectCandidate(Uri.parse('ws://127.0.0.1:58231/'));
        expect(await retryStates.moveNext(), isTrue);
        expect(
          retryStates.current,
          DovahLinkInitialConnectionRetryStatus.retrying,
        );
        final Future<void> canceledConnection = expectLater(
          connection,
          throwsA(isA<DovahLinkConnectionException>()),
        );

        await retryClient.close();

        await canceledConnection;
        expect(await retryStates.moveNext(), isTrue);
        expect(
          retryStates.current,
          DovahLinkInitialConnectionRetryStatus.inactive,
        );
        expect(retryTransport.connectCalls, isEmpty);
      },
    );

    test(
      'Behavior grouped API composition exposes the client-owned candidate projection',
      () async {
        final DovahLinkHost candidate = Fixtures.buildDovahLinkHost();
        final MockDovahLinkDiscoveryService discovery =
            MockDovahLinkDiscoveryService();
        when(
          () => discovery.discover(),
        ).thenAnswer((_) async => <DovahLinkHost>[candidate]);
        final DovahLinkClient pairingClient = buildDovahLinkClientForTesting(
          transport: FakeDovahLinkTransport(),
          storage: InMemoryClientStorage(),
          discoveryService: discovery,
        );
        addTearDown(pairingClient.close);
        final StreamIterator<List<DovahLinkHost>> candidates = StreamIterator(
          pairingClient.pairing.candidates,
        );
        addTearDown(candidates.cancel);

        expect(await candidates.moveNext(), isTrue);
        expect(candidates.current, isEmpty);
        expect(await pairingClient.pairing.discoverHosts(), <DovahLinkHost>[
          candidate,
        ]);
        expect(await candidates.moveNext(), isTrue);
        expect(candidates.current, <DovahLinkHost>[candidate]);
        verify(() => discovery.discover()).called(1);
      },
    );
  });

  group('Method loadKnownHosts behaves correctly', () {
    test(
      'Method loadKnownHosts returns persisted Hosts without credentials',
      () async {
        final DovahLinkHost knownHost = DovahLinkHost(
          hostId: '81869993-955c-4ba3-a7d0-d35ca86078ea',
          hostName: 'LOCAL-HOST',
          endpoint: Uri.parse('ws://127.0.0.1:58231/'),
        );
        await storage.save(
          _persistedState(
            clientId: 'client-1',
            credential: 'private-credential',
            knownHost: knownHost,
          ),
        );

        expect(await client.hosts.loadKnownHosts(), <DovahLinkHost>[knownHost]);
      },
    );

    test(
      'Method loadKnownHosts returns an empty list when no Host is persisted',
      () async {
        expect(await client.hosts.loadKnownHosts(), isEmpty);
      },
    );

    test(
      'Method loadKnownHosts returns an immutable Host-ID-sorted collection',
      () async {
        const String hostAId = '81869993-955c-4ba3-a7d0-d35ca86078ea';
        const String hostBId = '81f6cc90-3a88-40c7-8351-104d4a36c971';
        await storage.save(
          PersistedClientState(
            clientId: 'client-1',
            knownHosts: <String, PersistedKnownHost>{
              hostBId: PersistedKnownHost(
                host: Fixtures.buildDovahLinkHost(hostId: hostBId),
              ),
              hostAId: PersistedKnownHost(
                host: Fixtures.buildDovahLinkHost(hostId: hostAId),
              ),
            },
          ),
        );

        final List<DovahLinkHost> hosts = await client.hosts.loadKnownHosts();

        expect(hosts.map((host) => host.hostId), <String>[hostAId, hostBId]);
        expect(
          () => hosts.add(Fixtures.buildDovahLinkHost()),
          throwsUnsupportedError,
        );
      },
    );

    test('Method loadKnownHosts propagates corrupt storage errors', () async {
      final TrackingClientStorage failingStorage = TrackingClientStorage(
        Fixtures.buildPersistedClientState(clientId: 'client-1'),
      )..loadError = const DovahLinkStorageException('corrupt state');
      final DovahLinkClient failingClient = buildDovahLinkClientForTesting(
        transport: FakeDovahLinkTransport(),
        storage: failingStorage,
      );

      await expectLater(
        failingClient.hosts.loadKnownHosts(),
        throwsA(isA<DovahLinkStorageException>()),
      );
    });
  });

  group('Property knownHostsChanges behaves correctly', () {
    test(
      'Property knownHostsChanges first emits an empty collection for a new client',
      () async {
        expect(await client.hosts.knownHostsChanges.first, isEmpty);
      },
    );

    test(
      'Property knownHostsChanges refreshes metadata after trusted hello',
      () async {
        final DovahLinkHost oldHost = DovahLinkHost(
          hostId: '81869993-955c-4ba3-a7d0-d35ca86078ea',
          hostName: 'OLD-NAME',
          endpoint: Uri.parse('ws://127.0.0.1:58230/'),
        );
        await storage.save(
          _persistedState(
            clientId: 'client-1',
            credential: 'known-host-credential',
            knownHost: oldHost,
          ),
        );
        final StreamIterator<List<DovahLinkHost>> changes = StreamIterator(
          client.hosts.knownHostsChanges,
        );
        addTearDown(changes.cancel);

        expect(await changes.moveNext(), isTrue);
        expect(changes.current, <DovahLinkHost>[oldHost]);
        transport.queueResponse(
          _rawFixture('connection/hello-ack-paired.json'),
        );
        transport.queueResponse(
          _rawFixture('capabilities/capabilities-host.json'),
        );

        await client.authenticateKnownHost(DovahLinkHostId(oldHost.hostId));

        expect(await changes.moveNext(), isTrue);
        expect(changes.current, <DovahLinkHost>[
          DovahLinkHost(
            hostId: oldHost.hostId,
            hostName: 'Soneka-Desktop',
            endpoint: Uri.parse('ws://127.0.0.1:58230/'),
          ),
        ]);
      },
    );
  });

  group('Method discoverHosts behaves correctly', () {
    test(
      'Method discoverHosts removes Known Host IDs regardless of endpoint and ID casing',
      () async {
        const String hostAId = '81869993-955c-4ba3-a7d0-d35ca86078ea';
        const String hostBId = '81f6cc90-3a88-40c7-8351-104d4a36c971';
        final DovahLinkHost knownA = Fixtures.buildDovahLinkHost(
          hostId: hostAId,
          endpoint: 'ws://127.0.0.1:58230/',
        );
        final DovahLinkHost knownB = Fixtures.buildDovahLinkHost(
          hostId: hostBId,
        );
        final DovahLinkHost claimedA = Fixtures.buildDovahLinkHost(
          hostId: hostAId.toUpperCase(),
          endpoint: 'ws://127.0.0.1:58232/',
        );
        final DovahLinkHost candidateC = Fixtures.buildDovahLinkHost(
          hostId: 'aaaaaaaa-aaaa-4aaa-8aaa-aaaaaaaaaaaa',
        );
        await storage.save(
          PersistedClientState(
            knownHosts: <String, PersistedKnownHost>{
              hostAId: PersistedKnownHost(host: knownA),
              hostBId: PersistedKnownHost(host: knownB),
            },
          ),
        );
        final MockDovahLinkDiscoveryService discovery =
            MockDovahLinkDiscoveryService();
        when(
          () => discovery.discover(),
        ).thenAnswer((_) async => <DovahLinkHost>[claimedA, candidateC]);
        final DovahLinkClient discoveryClient = buildDovahLinkClientForTesting(
          transport: FakeDovahLinkTransport(),
          storage: storage,
          discoveryService: discovery,
        );
        addTearDown(discoveryClient.close);
        final StreamIterator<List<DovahLinkHost>> candidates = StreamIterator(
          discoveryClient.candidateHostsChanges,
        );
        addTearDown(candidates.cancel);

        expect(await candidates.moveNext(), isTrue);
        expect(candidates.current, isEmpty);
        expect(await discoveryClient.discoverHosts(), <DovahLinkHost>[
          candidateC,
        ]);
        expect(await candidates.moveNext(), isTrue);
        expect(candidates.current, <DovahLinkHost>[candidateC]);
        expect(await discoveryClient.hosts.loadKnownHosts(), <DovahLinkHost>[
          knownA,
          knownB,
        ]);
      },
    );

    test(
      'Method discoverHosts keeps different Host IDs at one endpoint distinct',
      () async {
        final Uri sharedEndpoint = Uri.parse('ws://127.0.0.1:58232/');
        final List<DovahLinkHost> discovered = <DovahLinkHost>[
          Fixtures.buildDovahLinkHost(
            hostId: 'aaaaaaaa-aaaa-4aaa-8aaa-aaaaaaaaaaaa',
            endpoint: sharedEndpoint.toString(),
          ),
          Fixtures.buildDovahLinkHost(
            hostId: 'bbbbbbbb-bbbb-4bbb-8bbb-bbbbbbbbbbbb',
            endpoint: sharedEndpoint.toString(),
          ),
        ];
        final MockDovahLinkDiscoveryService discovery =
            MockDovahLinkDiscoveryService();
        when(() => discovery.discover()).thenAnswer((_) async => discovered);
        final DovahLinkClient discoveryClient = buildDovahLinkClientForTesting(
          transport: FakeDovahLinkTransport(),
          storage: InMemoryClientStorage(),
          discoveryService: discovery,
        );
        addTearDown(discoveryClient.close);

        expect(await discoveryClient.discoverHosts(), discovered);
      },
    );

    test(
      'Method discoverHosts ignores an older discovery that finishes last',
      () async {
        final DovahLinkHost olderCandidate = Fixtures.buildDovahLinkHost(
          hostId: 'aaaaaaaa-aaaa-4aaa-8aaa-aaaaaaaaaaaa',
        );
        final DovahLinkHost newerCandidate = Fixtures.buildDovahLinkHost(
          hostId: 'bbbbbbbb-bbbb-4bbb-8bbb-bbbbbbbbbbbb',
        );
        final Completer<List<DovahLinkHost>> olderResult =
            Completer<List<DovahLinkHost>>();
        final Completer<List<DovahLinkHost>> newerResult =
            Completer<List<DovahLinkHost>>();
        final Completer<void> olderStarted = Completer<void>();
        final Completer<void> newerStarted = Completer<void>();
        int callCount = 0;
        final MockDovahLinkDiscoveryService discovery =
            MockDovahLinkDiscoveryService();
        when(() => discovery.discover()).thenAnswer((_) {
          callCount++;
          if (callCount == 1) {
            olderStarted.complete();
            return olderResult.future;
          }
          newerStarted.complete();
          return newerResult.future;
        });
        final DovahLinkClient discoveryClient = buildDovahLinkClientForTesting(
          transport: FakeDovahLinkTransport(),
          storage: InMemoryClientStorage(),
          discoveryService: discovery,
        );
        addTearDown(discoveryClient.close);

        final Future<List<DovahLinkHost>> olderDiscovery = discoveryClient
            .discoverHosts();
        await olderStarted.future.timeout(const Duration(seconds: 5));
        final Future<List<DovahLinkHost>> newerDiscovery = discoveryClient
            .discoverHosts();
        await newerStarted.future.timeout(const Duration(seconds: 5));
        newerResult.complete(<DovahLinkHost>[newerCandidate]);
        expect(await newerDiscovery, <DovahLinkHost>[newerCandidate]);
        olderResult.complete(<DovahLinkHost>[olderCandidate]);
        expect(await olderDiscovery, <DovahLinkHost>[newerCandidate]);
      },
    );

    test(
      'Method discoverHosts stops after close while initial storage is pending',
      () async {
        final TrackingClientStorage pendingStorage = TrackingClientStorage(
          Fixtures.buildPersistedClientState(),
        );
        final Completer<void> loadStarted = Completer<void>();
        final Completer<void> loadGate = Completer<void>();
        pendingStorage
          ..loadStarted = loadStarted
          ..loadGate = loadGate;
        final MockDovahLinkDiscoveryService discovery =
            MockDovahLinkDiscoveryService();
        final DovahLinkClient closeClient = buildDovahLinkClientForTesting(
          transport: FakeDovahLinkTransport(),
          storage: pendingStorage,
          discoveryService: discovery,
        );
        final List<List<DovahLinkHost>> candidateUpdates =
            <List<DovahLinkHost>>[];
        final List<Object> streamErrors = <Object>[];
        final Completer<void> initialUpdate = Completer<void>();
        final StreamSubscription<List<DovahLinkHost>> candidates = closeClient
            .candidateHostsChanges
            .listen((List<DovahLinkHost> hosts) {
              candidateUpdates.add(hosts);
              if (!initialUpdate.isCompleted) {
                initialUpdate.complete();
              }
            }, onError: streamErrors.add);
        addTearDown(candidates.cancel);
        await initialUpdate.future;

        final Future<List<DovahLinkHost>> pendingDiscovery = closeClient
            .discoverHosts();
        await loadStarted.future;
        await closeClient.close();
        loadGate.complete();

        expect(await pendingDiscovery, isEmpty);
        await pumpEventQueue();
        expect(closeClient.isObservingCandidateKnownHosts, isFalse);
        verifyNever(() => discovery.discover());
        expect(candidateUpdates, hasLength(1));
        expect(candidateUpdates.single, isEmpty);
        expect(streamErrors, isEmpty);
        expect(pendingStorage.loadCount, 1);
      },
    );

    test(
      'Method discoverHosts ignores a result that completes after close',
      () async {
        final Completer<List<DovahLinkHost>> discoveryResult =
            Completer<List<DovahLinkHost>>();
        final Completer<void> discoveryStarted = Completer<void>();
        final MockDovahLinkDiscoveryService discovery =
            MockDovahLinkDiscoveryService();
        when(() => discovery.discover()).thenAnswer((_) {
          discoveryStarted.complete();
          return discoveryResult.future;
        });
        final DovahLinkClient closeClient = buildDovahLinkClientForTesting(
          transport: FakeDovahLinkTransport(),
          storage: InMemoryClientStorage(),
          discoveryService: discovery,
        );
        final List<List<DovahLinkHost>> candidateUpdates =
            <List<DovahLinkHost>>[];
        final List<Object> streamErrors = <Object>[];
        final Completer<void> initialUpdate = Completer<void>();
        final StreamSubscription<List<DovahLinkHost>> candidates = closeClient
            .candidateHostsChanges
            .listen((List<DovahLinkHost> hosts) {
              candidateUpdates.add(hosts);
              if (!initialUpdate.isCompleted) {
                initialUpdate.complete();
              }
            }, onError: streamErrors.add);
        addTearDown(candidates.cancel);
        await initialUpdate.future;

        final Future<List<DovahLinkHost>> pendingDiscovery = closeClient
            .discoverHosts();
        await discoveryStarted.future;
        await closeClient.close();
        discoveryResult.complete(<DovahLinkHost>[
          Fixtures.buildDovahLinkHost(
            hostId: 'aaaaaaaa-aaaa-4aaa-8aaa-aaaaaaaaaaaa',
          ),
        ]);

        expect(await pendingDiscovery, isEmpty);
        await pumpEventQueue();
        expect(candidateUpdates, hasLength(1));
        expect(candidateUpdates.single, isEmpty);
        expect(streamErrors, isEmpty);
      },
    );

    test(
      'Method discoverHosts suppresses an initial storage error after close',
      () async {
        final TrackingClientStorage pendingStorage = TrackingClientStorage(
          Fixtures.buildPersistedClientState(),
        );
        final Completer<void> loadStarted = Completer<void>();
        final Completer<void> loadGate = Completer<void>();
        pendingStorage
          ..loadStarted = loadStarted
          ..loadGate = loadGate;
        final MockDovahLinkDiscoveryService discovery =
            MockDovahLinkDiscoveryService();
        final DovahLinkClient closeClient = buildDovahLinkClientForTesting(
          transport: FakeDovahLinkTransport(),
          storage: pendingStorage,
          discoveryService: discovery,
        );

        final Future<List<DovahLinkHost>> pendingDiscovery = closeClient
            .discoverHosts();
        await loadStarted.future;
        await closeClient.close();
        pendingStorage.loadError = const DovahLinkStorageException(
          'storage failed after close',
        );
        loadGate.complete();

        expect(await pendingDiscovery, isEmpty);
        expect(closeClient.isObservingCandidateKnownHosts, isFalse);
        verifyNever(() => discovery.discover());
        expect(pendingStorage.loadCount, 1);
      },
    );

    test(
      'Method discoverHosts stops before its final read when close follows probe completion',
      () async {
        final Completer<List<DovahLinkHost>> discoveryResult =
            Completer<List<DovahLinkHost>>();
        final MockDovahLinkDiscoveryService discovery =
            MockDovahLinkDiscoveryService();
        when(
          () => discovery.discover(),
        ).thenAnswer((_) => discoveryResult.future);
        final TrackingClientStorage trackingStorage = TrackingClientStorage(
          Fixtures.buildPersistedClientState(),
        );
        final DovahLinkClient closeClient = buildDovahLinkClientForTesting(
          transport: FakeDovahLinkTransport(),
          storage: trackingStorage,
          discoveryService: discovery,
        );
        final List<List<DovahLinkHost>> candidateUpdates =
            <List<DovahLinkHost>>[];
        final Completer<void> initialUpdate = Completer<void>();
        final StreamSubscription<List<DovahLinkHost>> candidates = closeClient
            .candidateHostsChanges
            .listen((List<DovahLinkHost> hosts) {
              candidateUpdates.add(hosts);
              if (!initialUpdate.isCompleted) {
                initialUpdate.complete();
              }
            });
        addTearDown(candidates.cancel);
        await initialUpdate.future;

        final Future<List<DovahLinkHost>> pendingDiscovery = closeClient
            .discoverHosts();
        await pumpEventQueue();
        discoveryResult.complete(<DovahLinkHost>[
          Fixtures.buildDovahLinkHost(
            hostId: 'aaaaaaaa-aaaa-4aaa-8aaa-aaaaaaaaaaaa',
          ),
        ]);
        final Future<void> closing = closeClient.close();
        await closing;

        expect(await pendingDiscovery, isEmpty);
        expect(candidateUpdates, hasLength(1));
        expect(candidateUpdates.single, isEmpty);
        expect(trackingStorage.loadCount, 1);
      },
    );

    test(
      'Method discoverHosts replaces candidates without persisting them or accumulating duplicates',
      () async {
        final DovahLinkHost candidate = Fixtures.buildDovahLinkHost();
        final MockDovahLinkDiscoveryService discovery =
            MockDovahLinkDiscoveryService();
        when(
          () => discovery.discover(),
        ).thenAnswer((_) async => <DovahLinkHost>[candidate, candidate]);
        final InMemoryClientStorage candidateStorage = InMemoryClientStorage();
        final DovahLinkClient discoveryClient = buildDovahLinkClientForTesting(
          transport: FakeDovahLinkTransport(),
          storage: candidateStorage,
          discoveryService: discovery,
        );
        addTearDown(discoveryClient.close);

        expect(await discoveryClient.discoverHosts(), <DovahLinkHost>[
          candidate,
        ]);
        expect(await discoveryClient.discoverHosts(), <DovahLinkHost>[
          candidate,
        ]);
        expect((await candidateStorage.load()).knownHosts, isEmpty);
      },
    );

    test(
      'Method discoverHosts leaves persisted Known Hosts intact after empty discovery',
      () async {
        final DovahLinkHost knownHost = Fixtures.buildDovahLinkHost();
        await storage.save(
          PersistedClientState(
            knownHosts: <String, PersistedKnownHost>{
              knownHost.hostId: PersistedKnownHost(host: knownHost),
            },
          ),
        );
        final MockDovahLinkDiscoveryService discovery =
            MockDovahLinkDiscoveryService();
        when(
          () => discovery.discover(),
        ).thenAnswer((_) async => const <DovahLinkHost>[]);
        final DovahLinkClient discoveryClient = buildDovahLinkClientForTesting(
          transport: FakeDovahLinkTransport(),
          storage: storage,
          discoveryService: discovery,
        );
        addTearDown(discoveryClient.close);

        expect(await discoveryClient.discoverHosts(), isEmpty);
        expect(await discoveryClient.hosts.loadKnownHosts(), <DovahLinkHost>[
          knownHost,
        ]);
      },
    );

    test(
      'Method discoverHosts does not publish when a Known Host commits during discovery',
      () async {
        const String hostId = 'aaaaaaaa-aaaa-4aaa-8aaa-aaaaaaaaaaaa';
        final DovahLinkHost claimedHost = Fixtures.buildDovahLinkHost(
          hostId: hostId,
          endpoint: 'ws://127.0.0.1:58232/',
        );
        final Completer<List<DovahLinkHost>> discoveryResult =
            Completer<List<DovahLinkHost>>();
        final MockDovahLinkDiscoveryService discovery =
            MockDovahLinkDiscoveryService();
        when(
          () => discovery.discover(),
        ).thenAnswer((_) => discoveryResult.future);
        final FakeDovahLinkTransport pairingTransport =
            FakeDovahLinkTransport();
        final DovahLinkClient discoveryClient = buildDovahLinkClientForTesting(
          transport: pairingTransport,
          storage: InMemoryClientStorage(),
          discoveryService: discovery,
        );
        addTearDown(discoveryClient.close);
        final StreamIterator<List<DovahLinkHost>> candidates = StreamIterator(
          discoveryClient.candidateHostsChanges,
        );
        addTearDown(candidates.cancel);
        expect(await candidates.moveNext(), isTrue);
        expect(candidates.current, isEmpty);

        final Future<List<DovahLinkHost>> pendingDiscovery = discoveryClient
            .discoverHosts();
        await pumpEventQueue();
        final JsonMap helloAck =
            jsonDecode(_rawFixture('connection/hello-ack-paired.json'))
                as JsonMap;
        final JsonMap helloPayload = helloAck['payload'] as JsonMap;
        helloPayload['hostId'] = hostId;
        helloPayload['clientIdentityKind'] = 'unpaired';
        pairingTransport.queueResponse(jsonEncode(helloAck));
        pairingTransport.queueResponse(
          _rawFixture('capabilities/capabilities-host.json'),
        );
        await discoveryClient.authenticateCandidate(claimedHost.endpoint);
        pairingTransport.queueResponse(
          _rawFixture('pairing/pairing-outcome-credential-issued.json'),
        );
        pairingTransport.queueResponse(
          _rawFixture('pairing/pairing-outcome-trusted.json'),
        );
        await discoveryClient.confirmPairingCode(code: '123456');

        discoveryResult.complete(<DovahLinkHost>[claimedHost]);
        expect(await pendingDiscovery, isEmpty);
        expect(await discoveryClient.hosts.loadKnownHosts(), hasLength(1));
      },
    );

    test(
      'Method discoverHosts surfaces authoritative storage failures',
      () async {
        final TrackingClientStorage failingStorage = TrackingClientStorage(
          Fixtures.buildPersistedClientState(),
        )..loadError = const DovahLinkStorageException('corrupt state');
        final MockDovahLinkDiscoveryService discovery =
            MockDovahLinkDiscoveryService();
        when(() => discovery.discover()).thenAnswer(
          (_) async => <DovahLinkHost>[Fixtures.buildDovahLinkHost()],
        );
        final DovahLinkClient discoveryClient = buildDovahLinkClientForTesting(
          transport: FakeDovahLinkTransport(),
          storage: failingStorage,
          discoveryService: discovery,
        );
        addTearDown(discoveryClient.close);

        await expectLater(
          discoveryClient.discoverHosts(),
          throwsA(isA<DovahLinkStorageException>()),
        );
      },
    );
  });

  group('Property candidateHostsChanges behaves correctly', () {
    test(
      'Property candidateHostsChanges reports storage errors and receives later candidate state',
      () async {
        final TrackingClientStorage failingStorage = TrackingClientStorage(
          Fixtures.buildPersistedClientState(),
        )..loadError = const DovahLinkStorageException('corrupt state');
        final DovahLinkHost candidate = Fixtures.buildDovahLinkHost();
        final MockDovahLinkDiscoveryService discovery =
            MockDovahLinkDiscoveryService();
        when(
          () => discovery.discover(),
        ).thenAnswer((_) async => <DovahLinkHost>[candidate]);
        final DovahLinkClient discoveryClient = buildDovahLinkClientForTesting(
          transport: FakeDovahLinkTransport(),
          storage: failingStorage,
          discoveryService: discovery,
        );
        addTearDown(discoveryClient.close);
        final Completer<void> storageFailure = Completer<void>();
        final Completer<List<DovahLinkHost>> candidatePublished =
            Completer<List<DovahLinkHost>>();
        final StreamSubscription<List<DovahLinkHost>> subscription =
            discoveryClient.candidateHostsChanges.listen(
              (List<DovahLinkHost> hosts) {
                if (hosts.isNotEmpty && !candidatePublished.isCompleted) {
                  candidatePublished.complete(hosts);
                }
              },
              onError: (Object error, StackTrace stackTrace) {
                if (error is DovahLinkStorageException &&
                    !storageFailure.isCompleted) {
                  storageFailure.complete();
                }
              },
            );
        addTearDown(subscription.cancel);
        await expectLater(
          discoveryClient.discoverHosts(),
          throwsA(isA<DovahLinkStorageException>()),
        );
        await storageFailure.future.timeout(const Duration(seconds: 5));
        failingStorage.loadError = null;
        expect(await discoveryClient.discoverHosts(), <DovahLinkHost>[
          candidate,
        ]);
        expect(await candidatePublished.future, <DovahLinkHost>[candidate]);
      },
    );

    test(
      'Property candidateHostsChanges removes a candidate after pairing commits its Known Host relationship',
      () async {
        const String hostId = 'aaaaaaaa-aaaa-4aaa-8aaa-aaaaaaaaaaaa';
        final DovahLinkHost candidate = Fixtures.buildDovahLinkHost(
          hostId: hostId,
          endpoint: 'ws://127.0.0.1:58232/',
        );
        final MockDovahLinkDiscoveryService discovery =
            MockDovahLinkDiscoveryService();
        when(
          () => discovery.discover(),
        ).thenAnswer((_) async => <DovahLinkHost>[candidate]);
        final FakeDovahLinkTransport pairingTransport =
            FakeDovahLinkTransport();
        final DovahLinkClient discoveryClient = buildDovahLinkClientForTesting(
          transport: pairingTransport,
          storage: InMemoryClientStorage(),
          discoveryService: discovery,
        );
        addTearDown(discoveryClient.close);
        final StreamIterator<List<DovahLinkHost>> candidates = StreamIterator(
          discoveryClient.candidateHostsChanges,
        );
        addTearDown(candidates.cancel);
        expect(await candidates.moveNext(), isTrue);
        expect(candidates.current, isEmpty);
        expect(await discoveryClient.discoverHosts(), <DovahLinkHost>[
          candidate,
        ]);
        expect(await candidates.moveNext(), isTrue);
        expect(candidates.current, <DovahLinkHost>[candidate]);

        final JsonMap helloAck =
            jsonDecode(_rawFixture('connection/hello-ack-paired.json'))
                as JsonMap;
        final JsonMap helloPayload = helloAck['payload'] as JsonMap;
        helloPayload['hostId'] = hostId;
        helloPayload['clientIdentityKind'] = 'unpaired';
        pairingTransport.queueResponse(jsonEncode(helloAck));
        pairingTransport.queueResponse(
          _rawFixture('capabilities/capabilities-host.json'),
        );
        await discoveryClient.authenticateCandidate(candidate.endpoint);
        pairingTransport.queueResponse(
          _rawFixture('pairing/pairing-outcome-credential-issued.json'),
        );
        pairingTransport.queueResponse(
          _rawFixture('pairing/pairing-outcome-trusted.json'),
        );
        await discoveryClient.confirmPairingCode(code: '123456');

        expect(await candidates.moveNext(), isTrue);
        expect(candidates.current, isEmpty);
        expect(await discoveryClient.hosts.loadKnownHosts(), hasLength(1));
      },
    );
  });

  group('Property knownHostStatesChanges behaves correctly', () {
    test(
      'Property knownHostStatesChanges starts persisted Hosts unknown without connecting',
      () async {
        const String hostAId = '81869993-955c-4ba3-a7d0-d35ca86078ea';
        const String hostBId = '81f6cc90-3a88-40c7-8351-104d4a36c971';
        final DovahLinkHost hostA = Fixtures.buildDovahLinkHost(
          hostId: hostAId,
        );
        final DovahLinkHost hostB = Fixtures.buildDovahLinkHost(
          hostId: hostBId,
        );
        final InMemoryClientStorage runtimeStorage = InMemoryClientStorage();
        await runtimeStorage.save(
          PersistedClientState(
            clientId: 'client-1',
            knownHosts: <String, PersistedKnownHost>{
              hostBId: PersistedKnownHost(host: hostB, credential: 'secret-b'),
              hostAId: PersistedKnownHost(host: hostA, credential: 'secret-a'),
            },
          ),
        );
        final FakeDovahLinkTransport runtimeTransport =
            FakeDovahLinkTransport();
        final DovahLinkClient runtimeClient = buildDovahLinkClientForTesting(
          transport: runtimeTransport,
          storage: runtimeStorage,
        );

        final List<DovahLinkKnownHostState> states =
            await runtimeClient.hosts.knownHostStatesChanges.first;

        expect(states.map((state) => state.host.hostId), <String>[
          hostAId,
          hostBId,
        ]);
        expect(
          states.map((state) => state.availability),
          <DovahLinkHostAvailability>[
            DovahLinkHostAvailability.unknown,
            DovahLinkHostAvailability.unknown,
          ],
        );
        expect(
          () => states.add(Fixtures.buildDovahLinkKnownHostState()),
          throwsUnsupportedError,
        );
        expect(runtimeTransport.connectCalls, isEmpty);
      },
    );

    test(
      'Property knownHostStatesChanges reports online after Known Host admission',
      () async {
        const String hostId = '81869993-955c-4ba3-a7d0-d35ca86078ea';
        final InMemoryClientStorage runtimeStorage = InMemoryClientStorage();
        await runtimeStorage.save(
          _persistedState(
            clientId: 'client-1',
            credential: 'known-host-credential',
            knownHost: Fixtures.buildDovahLinkHost(
              hostId: hostId,
              hostName: 'Soneka-Desktop',
            ),
          ),
        );
        final FakeDovahLinkTransport runtimeTransport =
            FakeDovahLinkTransport();
        final DovahLinkClient runtimeClient = buildDovahLinkClientForTesting(
          transport: runtimeTransport,
          storage: runtimeStorage,
        );
        final List<List<DovahLinkKnownHostState>> snapshots = [];
        final StreamSubscription<List<DovahLinkKnownHostState>> subscription =
            runtimeClient.hosts.knownHostStatesChanges.listen(snapshots.add);
        runtimeTransport.queueResponse(
          _rawFixture('connection/hello-ack-paired.json'),
        );
        runtimeTransport.queueResponse(
          _rawFixture('capabilities/capabilities-host.json'),
        );
        await Future<void>.delayed(Duration.zero);

        expect(
          snapshots.single.single.availability,
          DovahLinkHostAvailability.unknown,
        );
        await runtimeClient.authenticateKnownHost(DovahLinkHostId(hostId));
        await Future<void>.delayed(Duration.zero);

        expect(
          snapshots.last.single.availability,
          DovahLinkHostAvailability.online,
        );
        expect(
          snapshots.last.single.sessionState,
          DovahLinkKnownHostSessionState.connected,
        );
        await runtimeClient.disconnect();
        await Future<void>.delayed(Duration.zero);
        expect(
          snapshots.last.single.availability,
          DovahLinkHostAvailability.online,
        );
        expect(
          snapshots.last.single.sessionState,
          DovahLinkKnownHostSessionState.disconnected,
        );
        await subscription.cancel();
      },
    );

    test(
      'Property knownHostStatesChanges does not associate a candidate session with a matching Host claim',
      () async {
        const String hostId = '81869993-955c-4ba3-a7d0-d35ca86078ea';
        final InMemoryClientStorage runtimeStorage = InMemoryClientStorage();
        await runtimeStorage.save(
          _persistedState(
            clientId: 'client-1',
            credential: 'known-host-credential',
            knownHost: Fixtures.buildDovahLinkHost(hostId: hostId),
          ),
        );
        final FakeDovahLinkTransport runtimeTransport =
            FakeDovahLinkTransport();
        final DovahLinkClient runtimeClient = buildDovahLinkClientForTesting(
          transport: runtimeTransport,
          storage: runtimeStorage,
        );
        final List<List<DovahLinkKnownHostState>> snapshots = [];
        final StreamSubscription<List<DovahLinkKnownHostState>> subscription =
            runtimeClient.hosts.knownHostStatesChanges.listen(snapshots.add);
        runtimeTransport.queueResponse(
          _rawFixture('connection/hello-ack.json'),
        );
        runtimeTransport.queueResponse(
          _rawFixture('capabilities/capabilities-host.json'),
        );
        await Future<void>.delayed(Duration.zero);

        await runtimeClient.authenticateCandidate(
          Uri.parse('ws://127.0.0.1:58231/'),
        );
        await Future<void>.delayed(Duration.zero);

        expect(
          runtimeClient.connectionState,
          DovahLinkConnectionState.connected,
        );
        expect(snapshots.last.single.host.hostId, hostId);
        expect(
          snapshots.last.single.sessionState,
          DovahLinkKnownHostSessionState.disconnected,
        );
        await runtimeClient.disconnect();
        await Future<void>.delayed(Duration.zero);
        expect(
          snapshots.last.single.availability,
          DovahLinkHostAvailability.unknown,
        );
        expect(
          snapshots.last.single.sessionState,
          DovahLinkKnownHostSessionState.disconnected,
        );
        await subscription.cancel();
      },
    );

    test(
      'Property knownHostStatesChanges preserves availability through administrative invalidation',
      () async {
        const String hostId = '81869993-955c-4ba3-a7d0-d35ca86078ea';
        final InMemoryClientStorage runtimeStorage = InMemoryClientStorage();
        await runtimeStorage.save(
          _persistedState(
            clientId: 'client-1',
            credential: 'known-host-credential',
            knownHost: Fixtures.buildDovahLinkHost(
              hostId: hostId,
              hostName: 'Soneka-Desktop',
            ),
          ),
        );
        final FakeDovahLinkTransport runtimeTransport =
            FakeDovahLinkTransport();
        final DovahLinkClient runtimeClient = buildDovahLinkClientForTesting(
          transport: runtimeTransport,
          storage: runtimeStorage,
        );
        final List<List<DovahLinkKnownHostState>> snapshots = [];
        final StreamSubscription<List<DovahLinkKnownHostState>> subscription =
            runtimeClient.hosts.knownHostStatesChanges.listen(snapshots.add);
        runtimeTransport.queueResponse(
          _rawFixture('connection/hello-ack-paired.json'),
        );
        runtimeTransport.queueResponse(
          _rawFixture('capabilities/capabilities-host.json'),
        );
        await Future<void>.delayed(Duration.zero);
        await runtimeClient.authenticateKnownHost(DovahLinkHostId(hostId));
        await Future<void>.delayed(Duration.zero);

        runtimeTransport.queueResponse(
          _rawSessionInvalidated('revoked', sessionId: 'session-paired-1'),
        );
        await Future<void>.delayed(Duration.zero);
        await Future<void>.delayed(Duration.zero);

        expect(
          runtimeClient.connectionState,
          DovahLinkConnectionState.administrativelyInvalidated,
        );
        expect(
          snapshots.last.single.sessionState,
          DovahLinkKnownHostSessionState.disconnected,
        );
        expect(
          snapshots.last.single.availability,
          DovahLinkHostAvailability.online,
        );
        await runtimeClient.disconnect();
        await Future<void>.delayed(Duration.zero);
        expect(
          snapshots.last.single.availability,
          DovahLinkHostAvailability.online,
        );
        await subscription.cancel();
      },
    );

    test(
      'Property knownHostStatesChanges reports offline after an explicit transport connection failure',
      () async {
        const String hostId = '81869993-955c-4ba3-a7d0-d35ca86078ea';
        final InMemoryClientStorage runtimeStorage = InMemoryClientStorage();
        await runtimeStorage.save(
          _persistedState(
            clientId: 'client-1',
            credential: 'known-host-credential',
            knownHost: Fixtures.buildDovahLinkHost(hostId: hostId),
          ),
        );
        final FakeDovahLinkTransport runtimeTransport = FakeDovahLinkTransport()
          ..failConnectWith = const SocketException('refused');
        final DovahLinkClient runtimeClient = buildDovahLinkClientForTesting(
          transport: runtimeTransport,
          storage: runtimeStorage,
          initialConnectionRetryDelay: const Duration(seconds: 30),
        );
        final List<List<DovahLinkKnownHostState>> snapshots = [];
        final StreamSubscription<List<DovahLinkKnownHostState>> subscription =
            runtimeClient.hosts.knownHostStatesChanges.listen(snapshots.add);
        final StreamIterator<DovahLinkInitialConnectionRetryStatus>
        retryStates = StreamIterator(
          runtimeClient.connections.initialConnectionRetryChanges,
        );
        addTearDown(retryStates.cancel);
        await Future<void>.delayed(Duration.zero);

        final Future<HelloResult> authentication = runtimeClient.connections
            .connectKnownHost(DovahLinkHostId(hostId));
        expect(await retryStates.moveNext(), isTrue);
        expect(
          retryStates.current,
          DovahLinkInitialConnectionRetryStatus.inactive,
        );
        expect(await retryStates.moveNext(), isTrue);
        expect(
          retryStates.current,
          DovahLinkInitialConnectionRetryStatus.retrying,
        );
        final Future<void> canceledAuthentication = expectLater(
          authentication,
          throwsA(isA<DovahLinkConnectionException>()),
        );
        await Future<void>.delayed(Duration.zero);

        expect(
          snapshots.any(
            (List<DovahLinkKnownHostState> snapshot) =>
                snapshot.single.sessionState ==
                DovahLinkKnownHostSessionState.connecting,
          ),
          isTrue,
        );
        expect(
          snapshots.last.single.availability,
          DovahLinkHostAvailability.offline,
        );
        expect(
          snapshots.last.single.sessionState,
          DovahLinkKnownHostSessionState.disconnected,
        );
        await runtimeClient.connections.disconnect();
        await canceledAuthentication;
        await Future<void>.delayed(Duration.zero);
        expect(
          snapshots.last.single.availability,
          DovahLinkHostAvailability.offline,
        );
        await subscription.cancel();
      },
    );

    test(
      'Property knownHostStatesChanges reports online when active pairing creates a Known Host',
      () async {
        const String hostId = 'aaaaaaaa-aaaa-4aaa-8aaa-aaaaaaaaaaaa';
        final InMemoryClientStorage runtimeStorage = InMemoryClientStorage();
        final FakeDovahLinkTransport runtimeTransport =
            FakeDovahLinkTransport();
        final DovahLinkClient runtimeClient = buildDovahLinkClientForTesting(
          transport: runtimeTransport,
          storage: runtimeStorage,
        );
        final List<List<DovahLinkKnownHostState>> snapshots = [];
        final StreamSubscription<List<DovahLinkKnownHostState>> subscription =
            runtimeClient.hosts.knownHostStatesChanges.listen(snapshots.add);
        final JsonMap candidateHelloAck =
            jsonDecode(_rawFixture('connection/hello-ack-paired.json'))
                as JsonMap;
        final JsonMap candidatePayload =
            candidateHelloAck['payload'] as JsonMap;
        candidatePayload['hostId'] = hostId;
        candidatePayload['clientIdentityKind'] = 'unpaired';
        runtimeTransport.queueResponse(jsonEncode(candidateHelloAck));
        runtimeTransport.queueResponse(
          _rawFixture('capabilities/capabilities-host.json'),
        );
        await Future<void>.delayed(Duration.zero);

        await runtimeClient.authenticateCandidate(
          Uri.parse('ws://127.0.0.1:58232/'),
        );
        await Future<void>.delayed(Duration.zero);
        expect(snapshots, <List<DovahLinkKnownHostState>>[
          const <DovahLinkKnownHostState>[],
        ]);

        final JsonMap pairingOutcome =
            jsonDecode(
                  _rawFixture('pairing/pairing-outcome-credential-issued.json'),
                )
                as JsonMap;
        pairingOutcome['sessionId'] = 'session-paired-1';
        runtimeTransport.queueResponse(jsonEncode(pairingOutcome));
        runtimeTransport.queueResponse(
          _rawFixture('pairing/pairing-outcome-trusted.json'),
        );
        await runtimeClient.confirmPairingCode(code: '123456');
        await Future<void>.delayed(Duration.zero);

        expect(snapshots.last, <DovahLinkKnownHostState>[
          Fixtures.buildDovahLinkKnownHostState(
            host: Fixtures.buildDovahLinkHost(
              hostId: hostId,
              hostName: 'Soneka-Desktop',
              endpoint: 'ws://127.0.0.1:58232/',
            ),
            availability: DovahLinkHostAvailability.online,
            sessionState: DovahLinkKnownHostSessionState.connected,
          ),
        ]);
        await runtimeClient.disconnect();
        await Future<void>.delayed(Duration.zero);
        expect(
          snapshots.last.single.availability,
          DovahLinkHostAvailability.online,
        );
        expect(
          snapshots.last.single.sessionState,
          DovahLinkKnownHostSessionState.disconnected,
        );
        await subscription.cancel();
      },
    );

    test(
      'Property knownHostStatesChanges ignores a candidate claiming an existing Known Host ID',
      () async {
        const String hostId = '81869993-955c-4ba3-a7d0-d35ca86078ea';
        final InMemoryClientStorage runtimeStorage = InMemoryClientStorage();
        await runtimeStorage.save(
          _persistedState(
            clientId: 'client-1',
            credential: 'known-host-credential',
            knownHost: Fixtures.buildDovahLinkHost(hostId: hostId),
          ),
        );
        final FakeDovahLinkTransport runtimeTransport =
            FakeDovahLinkTransport();
        final DovahLinkClient runtimeClient = buildDovahLinkClientForTesting(
          transport: runtimeTransport,
          storage: runtimeStorage,
        );
        final List<List<DovahLinkKnownHostState>> snapshots = [];
        final StreamSubscription<List<DovahLinkKnownHostState>> subscription =
            runtimeClient.hosts.knownHostStatesChanges.listen(snapshots.add);
        final JsonMap response =
            jsonDecode(_rawFixture('connection/hello-ack-paired.json'))
                as JsonMap;
        (response['payload'] as JsonMap)['clientIdentityKind'] = 'unpaired';
        runtimeTransport.queueResponse(jsonEncode(response));
        runtimeTransport.queueResponse(
          _rawFixture('capabilities/capabilities-host.json'),
        );
        await Future<void>.delayed(Duration.zero);

        await runtimeClient.authenticateCandidate(
          Uri.parse('ws://127.0.0.1:58232/'),
        );
        await Future<void>.delayed(Duration.zero);

        expect(snapshots, hasLength(1));
        expect(
          snapshots.single.single.availability,
          DovahLinkHostAvailability.unknown,
        );
        await runtimeClient.disconnect();
        await Future<void>.delayed(Duration.zero);
        expect(snapshots, hasLength(1));
        await subscription.cancel();
      },
    );
  });

  group('Property character state streams behave correctly', () {
    test(
      'Property character state streams replay notSubscribed views',
      () async {
        expect(
          (await client.characterXpChanges.first).status,
          DovahLinkStateStatus.notSubscribed,
        );
        expect(
          (await client.characterHealthChanges.first).status,
          DovahLinkStateStatus.notSubscribed,
        );
        expect(
          (await client.characterMagickaChanges.first).status,
          DovahLinkStateStatus.notSubscribed,
        );
        expect(
          (await client.characterStaminaChanges.first).status,
          DovahLinkStateStatus.notSubscribed,
        );
        expect(
          (await client.characterLevelChanges.first).status,
          DovahLinkStateStatus.notSubscribed,
        );
      },
    );

    test(
      'Property character state streams receive typed Snapshots, level Events, and recovery updates',
      () async {
        await _connectAndTrustedHello(transport, client, storage);
        await _subscribeStateAreas(
          transport,
          client,
          DovahLinkStateArea.values,
        );

        final Future<void> experienceReceived = expectLater(
          client.characterXpChanges,
          emitsThrough(
            predicate<StateSynchronization<CharacterXpState>>(
              (StateSynchronization<CharacterXpState> state) =>
                  state.status == DovahLinkStateStatus.synchronized &&
                  state.value?.value == 42.5,
            ),
          ),
        );
        final Future<void> healthReceived = expectLater(
          client.characterHealthChanges,
          emitsThrough(
            predicate<StateSynchronization<CharacterHealthState>>(
              (StateSynchronization<CharacterHealthState> state) =>
                  state.status == DovahLinkStateStatus.unavailable &&
                  state.value?.value == null,
            ),
          ),
        );
        final Future<void> magickaReceived = expectLater(
          client.characterMagickaChanges,
          emitsThrough(
            predicate<StateSynchronization<CharacterMagickaState>>(
              (StateSynchronization<CharacterMagickaState> state) =>
                  state.status == DovahLinkStateStatus.synchronized &&
                  state.value?.value == 31.25,
            ),
          ),
        );
        final Future<void> staminaReceived = expectLater(
          client.characterStaminaChanges,
          emitsThrough(
            predicate<StateSynchronization<CharacterStaminaState>>(
              (StateSynchronization<CharacterStaminaState> state) =>
                  state.status == DovahLinkStateStatus.synchronized &&
                  state.value?.value == 15.0,
            ),
          ),
        );
        final Future<void> levelReceived = expectLater(
          client.characterLevelChanges,
          emitsThrough(
            predicate<StateSynchronization<CharacterLevelState>>(
              (StateSynchronization<CharacterLevelState> state) =>
                  state.status == DovahLinkStateStatus.synchronized &&
                  state.value?.value == 10,
            ),
          ),
        );

        transport.queueRawResponse(
          _rawStateSnapshot(
            stateArea: 'character_xp',
            revision: 1,
            value: 42.5,
          ),
        );
        transport.queueRawResponse(
          _rawStateSnapshot(
            stateArea: 'character_health',
            revision: 1,
            value: null,
          ),
        );
        transport.queueRawResponse(
          _rawStateSnapshot(
            stateArea: 'character_magicka',
            revision: 1,
            value: 31.25,
          ),
        );
        transport.queueRawResponse(
          _rawStateSnapshot(
            stateArea: 'character_stamina',
            revision: 1,
            value: 15,
          ),
        );
        transport.queueRawResponse(
          _rawStateSnapshot(
            stateArea: 'character_level',
            revision: 1,
            value: 10,
          ),
        );

        await Future.wait<void>(<Future<void>>[
          experienceReceived,
          healthReceived,
          magickaReceived,
          staminaReceived,
          levelReceived,
        ]);

        final Future<void> levelEventReceived = expectLater(
          client.characterLevelChanges,
          emitsThrough(
            predicate<StateSynchronization<CharacterLevelState>>(
              (StateSynchronization<CharacterLevelState> state) =>
                  state.status == DovahLinkStateStatus.synchronized &&
                  state.revision == 2 &&
                  state.value?.value == 11,
            ),
          ),
        );
        transport.queueRawResponse(
          _rawStateEvent(
            stateArea: 'character_level',
            baseRevision: 1,
            revision: 2,
            value: 11,
          ),
        );
        await levelEventReceived;

        final Future<void> recoveredLevelReceived = expectLater(
          client.characterLevelChanges,
          emitsThrough(
            predicate<StateSynchronization<CharacterLevelState>>(
              (StateSynchronization<CharacterLevelState> state) =>
                  state.status == DovahLinkStateStatus.synchronized &&
                  state.revision == 5 &&
                  state.value?.value == 14,
            ),
          ),
        );
        transport.queueResponse(
          _rawStateSnapshot(
            stateArea: 'character_level',
            revision: 5,
            value: 14,
            correlationId: 'snapshot-request-placeholder',
          ),
        );
        transport.queueRawResponse(
          _rawStateEvent(
            stateArea: 'character_level',
            baseRevision: 4,
            revision: 5,
            value: 14,
          ),
        );
        await recoveredLevelReceived;

        final JsonMap snapshotRequest =
            jsonDecode(transport.sent.last) as JsonMap;
        expect(snapshotRequest['messageType'], 'snapshot_request');
        expect(snapshotRequest['payload'], <String, dynamic>{
          'stateArea': 'character_level',
          'knownRevision': 2,
        });

        final Future<void> correlatedBaselineReceived = expectLater(
          client.characterLevelChanges,
          emitsThrough(
            predicate<StateSynchronization<CharacterLevelState>>(
              (StateSynchronization<CharacterLevelState> state) =>
                  state.status == DovahLinkStateStatus.synchronized &&
                  state.revision == 6 &&
                  state.value?.value == 15,
            ),
          ),
        );
        transport.queueRawResponse(
          _rawStateSnapshot(
            stateArea: 'character_level',
            revision: 6,
            value: 15,
            correlationId: snapshotRequest['messageId'] as String,
          ),
        );

        await correlatedBaselineReceived;
        expect(client.connectionState, DovahLinkConnectionState.connected);

        final Future<void> unavailableRecoveryReceived = expectLater(
          client.characterLevelChanges,
          emitsThrough(
            predicate<StateSynchronization<CharacterLevelState>>(
              (StateSynchronization<CharacterLevelState> state) =>
                  state.status == DovahLinkStateStatus.unavailable &&
                  state.revision == 9 &&
                  state.value?.value == null,
            ),
          ),
        );
        transport.queueResponse(
          _rawStateSnapshot(
            stateArea: 'character_level',
            revision: 9,
            value: null,
            correlationId: 'snapshot-request-placeholder',
          ),
        );
        transport.queueRawResponse(
          _rawStateEvent(
            stateArea: 'character_level',
            baseRevision: 8,
            revision: 9,
            value: 20,
          ),
        );

        await unavailableRecoveryReceived;
        final JsonMap unavailableRecoveryRequest =
            jsonDecode(transport.sent.last) as JsonMap;
        expect(unavailableRecoveryRequest['messageType'], 'snapshot_request');
        expect(unavailableRecoveryRequest['payload'], <String, dynamic>{
          'stateArea': 'character_level',
          'knownRevision': 6,
        });
        expect(client.connectionState, DovahLinkConnectionState.connected);
      },
    );
  });

  group(
    'Methods subscribeStateArea and unsubscribeStateArea behave correctly',
    () {
      test(
        'Method subscribeStateArea keeps Host-rejected domains inactive',
        () async {
          await _connectAndTrustedHello(transport, client, storage);
          transport.queueResponse(
            _rawSubscriptionAck(
              accepted: const <String>[],
              rejected: <String>['character_xp'],
            ),
          );

          expect(
            await client.subscribeStateArea(DovahLinkStateArea.characterXp),
            <DovahLinkStateArea>{DovahLinkStateArea.characterXp},
          );
          transport.queueRawResponse(
            _rawStateSnapshot(
              stateArea: 'character_xp',
              revision: 1,
              value: 42.5,
            ),
          );
          await pumpEventQueue();

          expect(
            (await client.characterXpChanges.first).status,
            DovahLinkStateStatus.notSubscribed,
          );
          expect(client.connectionState, DovahLinkConnectionState.connected);
        },
      );

      test(
        'Methods send the complete desired set and stop applying removed areas',
        () async {
          await _connectAndTrustedHello(transport, client, storage);

          transport.queueResponse(
            _rawSubscriptionAck(accepted: <String>['character_xp']),
          );
          expect(
            await client.subscribeStateArea(DovahLinkStateArea.characterXp),
            isEmpty,
          );
          expect(
            (jsonDecode(transport.sent.last) as JsonMap)['payload'],
            (jsonDecode(_rawFixture('subscriptions/subscribe.json'))
                as JsonMap)['payload'],
          );
          expect(
            (await client.characterXpChanges.first).status,
            DovahLinkStateStatus.recovering,
          );

          transport.queueResponse(
            _rawSubscriptionAck(
              accepted: <String>['character_xp', 'character_health'],
            ),
          );
          expect(
            await client.subscribeStateArea(DovahLinkStateArea.characterHealth),
            isEmpty,
          );
          expect(
            (jsonDecode(transport.sent.last) as JsonMap)['payload'],
            (jsonDecode(_rawFixture('subscriptions/subscribe-add-area.json'))
                as JsonMap)['payload'],
          );

          transport.queueRawResponse(
            _rawStateSnapshot(
              stateArea: 'character_xp',
              revision: 1,
              value: 42.5,
            ),
          );
          transport.queueRawResponse(
            _rawStateSnapshot(
              stateArea: 'character_health',
              revision: 1,
              value: 100,
            ),
          );
          await pumpEventQueue();
          expect(
            (await client.characterXpChanges.first).status,
            DovahLinkStateStatus.synchronized,
          );
          expect(
            (await client.characterHealthChanges.first).status,
            DovahLinkStateStatus.synchronized,
          );

          transport.queueResponse(
            _rawSubscriptionAck(accepted: <String>['character_health']),
          );
          expect(
            await client.unsubscribeStateArea(DovahLinkStateArea.characterXp),
            isEmpty,
          );
          expect(
            (jsonDecode(transport.sent.last) as JsonMap)['payload'],
            (jsonDecode(_rawFixture('subscriptions/subscribe-replacement.json'))
                as JsonMap)['payload'],
          );
          expect(
            (await client.characterXpChanges.first).status,
            DovahLinkStateStatus.notSubscribed,
          );

          transport.queueRawResponse(
            _rawStateSnapshot(
              stateArea: 'character_xp',
              revision: 2,
              value: 50,
            ),
          );
          transport.queueRawResponse(
            _rawStateEvent(
              stateArea: 'character_xp',
              baseRevision: 1,
              revision: 2,
              value: 50,
            ),
          );
          await pumpEventQueue();
          expect(
            (await client.characterXpChanges.first).status,
            DovahLinkStateStatus.notSubscribed,
          );
          expect(client.connectionState, DovahLinkConnectionState.connected);

          transport.queueResponse(
            _rawSubscriptionAck(accepted: const <String>[]),
          );
          expect(
            await client.unsubscribeStateArea(
              DovahLinkStateArea.characterHealth,
            ),
            isEmpty,
          );
          expect(
            (jsonDecode(transport.sent.last) as JsonMap)['payload'],
            (jsonDecode(_rawFixture('subscriptions/subscribe-empty.json'))
                as JsonMap)['payload'],
          );
          expect(
            (await client.characterHealthChanges.first).status,
            DovahLinkStateStatus.notSubscribed,
          );
        },
      );
    },
  );

  group('Behavior subscription session lifecycle behaves correctly', () {
    test(
      'Behavior explicit disconnect invalidates an ordinary-loss handoff still tearing down',
      () async {
        final FakeDovahLinkTransport reconnectTransport =
            FakeDovahLinkTransport();
        final InMemoryClientStorage reconnectStorage = InMemoryClientStorage();
        final DovahLinkClient reconnectClient = _buildFastReconnectClient(
          reconnectTransport,
          reconnectStorage,
        );
        final Completer<void> closeGate = Completer<void>();
        addTearDown(() async {
          if (!closeGate.isCompleted) {
            closeGate.complete();
          }
          await reconnectClient.disconnect();
        });
        reconnectTransport.closeGate = closeGate;
        await _connectAndTrustedHello(
          reconnectTransport,
          reconnectClient,
          reconnectStorage,
        );

        reconnectTransport.failMessagesWith(const SocketException('dropped'));
        for (
          int attempt = 0;
          attempt < 20 && !reconnectTransport.closeCalled;
          attempt++
        ) {
          await pumpEventQueue();
        }
        expect(reconnectTransport.closeCalled, isTrue);

        final Future<void> disconnect = reconnectClient.disconnect();
        closeGate.complete();
        await disconnect;
        for (int attempt = 0; attempt < 10; attempt++) {
          await pumpEventQueue();
        }

        expect(
          reconnectClient.connectionState,
          DovahLinkConnectionState.disconnected,
        );
        expect(reconnectTransport.connectCalls, hasLength(1));
      },
    );

    test(
      'Behavior later ordinary transport loss recovers after a new connection',
      () async {
        final FakeDovahLinkTransport reconnectTransport =
            FakeDovahLinkTransport();
        final InMemoryClientStorage reconnectStorage = InMemoryClientStorage();
        final DovahLinkClient reconnectClient = _buildFastReconnectClient(
          reconnectTransport,
          reconnectStorage,
        );
        addTearDown(reconnectClient.disconnect);
        await _connectAndTrustedHello(
          reconnectTransport,
          reconnectClient,
          reconnectStorage,
        );
        await reconnectClient.disconnect();
        await _connectAndTrustedHello(
          reconnectTransport,
          reconnectClient,
          reconnectStorage,
        );

        reconnectTransport.queueResponse(
          _rawFixture('connection/hello-ack-paired.json'),
        );
        reconnectTransport.queueResponse(
          _rawFixture('capabilities/capabilities-host.json'),
        );
        reconnectTransport.failMessagesWith(const SocketException('dropped'));

        for (
          int attempt = 0;
          attempt < 30 &&
              (reconnectTransport.connectCalls.length < 3 ||
                  reconnectClient.connectionState !=
                      DovahLinkConnectionState.connected);
          attempt++
        ) {
          await pumpEventQueue();
        }

        expect(reconnectTransport.connectCalls, hasLength(3));
        expect(
          reconnectClient.connectionState,
          DovahLinkConnectionState.connected,
        );
      },
    );

    test(
      'Behavior successful pending-pairing recovery restores remembered subscriptions',
      () async {
        await storage.save(
          Fixtures.buildPersistedClientState(
            clientId: 'client-1',
            credential: 'a1b2c3d4e5f6',
            recoveryState: PairingRecoveryState.confirming,
          ),
        );
        await _connectAndHello(transport, client);
        await expectLater(
          client.subscribeStateArea(DovahLinkStateArea.characterXp),
          throwsA(isA<DovahLinkConnectionException>()),
        );

        transport.queueResponse(
          _rawFixture('pairing/pairing-outcome-trusted.json'),
        );
        transport.queueResponse(
          _rawSubscriptionAck(
            accepted: <String>['character_xp'],
            sessionId: 'session-1',
          ),
        );
        expect(
          await client.recoverPendingPairing(),
          DovahLinkTrustState.trusted,
        );

        for (
          int attempt = 0;
          attempt < 20 && _sentSubscriptionUpdates(transport).isEmpty;
          attempt++
        ) {
          await pumpEventQueue();
        }
        expect(_sentSubscriptionUpdates(transport), hasLength(1));
        expect(
          _sentSubscriptionUpdates(transport).single['payload'],
          <String, dynamic>{
            'stateAreas': <String>['character_xp'],
          },
        );
      },
    );

    test(
      'Behavior ordinary reconnect restores only desired areas and waits for a fresh Snapshot',
      () async {
        final FakeDovahLinkTransport reconnectTransport =
            FakeDovahLinkTransport();
        final InMemoryClientStorage reconnectStorage = InMemoryClientStorage();
        final DovahLinkClient reconnectClient = _buildFastReconnectClient(
          reconnectTransport,
          reconnectStorage,
        );
        await _connectAndTrustedHello(
          reconnectTransport,
          reconnectClient,
          reconnectStorage,
        );
        await _subscribeStateAreas(
          reconnectTransport,
          reconnectClient,
          <DovahLinkStateArea>[
            DovahLinkStateArea.characterXp,
            DovahLinkStateArea.characterHealth,
          ],
        );

        reconnectTransport.queueResponse(
          _rawSubscriptionAck(accepted: <String>['character_health']),
        );
        await reconnectClient.unsubscribeStateArea(
          DovahLinkStateArea.characterXp,
        );
        reconnectTransport.queueRawResponse(
          _rawStateSnapshot(
            stateArea: 'character_health',
            revision: 7,
            value: 87.5,
          ),
        );
        await pumpEventQueue();
        expect(
          (await reconnectClient.characterHealthChanges.first).status,
          DovahLinkStateStatus.synchronized,
        );

        reconnectTransport.queueResponse(
          _rawFixture('connection/hello-ack-paired.json'),
        );
        reconnectTransport.queueResponse(
          _rawFixture('capabilities/capabilities-host.json'),
        );
        reconnectTransport.queueResponse(
          _rawSubscriptionAck(accepted: <String>['character_health']),
        );
        reconnectTransport.failMessagesWith(const SocketException('dropped'));

        for (
          int attempt = 0;
          attempt < 30 &&
              _sentSubscriptionUpdates(reconnectTransport).length < 4;
          attempt++
        ) {
          await pumpEventQueue();
        }

        final List<JsonMap> updates = _sentSubscriptionUpdates(
          reconnectTransport,
        );
        expect(updates, hasLength(4));
        expect(updates.last['payload'], <String, dynamic>{
          'stateAreas': <String>['character_health'],
        });
        expect(
          (await reconnectClient.characterHealthChanges.first).status,
          DovahLinkStateStatus.recovering,
        );
        expect(
          (await reconnectClient.characterXpChanges.first).status,
          DovahLinkStateStatus.notSubscribed,
        );

        reconnectTransport.queueRawResponse(
          _rawStateSnapshot(
            stateArea: 'character_health',
            revision: 1,
            value: 75,
            correlationId:
                _sentSubscriptionUpdates(reconnectTransport).last['messageId']
                    as String,
          ),
        );
        await pumpEventQueue();
        final StateSynchronization<CharacterHealthState> recovered =
            await reconnectClient.characterHealthChanges.first;
        expect(recovered.status, DovahLinkStateStatus.synchronized);
        expect(recovered.revision, 1);
        expect(recovered.value?.value, 75);
        expect(
          (await reconnectClient.characterXpChanges.first).status,
          DovahLinkStateStatus.notSubscribed,
        );
      },
    );

    test(
      'Behavior administrative invalidation stays dormant until explicit pairing succeeds',
      () async {
        await _connectAndTrustedHello(transport, client, storage);
        await _subscribeStateAreas(transport, client, <DovahLinkStateArea>[
          DovahLinkStateArea.characterXp,
        ]);
        transport.queueRawResponse(
          _rawStateSnapshot(
            stateArea: 'character_xp',
            revision: 1,
            value: 42.5,
          ),
        );
        await pumpEventQueue();
        expect(
          (await client.characterXpChanges.first).status,
          DovahLinkStateStatus.synchronized,
        );

        transport.queueRawResponse(
          _rawSessionInvalidated('revoked', sessionId: 'session-paired-1'),
        );
        transport.queueRawResponse(
          _rawStateSnapshot(stateArea: 'character_xp', revision: 2, value: 43),
        );
        transport.queueRawResponse(
          _rawStateEvent(
            stateArea: 'character_xp',
            baseRevision: 1,
            revision: 2,
            value: 43,
          ),
        );
        await pumpEventQueue();

        expect(
          client.connectionState,
          DovahLinkConnectionState.administrativelyInvalidated,
        );
        expect(transport.connectCalls, hasLength(1));
        expect(_sentSubscriptionUpdates(transport), hasLength(1));
        expect(
          (await client.characterXpChanges.first).status,
          DovahLinkStateStatus.notSubscribed,
        );

        transport.queueResponse(_rawFixture('connection/hello-ack.json'));
        transport.queueResponse(
          _rawFixture('capabilities/capabilities-host.json'),
        );
        final HelloResult retry = await client.authenticateCandidate(
          Uri.parse('ws://127.0.0.1:58231/'),
        );
        expect(retry.trustState, DovahLinkTrustState.unpaired);
        expect(_sentSubscriptionUpdates(transport), hasLength(1));

        transport.queueResponse(
          _rawFixture('pairing/pairing-status-available.json'),
        );
        await client.requestPairing();
        transport.queueResponse(
          _rawFixture('pairing/pairing-outcome-credential-issued.json'),
        );
        transport.queueResponse(
          _rawFixture('pairing/pairing-outcome-trusted.json'),
        );
        transport.queueResponse(
          _rawSubscriptionAck(
            accepted: <String>['character_xp'],
            sessionId: 'session-1',
          ),
        );
        await client.pairing.confirmCode(code: '123456', displayName: 'My PC');

        for (
          int attempt = 0;
          attempt < 20 && _sentSubscriptionUpdates(transport).length < 2;
          attempt++
        ) {
          await pumpEventQueue();
        }
        expect(_sentSubscriptionUpdates(transport), hasLength(2));
        final String restoredSubscriptionMessageId =
            _sentSubscriptionUpdates(transport).last['messageId'] as String;
        transport.queueRawResponse(
          _rawStateSnapshot(
            stateArea: 'character_xp',
            revision: 1,
            value: 42.5,
            correlationId: restoredSubscriptionMessageId,
          ),
        );
        await pumpEventQueue();
        expect(
          (await client.characterXpChanges.first).status,
          DovahLinkStateStatus.synchronized,
        );
        expect(client.trustState, DovahLinkTrustState.trusted);
      },
    );

    test(
      'Behavior intentional disconnect during reconnect clears intent before a later session',
      () async {
        final FakeDovahLinkTransport reconnectTransport =
            FakeDovahLinkTransport();
        final InMemoryClientStorage reconnectStorage = InMemoryClientStorage();
        final DovahLinkClient reconnectClient = buildDovahLinkClientForTesting(
          transport: reconnectTransport,
          storage: reconnectStorage,
          reconnectAttemptDelays: const <Duration>[
            Duration.zero,
            Duration(milliseconds: 200),
          ],
          reconnectDeadline: const Duration(seconds: 5),
        );
        await _connectAndTrustedHello(
          reconnectTransport,
          reconnectClient,
          reconnectStorage,
        );
        await _subscribeStateAreas(
          reconnectTransport,
          reconnectClient,
          <DovahLinkStateArea>[DovahLinkStateArea.characterXp],
        );
        reconnectTransport.failConnectWith = const SocketException(
          'Host remains unavailable',
        );
        reconnectTransport.failMessagesWith(const SocketException('dropped'));
        for (
          int attempt = 0;
          attempt < 20 &&
              reconnectClient.connectionState !=
                  DovahLinkConnectionState.reconnecting;
          attempt++
        ) {
          await pumpEventQueue();
        }
        expect(
          reconnectClient.connectionState,
          DovahLinkConnectionState.reconnecting,
        );

        await reconnectClient.disconnect();
        expect(
          reconnectClient.connectionState,
          DovahLinkConnectionState.disconnected,
        );
        final int connectCallsAfterDisconnect =
            reconnectTransport.connectCalls.length;
        await Future<void>.delayed(const Duration(milliseconds: 250));
        expect(
          reconnectTransport.connectCalls.length,
          connectCallsAfterDisconnect,
        );
        expect(
          (await reconnectClient.characterXpChanges.first).status,
          DovahLinkStateStatus.notSubscribed,
        );
        reconnectTransport.failConnectWith = null;
        await _connectAndTrustedHello(
          reconnectTransport,
          reconnectClient,
          reconnectStorage,
        );
        await pumpEventQueue();

        expect(_sentSubscriptionUpdates(reconnectTransport), hasLength(1));
      },
    );
  });

  group('Behavior default transport composition behaves correctly', () {
    test(
      'Behavior default transport composition connects and completes hello over '
      'WebSocket',
      () async {
        const Duration timeout = Duration(seconds: 5);
        final FakeWebSocketServer server = await FakeWebSocketServer.start()
            .timeout(timeout);
        addTearDown(server.close);

        final DovahLinkClient defaultClient = DovahLinkClient(
          storage: InMemoryClientStorage(),
        );
        addTearDown(defaultClient.close);
        addTearDown(defaultClient.disconnect);
        final Future<WebSocket> acceptedSocket = server.connections.first
            .timeout(timeout);

        await defaultClient.connect(server.uri).timeout(timeout);
        final WebSocket socket = await acceptedSocket;
        addTearDown(socket.close);

        final Completer<String> requestFrame = Completer<String>();
        socket.listen((Object? message) {
          if (message is String && !requestFrame.isCompleted) {
            requestFrame.complete(message);
          }
        });
        final Future<HelloResult> hello = defaultClient.hello().timeout(
          timeout,
        );
        final JsonMap request =
            jsonDecode(await requestFrame.future.timeout(timeout)) as JsonMap;
        final JsonMap helloAck =
            jsonDecode(_rawFixture('connection/hello-ack.json')) as JsonMap;
        helloAck['correlationId'] = request['messageId'];
        socket.add(jsonEncode(helloAck));
        socket.add(_rawFixture('capabilities/capabilities-host.json'));

        final HelloResult result = await hello;

        expect(
          defaultClient.connectionState,
          DovahLinkConnectionState.connected,
        );
        expect(result.hostVersion, '0.5.0');
        expect(result.hostId, '81869993-955c-4ba3-a7d0-d35ca86078ea');
        expect(result.hostName, 'Soneka-Desktop');
        expect(result.trustState, DovahLinkTrustState.unpaired);
      },
    );
  });

  group('Method enqueue behaves correctly', () {
    test('Method enqueue releases an uncorrelated reply immediately', () async {
      final StreamController<String> incoming = StreamController<String>();
      addTearDown(incoming.close);
      final PendingReplyQueue queue = PendingReplyQueue();

      queue.enqueue(PendingReply.immediate('reply'), incoming);

      expect(await incoming.stream.first, 'reply');
    });
  });

  group('Method releaseFor behaves correctly', () {
    test(
      'Method releaseFor rewrites one correlated reply with the request ID',
      () async {
        final StreamController<String> incoming = StreamController<String>();
        addTearDown(incoming.close);
        final PendingReplyQueue queue = PendingReplyQueue();
        queue.enqueue(
          PendingReply.correlated(<String, dynamic>{
            'correlationId': 'placeholder',
            'payload': <String, dynamic>{},
          }),
          incoming,
        );

        queue.releaseFor('request-1', incoming);

        expect(
          await incoming.stream.first,
          jsonEncode(<String, dynamic>{
            'correlationId': 'request-1',
            'payload': <String, dynamic>{},
          }),
        );
      },
    );

    test('Method releaseFor leaves a second correlated reply queued', () async {
      final StreamController<String> incoming = StreamController<String>();
      addTearDown(incoming.close);
      final PendingReplyQueue queue = PendingReplyQueue();
      final List<String> received = <String>[];
      incoming.stream.listen(received.add);
      queue.enqueue(
        PendingReply.correlated(<String, dynamic>{'correlationId': 'first'}),
        incoming,
      );
      queue.enqueue(
        PendingReply.correlated(<String, dynamic>{'correlationId': 'second'}),
        incoming,
      );

      queue.releaseFor('request-1', incoming);
      queue.releaseFor('request-2', incoming);
      await pumpEventQueue();

      expect(received, <String>[
        jsonEncode(<String, dynamic>{'correlationId': 'request-1'}),
        jsonEncode(<String, dynamic>{'correlationId': 'request-2'}),
      ]);
    });

    test(
      'Method releaseFor does not let an immediate reply jump a correlated reply',
      () async {
        final StreamController<String> incoming = StreamController<String>();
        addTearDown(incoming.close);
        final PendingReplyQueue queue = PendingReplyQueue();
        final List<String> received = <String>[];
        incoming.stream.listen(received.add);
        queue.enqueue(
          PendingReply.correlated(<String, dynamic>{'correlationId': 'first'}),
          incoming,
        );
        queue.enqueue(PendingReply.immediate('second'), incoming);
        await pumpEventQueue();

        expect(received, isEmpty);
        queue.releaseFor('request-1', incoming);
        await pumpEventQueue();

        expect(received, <String>[
          jsonEncode(<String, dynamic>{'correlationId': 'request-1'}),
          'second',
        ]);
      },
    );

    test(
      'Method releaseFor leaves a later reply available after an unanswered send',
      () async {
        final StreamController<String> incoming = StreamController<String>();
        addTearDown(incoming.close);
        final PendingReplyQueue queue = PendingReplyQueue();
        final List<String> received = <String>[];
        incoming.stream.listen(received.add);

        queue.releaseFor('unanswered-request', incoming);
        queue.enqueue(
          PendingReply.correlated(<String, dynamic>{'correlationId': 'later'}),
          incoming,
        );
        queue.releaseFor('next-request', incoming);
        await pumpEventQueue();

        expect(received, <String>[
          jsonEncode(<String, dynamic>{'correlationId': 'next-request'}),
        ]);
      },
    );
  });

  group('Method connect behaves correctly', () {
    test(
      'Method connect reaches connected state and forwards the URI to the transport',
      () async {
        final Uri uri = Uri.parse('ws://127.0.0.1:58231/');

        await client.connect(uri);

        expect(client.connectionState, DovahLinkConnectionState.connected);
        expect(transport.connectedUri, uri);
      },
    );
  });

  group('Method hello behaves correctly', () {
    test(
      'Method hello rejects a different Host during pending pairing recovery without changing persisted state',
      () async {
        const String knownHostId = '81869993-955c-4ba3-a7d0-d35ca86078ea';
        const String reportedHostId = 'aaaaaaaa-aaaa-4aaa-8aaa-aaaaaaaaaaaa';
        final PersistedClientState pendingState = _persistedState(
          clientId: 'client-1',
          credential: 'pending-credential',
          recoveryState: PairingRecoveryState.confirming,
          knownHost: DovahLinkHost(
            hostId: knownHostId,
            hostName: 'KNOWN-HOST',
            endpoint: Uri.parse('ws://127.0.0.1:58231/'),
          ),
        );
        await storage.save(pendingState);
        await client.connect(Uri.parse('ws://127.0.0.1:58231/'));
        final JsonMap helloAck =
            jsonDecode(_rawFixture('connection/hello-ack.json')) as JsonMap;
        (helloAck['payload'] as JsonMap)['hostId'] = reportedHostId;
        transport.queueResponse(jsonEncode(helloAck));

        await expectLater(
          client.hello(),
          throwsA(
            isA<DovahLinkHostIdentityMismatchException>()
                .having(
                  (error) => error.knownHostId,
                  'knownHostId',
                  knownHostId,
                )
                .having(
                  (error) => error.reportedHostId,
                  'reportedHostId',
                  reportedHostId,
                ),
          ),
        );

        expect(await storage.load(), pendingState);
        expect(client.connectionState, DovahLinkConnectionState.disconnected);
        expect(client.sessionId, isNull);
        expect(
          transport.sent
              .map(
                (String message) =>
                    (jsonDecode(message) as JsonMap)['messageType'],
              )
              .toList(),
          <String>['hello'],
        );
      },
    );

    test(
      'Method hello an unpaired hello (no stored credential) sets sessionId and trustState from the real fixtures',
      () async {
        await client.connect(Uri.parse('ws://127.0.0.1:58231/'));
        final String helloAckFixture = _rawFixture('connection/hello-ack.json');
        final JsonMap helloAckPayload =
            (jsonDecode(helloAckFixture) as JsonMap)['payload'] as JsonMap;
        transport.queueResponse(helloAckFixture);
        transport.queueResponse(
          _rawFixture('capabilities/capabilities-host.json'),
        );

        final HelloResult result = await client.hello();

        expect(result.hostVersion, helloAckPayload['hostVersion'] as String);
        expect(result.hostId, helloAckPayload['hostId'] as String);
        expect(result.hostName, helloAckPayload['hostName'] as String);
        expect(result.trustState, DovahLinkTrustState.unpaired);
        expect(client.trustState, DovahLinkTrustState.unpaired);
        expect(client.sessionId, 'session-1');
        // A successful hello must never trigger the failure-path cleanup.
        expect(transport.closeCalled, isFalse);

        final JsonMap sentPayload =
            (jsonDecode(transport.sent.single) as JsonMap)['payload']
                as JsonMap;
        expect(sentPayload['auth'], <String, dynamic>{'method': 'unpaired'});
      },
    );

    test(
      'Method hello a rejected hello throws DovahLinkProtocolException and leaves state unset',
      () async {
        await client.connect(Uri.parse('ws://127.0.0.1:58231/'));
        await storage.save(
          Fixtures.buildPersistedClientState(
            clientId: 'client-1',
            credential: 'deadbeef',
          ),
        );
        transport.queueResponse(
          _rawFixture('errors/error-unauthenticated-invalid-token.json'),
        );

        await expectLater(
          client.hello(),
          throwsA(
            isA<DovahLinkProtocolException>()
                .having(
                  (DovahLinkProtocolException e) => e.code,
                  'code',
                  ProtocolErrorCode.unauthenticated,
                )
                .having(
                  (DovahLinkProtocolException e) => e.retryable,
                  'retryable',
                  isFalse,
                ),
          ),
        );
        expect(client.trustState, isNull);
        expect(client.sessionId, isNull);
        // The host already closed this socket (every HandleHello failure path does); the
        // transport must be reset so the next connect() attempt does not find a stale socket
        // WebSocketTransport still considers open.
        expect(transport.closeCalled, isTrue);
      },
    );

    test(
      'Method hello disconnects and reports an incompatible Host before exposing a session',
      () async {
        await client.connect(Uri.parse('ws://127.0.0.1:58231/'));
        transport.queueResponse(
          jsonEncode(<String, dynamic>{
            'messageType': 'hello_ack',
            'messageId': 'message-hello-ack-1',
            'sessionId': 'session-1',
            'correlationId': 'irrelevant',
            'payload': <String, dynamic>{
              'hostVersion': '0.4.0',
              'hostId': '81869993-955c-4ba3-a7d0-d35ca86078ea',
              'hostName': 'Soneka-Desktop',
              'clientIdentityKind': 'paired',
            },
            'stateAuthorityId': 'state-authority-1',
            'playContextId': null,
            'clientId': 'client-1',
          }),
        );

        await expectLater(
          client.hello(),
          throwsA(
            isA<DovahLinkCompatibilityException>().having(
              (DovahLinkCompatibilityException error) => error.failure,
              'failure',
              HostVersionCompatibilityFailure.hostTooOld,
            ),
          ),
        );

        expect(client.connectionState, DovahLinkConnectionState.disconnected);
        expect(client.trustState, isNull);
        expect(client.sessionId, isNull);
        expect(transport.closeCalled, isTrue);
      },
    );

    test(
      'Method hello the original rejection still surfaces even when cleanup itself fails',
      () async {
        await client.connect(Uri.parse('ws://127.0.0.1:58231/'));
        await storage.save(
          Fixtures.buildPersistedClientState(
            clientId: 'client-1',
            credential: 'deadbeef',
          ),
        );
        transport.queueResponse(
          _rawFixture('errors/error-unauthenticated-invalid-token.json'),
        );
        transport.failCloseWith = const SocketException('socket already gone');

        await expectLater(
          client.hello(),
          throwsA(
            isA<DovahLinkProtocolException>().having(
              (DovahLinkProtocolException e) => e.code,
              'code',
              ProtocolErrorCode.unauthenticated,
            ),
          ),
        );
        // Cleanup was still attempted; its own failure must not replace the real error above.
        expect(transport.closeCalled, isTrue);
        expect(client.connectionState, DovahLinkConnectionState.disconnected);
        expect(client.trustState, isNull);
        expect(client.sessionId, isNull);
      },
    );

    test('Method hello a malformed message arriving after hello succeeds still resets session state, via the '
        'background receiver', () async {
      await client.connect(Uri.parse('ws://127.0.0.1:58231/'));
      // hello_ack resolves hello() as soon as it is correlated -- hello() does not wait for
      // capabilities. Queuing a malformed protocol message in its place proves the persistent receiver's own
      // cleanup covers state set moments earlier in this same call, not just the "never got
      // that far" case above -- even though it now runs after hello() has already returned.
      transport.queueResponse(_rawFixture('connection/hello-ack.json'));
      transport.queueResponse('not valid json');
      transport.failCloseWith = const SocketException('socket already gone');

      final HelloResult result = await client.hello();
      expect(result.trustState, DovahLinkTrustState.unpaired);

      await pumpEventQueue();

      expect(transport.closeCalled, isTrue);
      expect(client.connectionState, DovahLinkConnectionState.disconnected);
      expect(client.trustState, isNull);
      expect(client.sessionId, isNull);
    });

    test(
      'Method hello a freshly generated clientId is still persisted even when the hello_ack is rejected',
      () async {
        transport.queueResponse(
          _rawFixture('errors/error-unauthenticated-invalid-token.json'),
        );

        await expectLater(client.hello(), throwsA(isA<Exception>()));

        final PersistedClientState stored = await storage.load();
        expect(stored.clientId, isNotNull);
        expect(stored.clientId, client.clientId);
      },
    );

    test(
      'Method hello a malformed JSON response throws malformed_message',
      () async {
        await client.connect(Uri.parse('ws://127.0.0.1:58231/'));
        final Future<HelloResult> hello = client.hello();
        await pumpEventQueue();
        transport.queueResponse('not valid json');

        await expectLater(
          hello,
          throwsA(
            isA<DovahLinkProtocolException>().having(
              (DovahLinkProtocolException error) => error.code,
              'code',
              ProtocolErrorCode.malformedMessage,
            ),
          ),
        );
        // A transport-level failure leaves the socket just as unusable as a protocol rejection;
        // the reset must cover both, not only DovahLinkProtocolException.
        expect(transport.closeCalled, isTrue);
      },
    );
  });

  group('Method authenticate behaves correctly', () {
    test(
      'Method authenticateCandidate never selects a persisted Host credential',
      () async {
        final PersistedClientState knownState =
            Fixtures.buildPersistedClientState(
              clientId: 'client-1',
              credential: 'private-credential',
            );
        await storage.save(knownState);
        transport.queueResponse(_rawFixture('connection/hello-ack.json'));
        transport.queueResponse(
          _rawFixture('capabilities/capabilities-host.json'),
        );

        await client.authenticateCandidate(Uri.parse('ws://127.0.0.1:58232/'));

        final JsonMap sentPayload =
            (jsonDecode(transport.sent.first) as JsonMap)['payload'] as JsonMap;
        expect(sentPayload['auth'], <String, dynamic>{'method': 'unpaired'});
        expect(await storage.load(), knownState);
      },
    );

    test(
      'Method authenticate delegates to connect and hello when nothing is rejected',
      () async {
        transport.queueResponse(_rawFixture('connection/hello-ack.json'));
        transport.queueResponse(
          _rawFixture('capabilities/capabilities-host.json'),
        );

        final HelloResult result = await client.authenticateCandidate(
          Uri.parse('ws://127.0.0.1:58231/'),
        );

        expect(result.trustState, DovahLinkTrustState.unpaired);
        expect(result.recoveredFromRejectedCredential, isNull);
        expect(transport.connectedUri, Uri.parse('ws://127.0.0.1:58231/'));
      },
    );

    test(
      'Method authenticate does not reconnect when disconnect overlaps credential recovery',
      () async {
        final Completer<void> saveStarted = Completer<void>();
        final Completer<void> saveGate = Completer<void>();
        final TrackingClientStorage gatedStorage =
            TrackingClientStorage(
                Fixtures.buildPersistedClientState(
                  clientId: 'client-1',
                  credential: 'stale-credential',
                ),
              )
              ..saveStarted = saveStarted
              ..saveGate = saveGate;
        client = buildDovahLinkClientForTesting(
          transport: transport,
          storage: gatedStorage,
        );
        addTearDown(() async {
          if (!saveGate.isCompleted) {
            saveGate.complete();
          }
          await client.disconnect();
        });
        transport.queueResponse(_rawFixture('errors/error-revoked.json'));

        final Future<HelloResult> authentication = client.authenticateKnownHost(
          DovahLinkHostId('81869993-955c-4ba3-a7d0-d35ca86078ea'),
        );
        await saveStarted.future;
        await client.disconnect();
        saveGate.complete();

        await expectLater(
          authentication,
          throwsA(isA<DovahLinkConnectionException>()),
        );
        expect(transport.connectCalls, hasLength(1));
      },
    );

    test(
      'Method authenticate reconnects after disconnect even though the client was last trusted',
      () async {
        await storage.save(
          Fixtures.buildPersistedClientState(
            clientId: 'client-1',
            hostId: '81869993-955c-4ba3-a7d0-d35ca86078ea',
            credential: 'known-host-credential',
          ),
        );
        transport.queueResponse(
          jsonEncode(<String, dynamic>{
            'messageType': 'hello_ack',
            'messageId': 'message-hello-ack-1',
            'sessionId': 'session-1',
            'correlationId': 'irrelevant',
            'payload': <String, dynamic>{
              'hostVersion': '0.5.0',
              'hostId': '81869993-955c-4ba3-a7d0-d35ca86078ea',
              'hostName': 'Soneka-Desktop',
              'clientIdentityKind': 'paired',
            },
            'stateAuthorityId': 'state-authority-1',
            'playContextId': null,
            'clientId': 'client-1',
          }),
        );
        transport.queueResponse(
          _rawFixture('capabilities/capabilities-host.json'),
        );
        final DovahLinkHostId hostId = DovahLinkHostId(
          '81869993-955c-4ba3-a7d0-d35ca86078ea',
        );
        await client.authenticateKnownHost(hostId);
        await client.disconnect();

        transport.queueResponse(
          jsonEncode(<String, dynamic>{
            'messageType': 'hello_ack',
            'messageId': 'message-hello-ack-2',
            'sessionId': 'session-2',
            'correlationId': 'irrelevant',
            'payload': <String, dynamic>{
              'hostVersion': '0.5.0',
              'hostId': '81869993-955c-4ba3-a7d0-d35ca86078ea',
              'hostName': 'Soneka-Desktop',
              'clientIdentityKind': 'paired',
            },
            'stateAuthorityId': 'state-authority-1',
            'playContextId': null,
            'clientId': 'client-1',
          }),
        );
        transport.queueResponse(
          _rawFixture('capabilities/capabilities-host.json'),
        );
        await client.authenticateKnownHost(hostId);

        expect(transport.connectCalls, hasLength(2));
      },
    );
  });

  group('Method requestPairing behaves correctly', () {
    test(
      'Method requestPairing reports available with expiresInSeconds from the real fixture',
      () async {
        await _connectAndHello(transport, client);
        transport.queueResponse(
          _rawFixture('pairing/pairing-status-available.json'),
        );

        final PairingChallengeStatus status = await client.requestPairing();

        expect(status.availability, PairingAvailability.available);
        expect(status.expiresInSeconds, 300);
      },
    );

    test(
      'Method requestPairing stamps the resolved clientId on the outgoing envelope, unlike hello',
      () async {
        await _connectAndHello(transport, client);
        transport.queueResponse(
          _rawFixture('pairing/pairing-status-available.json'),
        );

        await client.requestPairing();

        expect(transport.sent, hasLength(2));
        final JsonMap helloEnvelope =
            jsonDecode(transport.sent.first) as JsonMap;
        final JsonMap pairingRequestEnvelope =
            jsonDecode(transport.sent.last) as JsonMap;
        expect(helloEnvelope['messageType'], 'hello');
        expect(helloEnvelope['clientId'], isNull);
        expect(pairingRequestEnvelope['messageType'], 'pairing_request');
        expect(pairingRequestEnvelope['clientId'], isNotNull);
        expect(pairingRequestEnvelope['clientId'], client.clientId);
      },
    );

    test('Method requestPairing a transport failure mid-request resets connection state, not just hello\'s, for a '
        'non-retry-safe operation', () async {
      // pairing_confirm is not retrySafe (unlike pairing_request): a send failure must fail it
      // immediately rather than parking it to retry after a reconnect -- see the "retry-safe
      // operations across reconnect" group below for the retrySafe counterpart of this case.
      await _connectAndHello(transport, client);
      transport.failSendWith = const SocketException('reset');

      await expectLater(
        client.confirmPairingCode(code: '123456'),
        throwsA(isA<DovahLinkConnectionException>()),
      );

      expect(transport.closeCalled, isTrue);
      // The send failure is ordinary transport loss, so SessionService hands off to bounded
      // automatic reconnect once teardown resolves to disconnected. Clear the injected send
      // failure first so the recovery attempt's own re-authentication does not fail the same way
      // and repeat the same hand-off, then deliberately disconnect before its delayed next
      // attempt runs, the same as the "already-orphaned operation" case below, so this test does
      // not leave a live background reconnect cycle behind it.
      transport.failSendWith = null;
      await pumpEventQueue();
      await client.disconnect();
      expect(client.connectionState, DovahLinkConnectionState.disconnected);
    });
  });

  group('Method requestPairingRenotify behaves correctly', () {
    test(
      'Method requestPairingRenotify reports renotified from the real fixture',
      () async {
        await _connectAndHello(transport, client);
        transport.queueResponse(
          _rawFixture('pairing/pairing-outcome-renotified.json'),
        );

        final PairingRenotifyResult result = await client
            .requestPairingRenotify();

        expect(result.status, PairingRenotifyStatus.renotified);
        expect(result.retryAfterSeconds, 5);
      },
    );
  });

  group('Method cancelPairing behaves correctly', () {
    test(
      'Method cancelPairing reports cancelled from the real fixture',
      () async {
        await _connectAndHello(transport, client);
        transport.queueResponse(
          _rawFixture('pairing/pairing-outcome-cancelled.json'),
        );

        final PairingCancelOutcome outcome = await client.cancelPairing();

        expect(outcome.status, PairingCancelStatus.cancelled);
      },
    );
  });

  group('Method confirmPairingCode behaves correctly', () {
    test(
      'Method confirmPairingCode persists its credential with Host-owned recovery',
      () async {
        await storage.save(
          Fixtures.buildPersistedClientState(clientId: 'client-1'),
        );
        await _connectAndHello(transport, client);
        final StreamIterator<List<DovahLinkHost>> changes = StreamIterator(
          client.hosts.knownHostsChanges,
        );
        addTearDown(changes.cancel);
        expect(await changes.moveNext(), isTrue);
        expect(changes.current, isEmpty);
        transport.queueResponse(
          _rawFixture('pairing/pairing-outcome-credential-issued.json'),
        );
        transport.queueResponse(
          _rawFixture('pairing/pairing-outcome-trusted.json'),
        );

        await client.confirmPairingCode(code: '123456', displayName: 'My PC');

        expect(await changes.moveNext(), isTrue);
        expect(changes.current, <DovahLinkHost>[
          DovahLinkHost(
            hostId: '81869993-955c-4ba3-a7d0-d35ca86078ea',
            hostName: 'Soneka-Desktop',
            endpoint: Uri.parse('ws://127.0.0.1:58231/'),
          ),
        ]);

        final PersistedClientState stored = await storage.load();
        expect(stored.clientId, 'client-1');
        expect(stored.knownHosts.values.single.credential, 'a1b2c3d4e5f6');
        expect(stored.pendingPairingRecovery, isNull);
        expect(client.trustState, DovahLinkTrustState.trusted);
        expect(
          stored.knownHosts.values.single.host,
          DovahLinkHost(
            hostId: '81869993-955c-4ba3-a7d0-d35ca86078ea',
            hostName: 'Soneka-Desktop',
            endpoint: Uri.parse('ws://127.0.0.1:58231/'),
          ),
        );
        expect(await client.hosts.loadKnownHosts(), <DovahLinkHost>[
          stored.knownHosts.values.single.host,
        ]);
      },
    );

    test(
      'Method confirmPairingCode keeps its Known Host and credential for a fresh client',
      () async {
        await storage.save(
          Fixtures.buildPersistedClientState(clientId: 'client-1'),
        );
        await _connectAndHello(transport, client);
        transport.queueResponse(
          _rawFixture('pairing/pairing-outcome-credential-issued.json'),
        );
        transport.queueResponse(
          _rawFixture('pairing/pairing-outcome-trusted.json'),
        );

        await client.confirmPairingCode(code: '123456', displayName: 'My PC');

        final PersistedClientState persisted = await storage.load();
        final DovahLinkHost host = persisted.knownHosts.values.single.host;
        final DovahLinkClient restoredClient = buildDovahLinkClientForTesting(
          transport: FakeDovahLinkTransport(),
          storage: storage,
        );

        expect(await restoredClient.hosts.loadKnownHosts(), <DovahLinkHost>[
          host,
        ]);
        expect(
          (await storage.load()).knownHosts[host.hostId]?.credential,
          'a1b2c3d4e5f6',
        );
        expect(
          await restoredClient.hosts.knownHostsChanges.first,
          <DovahLinkHost>[host],
        );
      },
    );

    test(
      'Method confirmPairingCode leaves a pre-existing CONFIRMING credential untouched when the outcome is a failure',
      () async {
        await storage.save(
          Fixtures.buildPersistedClientState(
            clientId: 'client-1',
            credential: 'already-confirming-credential',
            recoveryState: PairingRecoveryState.confirming,
          ),
        );
        await _connectAndHello(transport, client);
        transport.queueResponse(
          _rawFixture('pairing/pairing-outcome-expired.json'),
        );

        await expectLater(
          client.confirmPairingCode(code: '000000'),
          throwsA(isA<DovahLinkPairingException>()),
        );

        final PersistedClientState stored = await storage.load();
        expect(
          stored.knownHosts.values.single.credential,
          'already-confirming-credential',
        );
        expect(
          stored.pendingPairingRecovery?.state,
          PairingRecoveryState.confirming,
        );
      },
    );

    test(
      'Method confirmPairingCode exposes Host attempts remaining for an invalid code',
      () async {
        await storage.save(
          Fixtures.buildPersistedClientState(clientId: 'client-1'),
        );
        await _connectAndHello(transport, client);
        transport.queueResponse(
          _rawFixture('pairing/pairing-outcome-invalid.json'),
        );

        await expectLater(
          client.confirmPairingCode(code: '000000'),
          throwsA(
            isA<DovahLinkPairingException>().having(
              (DovahLinkPairingException error) => error.attemptsRemaining,
              'attemptsRemaining',
              4,
            ),
          ),
        );
      },
    );
  });

  group('Method recoverPendingPairing behaves correctly', () {
    test(
      'Method recoverPendingPairing leaves CONFIRMING untouched when the retry fails for another reason',
      () async {
        await storage.save(
          Fixtures.buildPersistedClientState(
            clientId: 'client-1',
            credential: 'a1b2c3d4e5f6',
            recoveryState: PairingRecoveryState.confirming,
          ),
        );
        await _connectAndHello(transport, client);
        transport.queueResponse('not valid json');

        await expectLater(
          client.recoverPendingPairing(),
          throwsA(
            isA<DovahLinkProtocolException>().having(
              (DovahLinkProtocolException error) => error.code,
              'code',
              ProtocolErrorCode.malformedMessage,
            ),
          ),
        );

        final PersistedClientState stored = await storage.load();
        expect(stored.knownHosts.values.single.credential, 'a1b2c3d4e5f6');
        expect(
          stored.pendingPairingRecovery?.state,
          PairingRecoveryState.confirming,
        );
      },
    );
  });

  group('Method disconnect behaves correctly', () {
    test(
      'Method disconnect closes the transport and resets session state',
      () async {
        await client.connect(Uri.parse('ws://127.0.0.1:58231/'));
        transport.queueResponse(_rawFixture('connection/hello-ack.json'));
        transport.queueResponse(
          _rawFixture('capabilities/capabilities-host.json'),
        );
        await client.hello();

        await client.disconnect();

        expect(transport.closeCalled, isTrue);
        expect(client.connectionState, DovahLinkConnectionState.disconnected);
        expect(client.trustState, isNull);
        expect(client.sessionId, isNull);
      },
    );

    test(
      'Method disconnect is observable through connectionStateChanges for an ordinary, '
      'non-administrative transition',
      () async {
        final List<DovahLinkConnectionState> observed =
            <DovahLinkConnectionState>[];
        final StreamSubscription<DovahLinkConnectionState> subscription = client
            .connectionStateChanges
            .listen(observed.add);
        addTearDown(subscription.cancel);

        await client.connect(Uri.parse('ws://127.0.0.1:58231/'));
        transport.queueResponse(_rawFixture('connection/hello-ack.json'));
        transport.queueResponse(
          _rawFixture('capabilities/capabilities-host.json'),
        );
        await client.hello();

        await client.disconnect();
        await pumpEventQueue();

        expect(observed, [
          DovahLinkConnectionState.disconnected,
          DovahLinkConnectionState.connecting,
          DovahLinkConnectionState.connected,
          DovahLinkConnectionState.disconnected,
        ]);
      },
    );

    test('Method disconnect preserves the persisted credential', () async {
      await storage.save(
        Fixtures.buildPersistedClientState(
          clientId: 'client-1',
          credential: 'credential-1',
        ),
      );
      await _connectAndTrustedHello(
        transport,
        client,
        storage,
        credential: 'credential-1',
      );

      await client.disconnect();

      final PersistedClientState stored = await storage.load();
      expect(stored.clientId, 'client-1');
      expect(stored.knownHosts.values.single.credential, 'credential-1');
    });
  });

  group('Method forgetCredential behaves correctly', () {
    test('Method forgetCredential preserves the observed Known Host', () async {
      final DovahLinkHost knownHost = DovahLinkHost(
        hostId: '81869993-955c-4ba3-a7d0-d35ca86078ea',
        hostName: 'KNOWN-HOST',
        endpoint: Uri.parse('ws://127.0.0.1:58231/'),
      );
      await storage.save(
        _persistedState(
          clientId: 'client-1',
          credential: 'credential-1',
          knownHost: knownHost,
        ),
      );
      final List<List<DovahLinkHost>> values = <List<DovahLinkHost>>[];
      final StreamSubscription<List<DovahLinkHost>> subscription = client
          .knownHostsChanges
          .listen(values.add);
      addTearDown(subscription.cancel);
      await pumpEventQueue();

      await client.forgetCredential(
        DovahLinkHostId('81869993-955c-4ba3-a7d0-d35ca86078ea'),
      );
      await pumpEventQueue();

      expect(values, <List<DovahLinkHost>>[
        <DovahLinkHost>[knownHost],
      ]);
      expect((await storage.load()).knownHosts.values.single.host, knownHost);
    });

    test(
      'Method forgetCredential a later hello presents unpaired instead of the forgotten credential',
      () async {
        await storage.save(
          Fixtures.buildPersistedClientState(
            clientId: 'client-1',
            credential: 'a1b2c3d4e5f6',
          ),
        );
        await client.forgetCredential(
          DovahLinkHostId('81869993-955c-4ba3-a7d0-d35ca86078ea'),
        );
        await client.connect(Uri.parse('ws://127.0.0.1:58231/'));
        transport.queueResponse(_rawFixture('connection/hello-ack.json'));
        transport.queueResponse(
          _rawFixture('capabilities/capabilities-host.json'),
        );

        await client.hello();

        final JsonMap sentPayload =
            (jsonDecode(transport.sent.single) as JsonMap)['payload']
                as JsonMap;
        expect(sentPayload['auth'], <String, dynamic>{'method': 'unpaired'});
      },
    );
  });

  group('Behavior inbound message routing behaves correctly', () {
    test(
      'Behavior inbound message routing gives sequential requests their own correlated replies',
      () async {
        await client.connect(Uri.parse('ws://127.0.0.1:58231/'));
        transport.queueResponse(_rawFixture('connection/hello-ack.json'));
        transport.queueResponse(
          _rawFixture('capabilities/capabilities-host.json'),
        );
        final HelloResult helloResult = await client.hello();
        expect(helloResult.trustState, DovahLinkTrustState.unpaired);

        transport.queueResponse(
          _rawFixture('pairing/pairing-status-available.json'),
        );
        final PairingChallengeStatus status = await client.requestPairing();

        expect(status.availability, PairingAvailability.available);
        expect(transport.sent, hasLength(2));
      },
    );

    test('Behavior inbound message routing does not consume an unsolicited message as a pending '
        'request reply', () async {
      await client.connect(Uri.parse('ws://127.0.0.1:58231/'));
      // capabilities is queued before hello-ack here, unlike every other test above, to prove
      // the router does not treat "whatever arrives first" as the pending operation's reply.
      transport.queueResponse(
        _rawFixture('capabilities/capabilities-host.json'),
      );
      transport.queueResponse(_rawFixture('connection/hello-ack.json'));

      final HelloResult result = await client.hello();

      expect(result.trustState, DovahLinkTrustState.unpaired);
    });

    test(
      'Behavior inbound message routing fails closed for an unmatched correlationId',
      () async {
        await client.connect(Uri.parse('ws://127.0.0.1:58231/'));
        final Future<HelloResult> helloFuture = client.hello();
        await pumpEventQueue();

        transport.queueRawResponse(
          jsonEncode(<String, dynamic>{
            'messageType': 'hello_ack',
            'messageId': 'message-hello-ack-1',
            'sessionId': 'session-1',
            'correlationId': 'no-such-pending-operation',
            'payload': <String, dynamic>{
              'hostVersion': '0.3.2',
              'hostId': '81869993-955c-4ba3-a7d0-d35ca86078ea',
              'hostName': 'Soneka-Desktop',
              'clientIdentityKind': 'unpaired',
            },
            'stateAuthorityId': 'state-authority-1',
            'playContextId': null,
            'clientId': 'client-1',
          }),
        );

        await expectLater(
          helloFuture,
          throwsA(
            isA<DovahLinkProtocolException>().having(
              (DovahLinkProtocolException e) => e.code,
              'code',
              ProtocolErrorCode.malformedMessage,
            ),
          ),
        );
        expect(client.connectionState, DovahLinkConnectionState.disconnected);
        expect(transport.closeCalled, isTrue);
      },
    );

    test(
      'Behavior inbound message routing contains malformed background JSON as a protocol failure',
      () async {
        final List<Object> uncaughtErrors = <Object>[];
        final Completer<void> done = Completer<void>();

        runZonedGuarded(() async {
          await client.connect(Uri.parse('ws://127.0.0.1:58231/'));
          transport.queueResponse(_rawFixture('connection/hello-ack.json'));
          await client.hello();

          transport.queueRawResponse('not valid json');
          await pumpEventQueue();
          done.complete();
        }, (Object error, StackTrace stackTrace) => uncaughtErrors.add(error));

        await done.future;

        expect(uncaughtErrors, isEmpty);
        expect(client.connectionState, DovahLinkConnectionState.disconnected);
        expect(transport.closeCalled, isTrue);
      },
    );
  });

  group('Behavior session_invalidated handling behaves correctly', () {
    test(
      'Behavior session_invalidated handling exposes the typed invalidationReason',
      () async {
        await client.connect(Uri.parse('ws://127.0.0.1:58231/'));
        transport.queueResponse(_rawFixture('connection/hello-ack.json'));
        transport.queueResponse(
          _rawFixture('capabilities/capabilities-host.json'),
        );
        await client.hello();

        transport.queueResponse(_rawSessionInvalidated('revoked'));
        await pumpEventQueue();

        expect(
          client.connectionState,
          DovahLinkConnectionState.administrativelyInvalidated,
        );
        expect(
          client.invalidationReason,
          AdministrativeInvalidationReason.revoked,
        );
      },
    );

    test('Behavior session_invalidated handling is observable through '
        'connectionStateChanges without waiting for another request', () async {
      final List<DovahLinkConnectionState> observed =
          <DovahLinkConnectionState>[];
      final StreamSubscription<DovahLinkConnectionState> subscription = client
          .connectionStateChanges
          .listen(observed.add);
      addTearDown(subscription.cancel);

      await client.connect(Uri.parse('ws://127.0.0.1:58231/'));
      transport.queueResponse(_rawFixture('connection/hello-ack.json'));
      transport.queueResponse(
        _rawFixture('capabilities/capabilities-host.json'),
      );
      await client.hello();

      transport.queueResponse(_rawSessionInvalidated('blocked'));
      await pumpEventQueue();

      expect(observed, [
        DovahLinkConnectionState.disconnected,
        DovahLinkConnectionState.connecting,
        DovahLinkConnectionState.connected,
        DovahLinkConnectionState.administrativelyInvalidated,
      ]);
    });

    for (final MapEntry<AdministrativeInvalidationReason, String> entry
        in _invalidationWireValues.entries) {
      test(
        'Behavior session_invalidated handling clears the persisted credential for ${entry.key.name}',
        () async {
          await storage.save(
            Fixtures.buildPersistedClientState(
              clientId: 'client-1',
              credential: 'credential-1',
            ),
          );
          await _connectAndTrustedHello(
            transport,
            client,
            storage,
            credential: 'credential-1',
          );

          transport.queueResponse(_rawSessionInvalidated(entry.value));
          await pumpEventQueue();

          final PersistedClientState stored = await storage.load();
          expect(stored.clientId, 'client-1');
          expect(stored.knownHosts.values.single.credential, isNull);
          expect(stored.pendingPairingRecovery, isNull);
        },
      );
    }

    test(
      'Behavior session_invalidated handling clears a confirming credential and recovery state',
      () async {
        await storage.save(
          Fixtures.buildPersistedClientState(
            clientId: 'client-1',
            credential: 'credential-1',
            recoveryState: PairingRecoveryState.confirming,
          ),
        );
        await _connectAndTrustedHello(
          transport,
          client,
          storage,
          credential: 'credential-1',
          recoveryState: PairingRecoveryState.confirming,
        );

        transport.queueResponse(_rawSessionInvalidated('revoked'));
        await pumpEventQueue();

        final PersistedClientState stored = await storage.load();
        expect(stored.clientId, 'client-1');
        expect(stored.knownHosts.values.single.credential, isNull);
        expect(stored.pendingPairingRecovery, isNull);
      },
    );

    test(
      'Behavior session_invalidated handling cleans up once when close signals are duplicated',
      () async {
        final TrackingClientStorage trackingStorage = TrackingClientStorage(
          Fixtures.buildPersistedClientState(
            clientId: 'client-1',
            credential: 'credential-1',
          ),
        );
        final FakeDovahLinkTransport trackingTransport =
            FakeDovahLinkTransport();
        final DovahLinkClient trackingClient = buildDovahLinkClientForTesting(
          transport: trackingTransport,
          storage: trackingStorage,
        );
        addTearDown(trackingClient.disconnect);

        await _connectAndTrustedHello(
          trackingTransport,
          trackingClient,
          trackingStorage,
          credential: 'credential-1',
        );

        trackingTransport.queueResponse(_rawSessionInvalidated('revoked'));
        trackingTransport.failMessagesWithBoth(
          const SocketException('closed by host'),
        );
        await pumpEventQueue();

        expect(trackingStorage.saveCount, 2);
        expect(
          (await trackingStorage.load()).knownHosts.values.single.credential,
          isNull,
        );
      },
    );

    test(
      'Behavior session_invalidated handling contains a persistence cleanup failure',
      () async {
        final TrackingClientStorage failingStorage = TrackingClientStorage(
          Fixtures.buildPersistedClientState(
            clientId: 'client-1',
            credential: 'credential-1',
          ),
        );
        final FakeDovahLinkTransport failingTransport =
            FakeDovahLinkTransport();
        final DovahLinkClient failingClient = buildDovahLinkClientForTesting(
          transport: failingTransport,
          storage: failingStorage,
        );
        addTearDown(failingClient.disconnect);

        await _connectAndTrustedHello(
          failingTransport,
          failingClient,
          failingStorage,
          credential: 'credential-1',
        );
        failingStorage.saveError = StateError('storage unavailable');

        final List<Object> uncaughtErrors = <Object>[];
        final Completer<void> done = Completer<void>();
        runZonedGuarded(() async {
          failingTransport.queueResponse(_rawSessionInvalidated('revoked'));
          await pumpEventQueue();
          done.complete();
        }, (Object error, StackTrace stackTrace) => uncaughtErrors.add(error));
        await done.future;

        expect(uncaughtErrors, isEmpty);
        expect(failingStorage.saveCount, 2);
        expect(
          (await failingStorage.load()).knownHosts.values.single.credential,
          'credential-1',
        );
      },
    );

    test(
      'Behavior session_invalidated handling fails a pending operation with a connection exception '
      'while it awaits a reply',
      () async {
        await client.connect(Uri.parse('ws://127.0.0.1:58231/'));
        transport.queueResponse(_rawFixture('connection/hello-ack.json'));
        transport.queueResponse(
          _rawFixture('capabilities/capabilities-host.json'),
        );
        await client.hello();

        final Future<PairingChallengeStatus> pending = client.requestPairing();
        await pumpEventQueue();
        transport.queueResponse(_rawSessionInvalidated('revoked'));

        await expectLater(
          pending,
          throwsA(isA<DovahLinkConnectionException>()),
        );
        expect(
          client.connectionState,
          DovahLinkConnectionState.administrativelyInvalidated,
        );
        expect(
          client.invalidationReason,
          AdministrativeInvalidationReason.revoked,
        );
      },
    );

    test(
      'Behavior session_invalidated handling fails closed when no session is authenticated',
      () async {
        await client.connect(Uri.parse('ws://127.0.0.1:58231/'));
        // hello() never ran -- no sessionId/trustState exists yet.
        transport.queueResponse(_rawSessionInvalidated('revoked'));
        await pumpEventQueue();

        expect(client.connectionState, DovahLinkConnectionState.disconnected);
        expect(client.invalidationReason, isNull);
        expect(transport.closeCalled, isTrue);
      },
    );

    test('Behavior session_invalidated handling preserves its typed reason during a transport '
        'failure race', () async {
      await client.connect(Uri.parse('ws://127.0.0.1:58231/'));
      transport.queueResponse(_rawFixture('connection/hello-ack.json'));
      transport.queueResponse(
        _rawFixture('capabilities/capabilities-host.json'),
      );
      await client.hello();

      // Both delivered on the same still-active subscription before either is processed,
      // simulating the host's own follow-up socket close racing this SDK's own
      // subscription-cancellation cleanup for session_invalidated.
      transport.queueResponse(_rawSessionInvalidated('blocked'));
      transport.failMessagesWith(const SocketException('closed by host'));
      await pumpEventQueue();

      expect(
        client.connectionState,
        DovahLinkConnectionState.administrativelyInvalidated,
      );
      expect(
        client.invalidationReason,
        AdministrativeInvalidationReason.blocked,
      );
    });
  });

  group('Behavior explicit retry after administrative invalidation behaves '
      'correctly', () {
    test('Behavior explicit retry after administrative invalidation never '
        'starts an automatic reconnect on its own', () async {
      await _connectAndHello(transport, client);

      transport.queueResponse(_rawSessionInvalidated('revoked'));
      await pumpEventQueue();

      expect(
        client.connectionState,
        DovahLinkConnectionState.administrativelyInvalidated,
      );
      expect(transport.connectCalls, hasLength(1));

      // Nothing further happens on its own: no automatic reconnect fires while this client
      // just sits invalidated.
      await pumpEventQueue();
      expect(
        client.connectionState,
        DovahLinkConnectionState.administrativelyInvalidated,
      );
      expect(transport.connectCalls, hasLength(1));
    });

    test('Behavior explicit retry after administrative invalidation succeeds '
        'and clears the typed reason', () async {
      await _connectAndHello(transport, client);
      final String? clientIdBeforeInvalidation = client.clientId;
      expect(clientIdBeforeInvalidation, isNotNull);

      transport.queueResponse(_rawSessionInvalidated('revoked'));
      await pumpEventQueue();
      expect(
        client.invalidationReason,
        AdministrativeInvalidationReason.revoked,
      );

      await client.connect(Uri.parse('ws://127.0.0.1:58231/'));
      transport.queueResponse(_rawFixture('connection/hello-ack.json'));
      transport.queueResponse(
        _rawFixture('capabilities/capabilities-host.json'),
      );
      final HelloResult result = await client.hello();

      expect(transport.connectCalls, hasLength(2));
      expect(result.trustState, DovahLinkTrustState.unpaired);
      expect(client.connectionState, DovahLinkConnectionState.connected);
      expect(client.invalidationReason, isNull);
      // The stable clientId survives explicit recovery; only the rejected credential was discarded.
      expect(client.clientId, clientIdBeforeInvalidation);
    });

    test('Behavior explicit retry after administrative invalidation leaves '
        'connectionState disconnected, not reverted to invalidated, when the '
        'retry attempt itself fails to connect', () async {
      await _connectAndHello(transport, client);

      transport.queueResponse(_rawSessionInvalidated('revoked'));
      await pumpEventQueue();

      transport.failConnectWith = const SocketException('still unreachable');
      await expectLater(
        client.connect(Uri.parse('ws://127.0.0.1:58231/')),
        throwsA(isA<DovahLinkConnectionException>()),
      );

      expect(client.connectionState, DovahLinkConnectionState.disconnected);
      expect(client.invalidationReason, isNull);
    });
  });

  group('Behavior retry-safe operations across reconnect behaves correctly', () {
    test(
      'Behavior retry-safe reconnect never replays a pending pairing_ack to a different Known Host',
      () async {
        const String knownHostId = '81869993-955c-4ba3-a7d0-d35ca86078ea';
        final PersistedClientState pendingState = _persistedState(
          clientId: 'client-1',
          credential: 'pending-credential',
          recoveryState: PairingRecoveryState.confirming,
          knownHost: DovahLinkHost(
            hostId: knownHostId,
            hostName: 'KNOWN-HOST',
            endpoint: Uri.parse('ws://127.0.0.1:58231/'),
          ),
        );
        final FakeDovahLinkTransport reconnectTransport =
            FakeDovahLinkTransport();
        final InMemoryClientStorage reconnectStorage = InMemoryClientStorage();
        final DovahLinkClient reconnectClient = _buildFastReconnectClient(
          reconnectTransport,
          reconnectStorage,
        );
        await reconnectStorage.save(pendingState);
        await _connectAndHello(reconnectTransport, reconnectClient);

        final Future<DovahLinkTrustState> pending = reconnectClient
            .recoverPendingPairing();
        final Future<void> pendingFails = expectLater(
          pending,
          throwsA(isA<DovahLinkHostIdentityMismatchException>()),
        );
        await pumpEventQueue();
        expect(reconnectTransport.sent, hasLength(2));

        final JsonMap helloAckB =
            jsonDecode(_rawFixture('connection/hello-ack.json')) as JsonMap;
        (helloAckB['payload'] as JsonMap)['hostId'] =
            'aaaaaaaa-aaaa-4aaa-8aaa-aaaaaaaaaaaa';
        reconnectTransport.queueResponse(jsonEncode(helloAckB));
        reconnectTransport.failMessagesWith(const SocketException('dropped'));

        for (
          int attempt = 0;
          attempt < 50 &&
              reconnectClient.connectionState !=
                  DovahLinkConnectionState.disconnected;
          attempt++
        ) {
          await pumpEventQueue();
        }
        await pendingFails;

        expect(reconnectTransport.connectCalls, hasLength(2));
        expect(reconnectTransport.sent, hasLength(3));
        expect(
          reconnectTransport.sent
              .map(
                (String message) =>
                    (jsonDecode(message) as JsonMap)['messageType'],
              )
              .toList(),
          <String>['hello', 'pairing_ack', 'hello'],
        );
        expect(
          reconnectClient.connectionState,
          DovahLinkConnectionState.disconnected,
        );
        expect(await reconnectStorage.load(), pendingState);
      },
    );

    test('Behavior retry-safe reconnect retransmits an orphaned operation and resolves its caller, '
        'via automatic reconnect', () async {
      await client.connect(Uri.parse('ws://127.0.0.1:58231/'));
      transport.queueResponse(_rawFixture('connection/hello-ack.json'));
      transport.queueResponse(
        _rawFixture('capabilities/capabilities-host.json'),
      );
      await client.hello();

      final Future<PairingChallengeStatus> pending = client.requestPairing();
      await pumpEventQueue();
      // Queued ahead of the drop so bounded automatic reconnect's own connect()+hello()+retry
      // finds them ready the moment it retries -- nothing in this test drives reconnect by hand.
      transport.queueResponse(_rawFixture('connection/hello-ack.json'));
      transport.queueResponse(
        _rawFixture('capabilities/capabilities-host.json'),
      );
      transport.queueResponse(
        _rawFixture('pairing/pairing-status-available.json'),
      );
      transport.failMessagesWith(const SocketException('dropped'));

      final PairingChallengeStatus status = await pending;
      expect(status.availability, PairingAvailability.available);
      expect(client.connectionState, DovahLinkConnectionState.connected);
      // hello#1, pairing_request#1 (orphaned), hello#2 (automatic), pairing_request#2 (the one
      // retry).
      expect(transport.sent, hasLength(4));
    });

    test('Behavior retry-safe reconnect rejects a paired claim without Known Host authentication, via '
        'automatic reconnect', () async {
      await client.connect(Uri.parse('ws://127.0.0.1:58231/'));
      transport.queueResponse(_rawFixture('connection/hello-ack.json'));
      transport.queueResponse(
        _rawFixture('capabilities/capabilities-host.json'),
      );
      await client.hello();

      final Future<PairingChallengeStatus> pending = client.requestPairing();
      await pumpEventQueue();
      // This unpaired candidate has no Known Host relationship. A later paired claim cannot
      // promote it or select a credential, so the orphaned request is never retried.
      transport.queueResponse(
        jsonEncode(<String, dynamic>{
          'messageType': 'hello_ack',
          'messageId': 'message-hello-ack-2',
          'sessionId': 'session-2',
          'correlationId': 'irrelevant',
          'payload': <String, dynamic>{
            'hostVersion': '0.5.0',
            'hostId': '81869993-955c-4ba3-a7d0-d35ca86078ea',
            'hostName': 'Soneka-Desktop',
            'clientIdentityKind': 'paired',
          },
          'stateAuthorityId': 'state-authority-1',
          'playContextId': null,
          'clientId': 'client-1',
        }),
      );
      transport.queueResponse(
        _rawFixture('capabilities/capabilities-host.json'),
      );
      transport.failMessagesWith(const SocketException('dropped'));

      await expectLater(
        pending,
        throwsA(
          isA<DovahLinkProtocolException>().having(
            (error) => error.code,
            'code',
            ProtocolErrorCode.malformedMessage,
          ),
        ),
      );
      expect(client.connectionState, DovahLinkConnectionState.disconnected);
      // hello#1, pairing_request#1 (orphaned, already sent before the drop), hello#2
      // (automatic) -- no pairing_request#2: the orphaned request was never retransmitted.
      expect(transport.sent, hasLength(3));
    });

    test(
      'Behavior retry-safe reconnect does not orphan a retried operation a second time',
      () async {
        await client.connect(Uri.parse('ws://127.0.0.1:58231/'));
        transport.queueResponse(_rawFixture('connection/hello-ack.json'));
        transport.queueResponse(
          _rawFixture('capabilities/capabilities-host.json'),
        );
        await client.hello();

        final Future<PairingChallengeStatus> pending = client.requestPairing();
        // Attached immediately, before this Future can possibly settle: Dart reports an error on
        // a Future that settles before anything is listening as unhandled, even if something
        // awaits it later.
        final Future<void> pendingFails = expectLater(
          pending,
          throwsA(isA<DovahLinkConnectionException>()),
        );
        await pumpEventQueue();
        // Queued ahead of the drop so automatic reconnect's own connect()+hello() finds them
        // ready, retransmitting the orphaned request as its one retry once the fresh session is
        // admitted.
        transport.queueResponse(_rawFixture('connection/hello-ack.json'));
        transport.queueResponse(
          _rawFixture('capabilities/capabilities-host.json'),
        );
        transport.failMessagesWith(const SocketException('dropped'));
        // Waits for the full automatic-reconnect cycle above -- connect, hello, admitSession, and
        // the resulting retransmit -- to actually finish before dropping the retry itself; a
        // single pumpEventQueue() does not reliably drain every hop in that chain. Bounded so a
        // genuine failure to reconnect fails this test instead of hanging it.
        for (int i = 0; i < 20 && transport.sent.length < 4; i++) {
          await pumpEventQueue();
        }
        expect(transport.sent, hasLength(4));
        // The retry itself now also drops, with no reply ever queued for it, so the next
        // automatic reconnect's own hello() succeeds but never resurrects the already-retried
        // operation.
        transport.queueResponse(_rawFixture('connection/hello-ack.json'));
        transport.queueResponse(
          _rawFixture('capabilities/capabilities-host.json'),
        );
        transport.failMessagesWith(const SocketException('dropped again'));

        await pendingFails;
        // Waits for automatic reconnect's own second cycle (triggered by the drop above) to
        // finish connecting and re-authenticating before asserting on its outcome, bounded so a
        // genuine failure to reconnect fails this test instead of hanging it.
        for (
          int i = 0;
          i < 20 &&
              client.connectionState != DovahLinkConnectionState.connected;
          i++
        ) {
          await pumpEventQueue();
        }
        expect(client.connectionState, DovahLinkConnectionState.connected);

        // A third connect/hello round must not resurrect it for a second retry.
        await client.disconnect();
        await client.connect(Uri.parse('ws://127.0.0.1:58231/'));
        transport.queueResponse(_rawFixture('connection/hello-ack.json'));
        transport.queueResponse(
          _rawFixture('capabilities/capabilities-host.json'),
        );
        await client.hello();

        // hello#1, pairing_request#1, hello#2(automatic), pairing_request#2(retry),
        // hello#3(automatic), hello#4(manual) -- no third pairing_request.
        expect(transport.sent, hasLength(6));
      },
    );

    test(
      'Behavior retry-safe reconnect fails a non-retry-safe operation immediately',
      () async {
        await client.connect(Uri.parse('ws://127.0.0.1:58231/'));
        transport.queueResponse(_rawFixture('connection/hello-ack.json'));
        transport.queueResponse(
          _rawFixture('capabilities/capabilities-host.json'),
        );
        await client.hello();

        final Future<void> pending = client.confirmPairingCode(code: '123456');
        await pumpEventQueue();
        transport.failMessagesWith(const SocketException('dropped'));

        await expectLater(
          pending,
          throwsA(isA<DovahLinkConnectionException>()),
        );
        // Cancels the automatic reconnect the drop above also started (this test is only about
        // the non-retry-safe operation's own immediate failure), so it cannot leak into a later
        // test's transport/client instances.
        await client.disconnect();
      },
    );
  });

  group('Behavior reconnect re-authentication sequencing behaves correctly', () {
    test(
      'Known Host reconnect preserves its availability while retrying and marks only that Host offline after exhaustion',
      () async {
        const String hostAId = '81869993-955c-4ba3-a7d0-d35ca86078ea';
        const String hostBId = '81f6cc90-3a88-40c7-8351-104d4a36c971';
        final DovahLinkHost hostA = Fixtures.buildDovahLinkHost(
          hostId: hostAId,
          hostName: 'Soneka-Desktop',
        );
        final DovahLinkHost hostB = Fixtures.buildDovahLinkHost(
          hostId: hostBId,
        );
        final PersistedClientState state = PersistedClientState(
          clientId: 'client-1',
          knownHosts: <String, PersistedKnownHost>{
            hostAId: PersistedKnownHost(
              host: hostA,
              credential: 'credential-a',
            ),
            hostBId: PersistedKnownHost(
              host: hostB,
              credential: 'credential-b',
            ),
          },
        );
        final InMemoryClientStorage reconnectStorage = InMemoryClientStorage();
        await reconnectStorage.save(state);
        final FakeDovahLinkTransport reconnectTransport =
            FakeDovahLinkTransport();
        final DovahLinkClient reconnectClient = _buildFastReconnectClient(
          reconnectTransport,
          reconnectStorage,
        );
        final List<List<DovahLinkKnownHostState>> snapshots = [];
        final StreamSubscription<List<DovahLinkKnownHostState>> subscription =
            reconnectClient.hosts.knownHostStatesChanges.listen(snapshots.add);
        reconnectTransport.queueResponse(
          _rawFixture('connection/hello-ack-paired.json'),
        );
        reconnectTransport.queueResponse(
          _rawFixture('capabilities/capabilities-host.json'),
        );
        await Future<void>.delayed(Duration.zero);

        await reconnectClient.authenticateKnownHost(DovahLinkHostId(hostAId));
        await Future<void>.delayed(Duration.zero);
        expect(snapshots.last, <DovahLinkKnownHostState>[
          Fixtures.buildDovahLinkKnownHostState(
            host: hostA,
            availability: DovahLinkHostAvailability.online,
            sessionState: DovahLinkKnownHostSessionState.connected,
          ),
          Fixtures.buildDovahLinkKnownHostState(host: hostB),
        ]);

        reconnectTransport.queueResponse(
          _rawFixture('connection/hello-ack-paired.json'),
        );
        reconnectTransport.queueResponse(
          _rawFixture('capabilities/capabilities-host.json'),
        );
        reconnectTransport.failMessagesWith(const SocketException('dropped'));
        for (int i = 0; i < 30; i++) {
          await pumpEventQueue();
        }

        expect(
          reconnectClient.connectionState,
          DovahLinkConnectionState.connected,
        );
        expect(
          snapshots.every((List<DovahLinkKnownHostState> snapshot) {
            return snapshot.first.availability !=
                DovahLinkHostAvailability.offline;
          }),
          isTrue,
        );
        expect(snapshots.last, <DovahLinkKnownHostState>[
          Fixtures.buildDovahLinkKnownHostState(
            host: hostA,
            availability: DovahLinkHostAvailability.online,
            sessionState: DovahLinkKnownHostSessionState.connected,
          ),
          Fixtures.buildDovahLinkKnownHostState(host: hostB),
        ]);

        reconnectTransport.failConnectWith = const SocketException(
          'unreachable',
        );
        reconnectTransport.failMessagesWith(const SocketException('dropped'));
        for (int i = 0; i < 40; i++) {
          await pumpEventQueue();
        }

        expect(
          reconnectClient.connectionState,
          DovahLinkConnectionState.disconnected,
        );
        expect(snapshots.last, <DovahLinkKnownHostState>[
          Fixtures.buildDovahLinkKnownHostState(
            host: hostA,
            availability: DovahLinkHostAvailability.offline,
          ),
          Fixtures.buildDovahLinkKnownHostState(host: hostB),
        ]);
        await subscription.cancel();
        await reconnectClient.disconnect();
      },
    );

    test(
      'Known Host reconnect identity mismatch returns that Host to unknown',
      () async {
        const String hostAId = '81869993-955c-4ba3-a7d0-d35ca86078ea';
        const String hostBId = '81f6cc90-3a88-40c7-8351-104d4a36c971';
        final DovahLinkHost hostA = Fixtures.buildDovahLinkHost(
          hostId: hostAId,
          hostName: 'Soneka-Desktop',
        );
        final DovahLinkHost hostB = Fixtures.buildDovahLinkHost(
          hostId: hostBId,
        );
        final InMemoryClientStorage reconnectStorage = InMemoryClientStorage();
        await reconnectStorage.save(
          PersistedClientState(
            clientId: 'client-1',
            knownHosts: <String, PersistedKnownHost>{
              hostAId: PersistedKnownHost(
                host: hostA,
                credential: 'credential-a',
              ),
              hostBId: PersistedKnownHost(
                host: hostB,
                credential: 'credential-b',
              ),
            },
          ),
        );
        final FakeDovahLinkTransport reconnectTransport =
            FakeDovahLinkTransport();
        final DovahLinkClient reconnectClient = _buildFastReconnectClient(
          reconnectTransport,
          reconnectStorage,
        );
        final List<List<DovahLinkKnownHostState>> snapshots = [];
        final StreamSubscription<List<DovahLinkKnownHostState>> subscription =
            reconnectClient.hosts.knownHostStatesChanges.listen(snapshots.add);

        reconnectTransport.queueResponse(
          _rawFixture('connection/hello-ack-paired.json'),
        );
        reconnectTransport.queueResponse(
          _rawFixture('capabilities/capabilities-host.json'),
        );
        await reconnectClient.authenticateKnownHost(DovahLinkHostId(hostAId));
        await Future<void>.delayed(Duration.zero);
        expect(
          snapshots.last.first.availability,
          DovahLinkHostAvailability.online,
        );

        final Map<String, dynamic> hostBHelloAck =
            jsonDecode(_rawFixture('connection/hello-ack-paired.json'))
                as Map<String, dynamic>;
        (hostBHelloAck['payload'] as Map<String, dynamic>)['hostId'] = hostBId;
        reconnectTransport.queueResponse(jsonEncode(hostBHelloAck));
        reconnectTransport.failMessagesWith(const SocketException('dropped'));

        for (
          int attempt = 0;
          attempt < 40 &&
              reconnectClient.connectionState !=
                  DovahLinkConnectionState.disconnected;
          attempt++
        ) {
          await pumpEventQueue();
        }

        expect(
          reconnectClient.connectionState,
          DovahLinkConnectionState.disconnected,
        );
        expect(snapshots.last, <DovahLinkKnownHostState>[
          Fixtures.buildDovahLinkKnownHostState(host: hostA),
          Fixtures.buildDovahLinkKnownHostState(host: hostB),
        ]);
        expect(
          snapshots.every(
            (List<DovahLinkKnownHostState> snapshot) =>
                snapshot.first.availability !=
                DovahLinkHostAvailability.offline,
          ),
          isTrue,
        );
        await subscription.cancel();
        await reconnectClient.disconnect();
      },
    );

    test('Behavior automatic reconnect passes through reauthenticating before resolving to '
        'connected, never exposing connected before hello succeeds', () async {
      final FakeDovahLinkTransport reconnectTransport =
          FakeDovahLinkTransport();
      final InMemoryClientStorage reconnectStorage = InMemoryClientStorage();
      final DovahLinkClient reconnectClient = _buildFastReconnectClient(
        reconnectTransport,
        reconnectStorage,
      );
      final List<DovahLinkConnectionState> observed =
          <DovahLinkConnectionState>[];
      final StreamSubscription<DovahLinkConnectionState> subscription =
          reconnectClient.connectionStateChanges.listen(observed.add);
      addTearDown(subscription.cancel);

      await reconnectClient.connect(Uri.parse('ws://127.0.0.1:58231/'));
      reconnectTransport.queueResponse(
        _rawFixture('connection/hello-ack.json'),
      );
      reconnectTransport.queueResponse(
        _rawFixture('capabilities/capabilities-host.json'),
      );
      await reconnectClient.hello();

      // Queued ahead of the drop so bounded automatic reconnect's own connect()+hello() finds
      // them ready the moment its first (zero-delay) attempt runs -- nothing in this test drives
      // reconnect by hand.
      reconnectTransport.queueResponse(
        _rawFixture('connection/hello-ack.json'),
      );
      reconnectTransport.queueResponse(
        _rawFixture('capabilities/capabilities-host.json'),
      );
      reconnectTransport.failMessagesWith(const SocketException('dropped'));

      // A fixed pump count, not a "not yet connected" condition -- connectionState is already
      // `connected` before the drop, so a condition guarding on that would exit before the drop
      // is ever actually processed. Reconnect delays are injected as zero for this composition
      // test, so no production-scale timer is required.
      await pumpEventQueue();
      for (int i = 0; i < 20; i++) {
        await pumpEventQueue();
      }

      expect(observed, [
        DovahLinkConnectionState.disconnected,
        DovahLinkConnectionState.connecting,
        DovahLinkConnectionState.connected,
        // Ordinary transport loss tears a fully connected session down to disconnected first
        // (it was not already recovering), then hands off to bounded automatic reconnect.
        DovahLinkConnectionState.disconnected,
        DovahLinkConnectionState.reconnecting,
        DovahLinkConnectionState.reauthenticating,
        DovahLinkConnectionState.connected,
      ]);
    });

    test('Behavior automatic reconnect continues after a retryable hello rejection instead of '
        'giving up, and still restores the session', () async {
      final FakeDovahLinkTransport reconnectTransport =
          FakeDovahLinkTransport();
      final InMemoryClientStorage reconnectStorage = InMemoryClientStorage();
      final DovahLinkClient reconnectClient = _buildFastReconnectClient(
        reconnectTransport,
        reconnectStorage,
      );
      await _connectAndHello(reconnectTransport, reconnectClient);

      //  Answers the first (zero-delay) automatic attempt's hello with a retryable rejection --
      // built inline, mirroring protocol/fixtures/errors/error-rate-limited.json, since that
      // canonical fixture's own correlationId is null (an unsolicited push shape) and this case
      // needs one correlated to the hello it rejects, the same way
      // errors/error-unauthenticated-invalid-token.json already is.
      reconnectTransport.queueResponse(
        jsonEncode(<String, dynamic>{
          'messageType': 'error',
          'messageId': 'message-error-rate-limited-retry-1',
          'sessionId': null,
          'correlationId': 'message-hello-placeholder',
          'payload': <String, dynamic>{
            'code': 'rate_limited',
            'message': 'Inbound message rate exceeded 100 messages per second',
            'retryable': true,
            'details': null,
          },
          'playContextId': null,
          'clientId': null,
        }),
      );
      // Answers the second automatic attempt with success.
      reconnectTransport.queueResponse(
        _rawFixture('connection/hello-ack.json'),
      );
      reconnectTransport.queueResponse(
        _rawFixture('capabilities/capabilities-host.json'),
      );
      reconnectTransport.failMessagesWith(const SocketException('dropped'));

      // A fixed pump count, not a "not yet connected" condition -- connectionState is already
      // `connected` from _connectAndHello before the drop, so a condition guarding on that would
      // exit before the drop is ever actually processed. Reconnect delays are injected as zero,
      // so the retry path does not wait on production-scale timers.
      await pumpEventQueue();
      for (int i = 0; i < 20; i++) {
        await pumpEventQueue();
      }

      expect(
        reconnectClient.connectionState,
        DovahLinkConnectionState.connected,
      );
      final List<String> helloSends = reconnectTransport.sent
          .where(
            (String raw) =>
                (jsonDecode(raw) as JsonMap)['messageType'] == 'hello',
          )
          .toList();
      // hello#1 (initial, from _connectAndHello), hello#2 (automatic, rejected retryably),
      // hello#3 (automatic, succeeds) -- the retryable rejection must consume one attempt and
      // continue, not end the cycle after hello#2.
      expect(helloSends, hasLength(3));
    });
  });

  group('Behavior credential cleanup during automatic reconnect behaves correctly', () {
    test(
      'Behavior automatic reconnect discards a credential the host rejects as blocked while '
      'recovering, preserving clientId and ending the cycle without retrying',
      () async {
        await storage.save(
          Fixtures.buildPersistedClientState(
            clientId: 'client-1',
            credential: 'stale-cred',
          ),
        );
        await _connectAndTrustedHello(
          transport,
          client,
          storage,
          credential: 'stale-cred',
        );

        // Answers the automatic reconnect's own hello -- the host decides, while this device
        // was briefly offline, that its presented credential is now blocked.
        transport.queueResponse(
          jsonEncode(<String, dynamic>{
            'messageType': 'error',
            'messageId': 'message-error-blocked-1',
            'sessionId': null,
            'correlationId': 'message-hello-placeholder',
            'payload': <String, dynamic>{
              'code': 'blocked',
              'message': 'This clientId is blocked',
              'retryable': false,
              'details': null,
            },
            'playContextId': null,
            'clientId': null,
          }),
        );
        transport.failMessagesWith(const SocketException('dropped'));

        for (int i = 0; i < 20; i++) {
          await pumpEventQueue();
        }

        expect(client.connectionState, DovahLinkConnectionState.disconnected);
        final PersistedClientState stored = await storage.load();
        expect(stored.knownHosts.values.single.credential, isNull);
        expect(stored.clientId, 'client-1');
        final List<String> helloSends = transport.sent
            .where(
              (String raw) =>
                  (jsonDecode(raw) as JsonMap)['messageType'] == 'hello',
            )
            .toList();
        // hello#1 (initial, succeeds) and hello#2 (automatic, rejected as blocked) -- a
        // terminal rejection must not consume the remaining attempt budget by retrying with
        // the now-forgotten credential.
        expect(helloSends, hasLength(2));
      },
    );
  });

  group('Behavior stale receiver isolation behaves correctly', () {
    test(
      'Behavior stale receiver isolation does not consume a late reply for a new operation',
      () async {
        await client.connect(Uri.parse('ws://127.0.0.1:58231/'));
        transport.queueResponse(_rawFixture('connection/hello-ack.json'));
        transport.queueResponse(
          _rawFixture('capabilities/capabilities-host.json'),
        );
        await client.hello();

        // confirmPairingCode (not retry-safe) rather than requestPairing here: the point of this
        // test is what happens to a stale reply arriving late for an old, dead generation, not
        // retry behavior -- a retry-safe first request would itself get auto-retried by the
        // second hello() below, which is exactly the mechanism the sibling group above already
        // covers and would confuse this test's own generation-isolation assertion.
        final Future<void> firstRequest = client.confirmPairingCode(
          code: '123456',
        );
        final Future<void> firstRequestFails = expectLater(
          firstRequest,
          throwsA(isA<DovahLinkConnectionException>()),
        );
        await pumpEventQueue();
        final String staleMessageId =
            (jsonDecode(transport.sent.last) as JsonMap)['messageId'] as String;
        transport.failMessagesWith(const SocketException('dropped'));
        await firstRequestFails;
        await pumpEventQueue();
        // Cancels whatever bounded automatic reconnect the drop above already started, so this
        // test regains explicit manual control of the next connect/hello cycle -- this test is
        // about stale-reply isolation across generations, not automatic recovery.
        await client.disconnect();

        await client.connect(Uri.parse('ws://127.0.0.1:58231/'));
        transport.queueResponse(_rawFixture('connection/hello-ack.json'));
        transport.queueResponse(
          _rawFixture('capabilities/capabilities-host.json'),
        );
        await client.hello();

        final Future<PairingChallengeStatus> secondRequest = client
            .requestPairing();
        final Future<void> secondRequestFails = expectLater(
          secondRequest,
          throwsA(isA<DovahLinkProtocolException>()),
        );
        await pumpEventQueue();

        // A reply correlated to the old, already-failed first request must not be mistaken for
        // the new one's reply -- it fails closed as an unmatched correlationId instead.
        transport.queueRawResponse(
          jsonEncode(<String, dynamic>{
            'messageType': 'pairing_status',
            'messageId': 'message-late-1',
            'sessionId': 'session-1',
            'correlationId': staleMessageId,
            'payload': <String, dynamic>{
              'state': 'available',
              'expiresInSeconds': 300,
            },
            'playContextId': null,
            'clientId': null,
          }),
        );

        await secondRequestFails;
      },
    );
  });

  group('Behavior request policy timeout handling behaves correctly', () {
    test(
      'Behavior request timeout handling surfaces DovahLinkConnectionException for a '
      'never-connected transport',
      () async {
        final DovahLinkClient realTransportClient = DovahLinkClient(
          storage: storage,
        );
        addTearDown(realTransportClient.close);

        await expectLater(
          realTransportClient.hello(),
          throwsA(isA<DovahLinkConnectionException>()),
        );
      },
    );

    test(
      'Behavior request timeout handling fails and disconnects after its timeout class duration',
      () async {
        final DovahLinkClient timeoutClient = buildDovahLinkClientForTesting(
          transport: transport,
          storage: storage,
          timeoutDurations: const <TimeoutClass, Duration>{
            TimeoutClass.short: Duration(milliseconds: 20),
            TimeoutClass.normal: Duration(milliseconds: 20),
            TimeoutClass.heavy: Duration(milliseconds: 20),
          },
        );

        // No reply is ever queued for hello -- it must time out rather than hang.
        await expectLater(
          timeoutClient.hello(),
          throwsA(isA<DovahLinkConnectionException>()),
        );
        expect(
          timeoutClient.connectionState,
          DovahLinkConnectionState.disconnected,
        );
      },
    );

    test('Behavior request timeout handling retransmits retry-safe pairing recovery '
        'after ordinary transport loss, via automatic reconnect', () async {
      await storage.save(
        Fixtures.buildPersistedClientState(
          clientId: 'client-1',
          credential: 'a1b2c3d4e5f6',
          recoveryState: PairingRecoveryState.confirming,
        ),
      );
      await client.connect(Uri.parse('ws://127.0.0.1:58231/'));
      transport.queueResponse(_rawFixture('connection/hello-ack.json'));
      transport.queueResponse(
        _rawFixture('capabilities/capabilities-host.json'),
      );
      await client.hello();

      final Future<DovahLinkTrustState> pending = client
          .recoverPendingPairing();
      final Future<void> pendingCompletes = expectLater(pending, completes);
      await pumpEventQueue();
      // Queued ahead of the drop so bounded automatic reconnect's own connect()+hello()+retry
      // finds them ready the moment it retries -- nothing in this test drives reconnect by hand.
      transport.queueResponse(_rawFixture('connection/hello-ack.json'));
      transport.queueResponse(
        _rawFixture('capabilities/capabilities-host.json'),
      );
      transport.queueResponse(
        _rawFixture('pairing/pairing-outcome-trusted.json'),
      );
      transport.failMessagesWith(const SocketException('dropped'));

      await pendingCompletes;
      expect(client.connectionState, DovahLinkConnectionState.connected);
      expect(client.trustState, DovahLinkTrustState.trusted);
    });

    test(
      'Behavior request timeout handling fails a pending retry-safe operation immediately after a '
      'protocol violation',
      () async {
        await client.connect(Uri.parse('ws://127.0.0.1:58231/'));
        transport.queueResponse(_rawFixture('connection/hello-ack.json'));
        transport.queueResponse(
          _rawFixture('capabilities/capabilities-host.json'),
        );
        await client.hello();

        final Future<PairingChallengeStatus> pending = client
            .requestPairing(); // retrySafe
        final Future<void> pendingFails = expectLater(
          pending,
          throwsA(isA<DovahLinkProtocolException>()),
        );
        await pumpEventQueue();

        transport.queueRawResponse(
          jsonEncode(<String, dynamic>{
            'messageType': 'pairing_status',
            'messageId': 'message-x',
            'sessionId': 'session-1',
            'correlationId': 'no-such-pending-operation',
            'payload': <String, dynamic>{
              'state': 'available',
              'expiresInSeconds': 300,
            },
            'playContextId': null,
            'clientId': null,
          }),
        );
        await pendingFails;

        // Confirmed not orphaned: a fresh connect/hello does not retransmit it.
        await client.connect(Uri.parse('ws://127.0.0.1:58231/'));
        transport.queueResponse(_rawFixture('connection/hello-ack.json'));
        transport.queueResponse(
          _rawFixture('capabilities/capabilities-host.json'),
        );
        await client.hello();
        expect(transport.sent, hasLength(3));
      },
    );

    test(
      'disconnect() also fails an already-orphaned operation, not just a currently pending one',
      () async {
        await client.connect(Uri.parse('ws://127.0.0.1:58231/'));
        transport.queueResponse(_rawFixture('connection/hello-ack.json'));
        transport.queueResponse(
          _rawFixture('capabilities/capabilities-host.json'),
        );
        await client.hello();

        final Future<PairingChallengeStatus> pending = client.requestPairing();
        final Future<void> pendingFails = expectLater(
          pending,
          throwsA(isA<DovahLinkConnectionException>()),
        );
        await pumpEventQueue();
        transport.failMessagesWith(const SocketException('dropped'));
        await pumpEventQueue();
        // Bounded automatic reconnect has already reconnected the transport (attempt 0 fires
        // immediately and this fake transport's connect() always succeeds) and is awaiting its
        // own re-authentication reply, never queued here -- so the session is reauthenticating,
        // not yet trusted, and the orphaned operation has not yet been retried; that only happens
        // once a fresh session is actually admitted.
        expect(
          client.connectionState,
          DovahLinkConnectionState.reauthenticating,
        );

        // Deliberate disconnect while automatic reconnect is still awaiting re-authentication and
        // has not yet retried the orphaned operation -- must not leave it hanging forever.
        await client.disconnect();

        await pendingFails;
      },
    );
  });

  group('Behavior composition-root teardown deduplication behaves correctly', () {
    test('Behavior composition-root teardown deduplication closes the transport exactly once for a '
        'duplicate onError/onDone signal on one dead connection', () async {
      // A real client composition must deduplicate a stream's onError and onDone signals for one
      // dead connection and close its transport once. Service tests isolate their collaborators;
      // this test covers the composed teardown path.
      await client.connect(Uri.parse('ws://127.0.0.1:58231/'));
      transport.queueResponse(_rawFixture('connection/hello-ack.json'));
      transport.queueResponse(
        _rawFixture('capabilities/capabilities-host.json'),
      );
      await client.hello();

      transport.failMessagesWithBoth(const SocketException('dropped'));
      await pumpEventQueue();

      expect(transport.closeCallCount, 1);
      // Ordinary transport loss with a known endpoint hands off to bounded automatic
      // reconnect; disconnect before its own delayed next attempt runs so this test does not
      // leave a live background reconnect cycle behind it, mirroring the established pattern
      // above.
      await client.disconnect();
    });
  });

  group('Behavior Known Host disconnect during recovery behaves correctly', () {
    test(
      'Behavior Known Host disconnect during reauthentication preserves presence and ignores a late hello reply',
      () async {
        const String hostId = '81869993-955c-4ba3-a7d0-d35ca86078ea';
        final InMemoryClientStorage reconnectStorage = InMemoryClientStorage();
        await reconnectStorage.save(
          _persistedState(
            clientId: 'client-1',
            credential: 'known-host-credential',
            knownHost: Fixtures.buildDovahLinkHost(
              hostId: hostId,
              hostName: 'Soneka-Desktop',
            ),
          ),
        );
        final FakeDovahLinkTransport reconnectTransport =
            FakeDovahLinkTransport();
        final DovahLinkClient reconnectClient = _buildFastReconnectClient(
          reconnectTransport,
          reconnectStorage,
        );
        final List<List<DovahLinkKnownHostState>> snapshots = [];
        final StreamSubscription<List<DovahLinkKnownHostState>> subscription =
            reconnectClient.hosts.knownHostStatesChanges.listen(snapshots.add);
        reconnectTransport.queueResponse(
          _rawFixture('connection/hello-ack-paired.json'),
        );
        reconnectTransport.queueResponse(
          _rawFixture('capabilities/capabilities-host.json'),
        );
        await Future<void>.delayed(Duration.zero);
        await reconnectClient.authenticateKnownHost(DovahLinkHostId(hostId));
        await Future<void>.delayed(Duration.zero);
        reconnectTransport.failMessagesWith(const SocketException('dropped'));
        for (int i = 0; i < 20; i++) {
          await pumpEventQueue();
        }
        expect(
          reconnectClient.connectionState,
          DovahLinkConnectionState.reauthenticating,
        );

        final int closeCountBeforeDisconnect =
            reconnectTransport.closeCallCount;
        final Completer<void> closeGate = Completer<void>();
        reconnectTransport.closeGate = closeGate;
        final Future<void> disconnect = reconnectClient.disconnect();
        for (
          int i = 0;
          i < 20 &&
              reconnectTransport.closeCallCount == closeCountBeforeDisconnect;
          i++
        ) {
          await pumpEventQueue();
        }
        expect(
          reconnectTransport.closeCallCount,
          greaterThan(closeCountBeforeDisconnect),
        );

        reconnectTransport.queueRawResponse(
          _rawFixture('connection/hello-ack-paired.json'),
        );
        closeGate.complete();
        await disconnect;
        for (int i = 0; i < 20; i++) {
          await pumpEventQueue();
        }

        expect(
          reconnectClient.connectionState,
          DovahLinkConnectionState.disconnected,
        );
        expect(snapshots.last, <DovahLinkKnownHostState>[
          Fixtures.buildDovahLinkKnownHostState(
            host: Fixtures.buildDovahLinkHost(
              hostId: hostId,
              hostName: 'Soneka-Desktop',
            ),
            availability: DovahLinkHostAvailability.online,
          ),
        ]);
        await subscription.cancel();
      },
    );
  });

  group('Behavior reconnect-disabled client composition behaves correctly', () {
    test(
      'Behavior reconnect-disabled client composition does not retry after transport loss',
      () async {
        final FakeDovahLinkTransport transport = FakeDovahLinkTransport();
        final DovahLinkClient client = buildDovahLinkClientForTesting(
          transport: transport,
          storage: InMemoryClientStorage(),
          reconnectEnabled: false,
        );
        addTearDown(client.disconnect);

        await client.connect(Uri.parse('ws://127.0.0.1:58231/'));
        transport.failMessagesWith(const SocketException('dropped'));
        for (
          int attempt = 0;
          attempt < 20 && !transport.closeCalled;
          attempt++
        ) {
          await pumpEventQueue();
        }
        await pumpEventQueue();

        expect(client.connectionState, DovahLinkConnectionState.disconnected);
        expect(transport.connectCalls, hasLength(1));
      },
    );
  });

  group(
    'Behavior Known Host presence monitoring lifecycle behaves correctly',
    () {
      test(
        'Behavior Known Host presence monitoring probes restored Hosts and survives disconnect until close',
        () async {
          final DovahLinkHost host = Fixtures.buildDovahLinkHost(
            hostId: '81869993-955c-4ba3-a7d0-d35ca86078ea',
          );
          final InMemoryClientStorage monitorStorage = InMemoryClientStorage();
          await monitorStorage.save(
            Fixtures.buildPersistedClientState(host: host),
          );
          final FakeDovahLinkTransport monitorTransport =
              FakeDovahLinkTransport();
          final ControllableClientPresenceProbe presenceProbe =
              ControllableClientPresenceProbe();
          final StreamController<void> refreshTicks =
              StreamController<void>.broadcast(sync: true);
          final DovahLinkClient monitorClient = buildDovahLinkClientForTesting(
            transport: monitorTransport,
            storage: monitorStorage,
            hostPresenceProbe: presenceProbe,
            knownHostPresenceMonitoringEnabled: true,
            hostPresenceRefreshTicks: refreshTicks.stream,
          );
          final List<List<DovahLinkKnownHostState>> snapshots = [];
          final Completer<void> checkingObserved = Completer<void>();
          final StreamSubscription<List<DovahLinkKnownHostState>> subscription =
              monitorClient.hosts.knownHostStatesChanges.listen((
                List<DovahLinkKnownHostState> snapshot,
              ) {
                snapshots.add(snapshot);
                if (snapshot.isNotEmpty &&
                    snapshot.single.availability ==
                        DovahLinkHostAvailability.checking &&
                    !checkingObserved.isCompleted) {
                  checkingObserved.complete();
                }
              });
          addTearDown(() async {
            await subscription.cancel();
            await monitorClient.close();
            await refreshTicks.close();
          });

          await waitForClientPresenceProbes(presenceProbe, 1);
          await checkingObserved.future.timeout(const Duration(seconds: 5));
          final List<DovahLinkKnownHostState> checkingSnapshot = snapshots
              .firstWhere(
                (List<DovahLinkKnownHostState> snapshot) =>
                    snapshot.single.availability ==
                    DovahLinkHostAvailability.checking,
              );
          expect(checkingSnapshot.single.host, host);
          expect(
            checkingSnapshot.single.availability,
            DovahLinkHostAvailability.checking,
          );
          presenceProbe.succeed(0, hostId: host.hostId);
          for (int attempt = 0; attempt < 20; attempt++) {
            await pumpEventQueue();
            if (snapshots.isNotEmpty &&
                snapshots.last.isNotEmpty &&
                snapshots.last.single.availability ==
                    DovahLinkHostAvailability.online) {
              break;
            }
          }
          expect(
            snapshots.last.single.availability,
            DovahLinkHostAvailability.online,
          );

          await monitorClient.disconnect();
          await pumpEventQueue();
          expect(
            snapshots.last.single.availability,
            DovahLinkHostAvailability.online,
          );
          expect(
            snapshots.last.single.sessionState,
            DovahLinkKnownHostSessionState.disconnected,
          );
          refreshTicks.add(null);
          await waitForClientPresenceProbes(presenceProbe, 2);
          presenceProbe.requests[1].response.completeError(
            const DovahLinkConnectionException('Could not reach the Host.'),
          );
          for (int attempt = 0; attempt < 20; attempt++) {
            await pumpEventQueue();
            if (snapshots.last.single.availability ==
                DovahLinkHostAvailability.offline) {
              break;
            }
          }
          expect(
            snapshots.last.single.availability,
            DovahLinkHostAvailability.offline,
          );
          refreshTicks.add(null);
          await waitForClientPresenceProbes(presenceProbe, 3);
          await monitorClient.close();

          expect(presenceProbe.cancellationCount, 1);
          expect(refreshTicks.hasListener, isFalse);
        },
      );
    },
  );

  group('Method close behaves correctly', () {
    test(
      'Method close starts active-session teardown before delayed presence cleanup and shares its future',
      () async {
        const String hostId = '81869993-955c-4ba3-a7d0-d35ca86078ea';
        final DovahLinkHost host = Fixtures.buildDovahLinkHost(hostId: hostId);
        final InMemoryClientStorage closeStorage = InMemoryClientStorage();
        await closeStorage.save(
          _persistedState(
            clientId: 'client-1',
            credential: 'known-host-credential',
            knownHost: host,
          ),
        );
        final FakeDovahLinkTransport closeTransport = FakeDovahLinkTransport();
        final ControllableClientPresenceProbe presenceProbe =
            ControllableClientPresenceProbe();
        final Completer<void> probeCancellation = Completer<void>();
        final Completer<void> transportClose = Completer<void>();
        presenceProbe.cancellationGate = probeCancellation;
        closeTransport.closeGate = transportClose;
        final StreamController<void> refreshTicks =
            StreamController<void>.broadcast(sync: true);
        final DovahLinkClient closeClient = buildDovahLinkClientForTesting(
          transport: closeTransport,
          storage: closeStorage,
          hostPresenceProbe: presenceProbe,
          knownHostPresenceMonitoringEnabled: true,
          hostPresenceRefreshTicks: refreshTicks.stream,
        );
        addTearDown(() async {
          if (!transportClose.isCompleted) {
            transportClose.complete();
          }
          if (!probeCancellation.isCompleted) {
            probeCancellation.complete();
          }
          await closeClient.close();
          await refreshTicks.close();
        });
        await waitForClientPresenceProbes(presenceProbe, 1);
        closeTransport.queueResponse(
          _rawFixture('connection/hello-ack-paired.json'),
        );
        closeTransport.queueResponse(
          _rawFixture('capabilities/capabilities-host.json'),
        );
        await closeClient.authenticateKnownHost(DovahLinkHostId(hostId));
        final Future<DovahLinkConnectionState> disconnected = closeClient
            .connectionStateChanges
            .firstWhere(
              (DovahLinkConnectionState state) =>
                  state == DovahLinkConnectionState.disconnected,
            );

        final Future<void> firstClose = closeClient.close();
        expect(identical(firstClose, closeClient.close()), isTrue);
        for (int attempt = 0; attempt < 20; attempt++) {
          await pumpEventQueue();
          if (closeTransport.closeCallCount > 0) {
            break;
          }
        }
        expect(closeTransport.closeCallCount, 1);
        expect(presenceProbe.cancellationCount, 0);

        transportClose.complete();
        await disconnected.timeout(const Duration(seconds: 5));
        bool closeCompleted = false;
        firstClose.then((_) => closeCompleted = true);
        await pumpEventQueue();
        expect(closeCompleted, isFalse);

        probeCancellation.complete();
        await firstClose;
        expect(presenceProbe.cancellationCount, 1);
      },
    );

    test('Method close cancels a pending candidate authentication', () async {
      final FakeDovahLinkTransport closeTransport = FakeDovahLinkTransport();
      final DovahLinkClient closeClient = buildDovahLinkClientForTesting(
        transport: closeTransport,
        storage: InMemoryClientStorage(),
      );
      addTearDown(closeClient.close);
      final Future<HelloResult> pendingAuthentication = closeClient
          .authenticateCandidate(Uri.parse('ws://127.0.0.1:58232/'));
      final Future<void> authenticationFails = expectLater(
        pendingAuthentication,
        throwsA(isA<DovahLinkConnectionException>()),
      );
      await pumpEventQueue();

      await closeClient.close();
      await authenticationFails;

      expect(closeTransport.closeCalled, isTrue);
      expect(
        closeClient.connectionState,
        DovahLinkConnectionState.disconnected,
      );
    });

    test('Method close stops a pending bounded recovery attempt', () async {
      const String hostId = '81869993-955c-4ba3-a7d0-d35ca86078ea';
      final DovahLinkHost host = Fixtures.buildDovahLinkHost(hostId: hostId);
      final InMemoryClientStorage closeStorage = InMemoryClientStorage();
      await closeStorage.save(
        _persistedState(
          clientId: 'client-1',
          credential: 'known-host-credential',
          knownHost: host,
        ),
      );
      final FakeDovahLinkTransport closeTransport = FakeDovahLinkTransport();
      final Completer<void> transportClose = Completer<void>();
      final DovahLinkClient closeClient = buildDovahLinkClientForTesting(
        transport: closeTransport,
        storage: closeStorage,
        reconnectAttemptDelays: const <Duration>[Duration(days: 1)],
        reconnectDeadline: const Duration(days: 2),
      );
      addTearDown(() async {
        if (!transportClose.isCompleted) {
          transportClose.complete();
        }
        await closeClient.close();
      });
      closeTransport.queueResponse(
        _rawFixture('connection/hello-ack-paired.json'),
      );
      closeTransport.queueResponse(
        _rawFixture('capabilities/capabilities-host.json'),
      );
      await closeClient.authenticateKnownHost(DovahLinkHostId(hostId));

      closeTransport.failMessagesWith(const SocketException('dropped'));
      for (int attempt = 0; attempt < 20; attempt++) {
        await pumpEventQueue();
        if (closeClient.connectionState ==
            DovahLinkConnectionState.reauthenticating) {
          break;
        }
      }
      expect(
        closeClient.connectionState,
        DovahLinkConnectionState.reauthenticating,
        reason: 'Outgoing messages: ${closeTransport.sent}',
      );
      expect(closeTransport.connectCalls, hasLength(2));

      final int closeCallCountBeforeClose = closeTransport.closeCallCount;
      closeTransport.closeGate = transportClose;
      final Future<void> closing = closeClient.close();
      for (int attempt = 0; attempt < 20; attempt++) {
        await pumpEventQueue();
        if (closeTransport.closeCallCount > closeCallCountBeforeClose) {
          break;
        }
      }
      expect(closeTransport.closeCallCount, closeCallCountBeforeClose + 1);
      closeTransport.queueRawResponse(
        _rawFixture('connection/hello-ack-paired.json'),
      );
      transportClose.complete();
      await closing;
      await pumpEventQueue();

      expect(
        closeClient.connectionState,
        DovahLinkConnectionState.disconnected,
      );
      expect(closeClient.sessionId, isNull);
      expect(closeClient.trustState, isNull);
      expect(closeTransport.connectCalls, hasLength(2));
    });

    test(
      'Method close continues session teardown after monitor cleanup fails',
      () async {
        int cancellationCount = 0;
        final Stream<void> failingRefreshTicks = Stream<void>.multi((
          MultiStreamController<void> sink,
        ) {
          sink.onCancel = () {
            cancellationCount++;
            return Future<void>.error(StateError('monitor cleanup failed'));
          };
        }, isBroadcast: true);
        final FakeDovahLinkTransport closeTransport = FakeDovahLinkTransport();
        final DovahLinkClient closeClient = buildDovahLinkClientForTesting(
          transport: closeTransport,
          storage: InMemoryClientStorage(),
          knownHostPresenceMonitoringEnabled: true,
          hostPresenceRefreshTicks: failingRefreshTicks,
        );
        await _connectAndHello(closeTransport, closeClient);

        await expectLater(closeClient.close(), completes);

        expect(cancellationCount, 1);
        expect(closeTransport.closeCalled, isTrue);
        expect(
          closeClient.connectionState,
          DovahLinkConnectionState.disconnected,
        );
      },
    );

    test(
      'Method close suppresses a stale presence result after terminal cleanup starts',
      () async {
        const String hostId = '81869993-955c-4ba3-a7d0-d35ca86078ea';
        final DovahLinkHost host = Fixtures.buildDovahLinkHost(hostId: hostId);
        final InMemoryClientStorage closeStorage = InMemoryClientStorage();
        await closeStorage.save(Fixtures.buildPersistedClientState(host: host));
        final ControllableClientPresenceProbe presenceProbe =
            ControllableClientPresenceProbe();
        final Completer<void> probeCancellation = Completer<void>();
        presenceProbe.cancellationGate = probeCancellation;
        final DovahLinkClient closeClient = buildDovahLinkClientForTesting(
          transport: FakeDovahLinkTransport(),
          storage: closeStorage,
          hostPresenceProbe: presenceProbe,
          knownHostPresenceMonitoringEnabled: true,
          hostPresenceRefreshTicks: const Stream<void>.empty(),
        );
        final List<List<DovahLinkKnownHostState>> snapshots =
            <List<DovahLinkKnownHostState>>[];
        final StreamSubscription<List<DovahLinkKnownHostState>> subscription =
            closeClient.hosts.knownHostStatesChanges.listen(snapshots.add);
        addTearDown(() async {
          await subscription.cancel();
          if (!probeCancellation.isCompleted) {
            probeCancellation.complete();
          }
          await closeClient.close();
        });
        await waitForClientPresenceProbes(presenceProbe, 1);

        final Future<void> closing = closeClient.close();
        presenceProbe.succeed(0, hostId: hostId);
        probeCancellation.complete();
        await closing;

        expect(
          snapshots
              .expand((List<DovahLinkKnownHostState> values) => values)
              .where(
                (DovahLinkKnownHostState state) =>
                    state.availability == DovahLinkHostAvailability.online,
              ),
          isEmpty,
        );
      },
    );
  });
}
