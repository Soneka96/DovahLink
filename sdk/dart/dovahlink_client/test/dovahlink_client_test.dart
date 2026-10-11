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

  /// Host frames to emit together in response to the next client send.
  List<String>? _responsesForNextSend;

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

  /// Whether the next [IDovahLinkTransport.connect] creates an inbound stream that delivers each
  /// frame to the listener inside `add`, with no microtask turn between frames -- the harshest
  /// ordering a transport could present for a Host ACK followed immediately by its baselines.
  bool synchronousDelivery = false;

  /// Makes the next [IDovahLinkTransport.connect] call throw [error] instead of succeeding.
  Object? failConnectWith;

  /// Makes every [IDovahLinkTransport.send] call throw [error] instead of succeeding.
  Object? failSendWith;

  /// Holds successive [IDovahLinkTransport.send] calls open: each send takes the first entry and
  /// stays pending until the test completes it (successfully, or with an error to simulate a late
  /// transport send failure).
  final List<Completer<void>> heldSends = <Completer<void>>[];

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

  /// Emits [rawJsonMessages] back-to-back when the next client message is sent.
  ///
  /// Each non-null `correlationId` is rewritten to that outgoing message's ID, allowing one
  /// request to receive its immediate reply and later correlated baseline in wire order.
  /// @param rawJsonMessages The Host frames to deliver in order.
  void queueResponsesForNextSend(List<String> rawJsonMessages) {
    if (_responsesForNextSend != null) {
      throw StateError('A response batch is already queued for the next send.');
    }
    _responsesForNextSend = List<String>.unmodifiable(rawJsonMessages);
  }

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
    _incoming = StreamController<String>(sync: synchronousDelivery);
  }

  /// Implements [IDovahLinkTransport.send].
  @override
  Future<void> send(String text) async {
    final Object? failure = failSendWith;
    if (failure != null) {
      throw failure;
    }
    sent.add(text);
    if (heldSends.isNotEmpty) {
      await heldSends.removeAt(0).future;
    }
    final String messageId =
        (jsonDecode(text) as JsonMap)['messageId'] as String;
    _pendingReplies.releaseFor(messageId, _requireIncoming());
    final List<String>? responseBatch = _responsesForNextSend;
    _responsesForNextSend = null;
    if (responseBatch != null) {
      final StreamController<String> incoming = _requireIncoming();
      for (final String rawJson in responseBatch) {
        final JsonMap response = jsonDecode(rawJson) as JsonMap;
        if (response['correlationId'] != null) {
          response['correlationId'] = messageId;
        }
        incoming.add(jsonEncode(response));
      }
    }
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

/// Reads the canonical data value from a state Snapshot fixture.
/// @param relativePath The fixture path relative to `protocol/fixtures/`.
/// @return The canonical state-area value inside the fixture Snapshot.
Object? _stateFixtureValue(String relativePath) {
  final JsonMap envelope = jsonDecode(_rawFixture(relativePath)) as JsonMap;
  final JsonMap payload = envelope['payload'] as JsonMap;
  final JsonMap data = payload['data'] as JsonMap;
  return data['value'];
}

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

/// Builds a retryable Host timeout for an accepted state baseline.
/// @param correlationId The `subscribe` request awaiting its baseline.
/// @param sessionId The authenticated Host session reporting the timeout.
/// @return The raw correlated Host error envelope.
String _rawTemporarilyUnavailableError({
  required String correlationId,
  String sessionId = 'session-paired-1',
}) => jsonEncode(<String, dynamic>{
  'messageType': 'error',
  'messageId': 'message-temporarily-unavailable-1',
  'sessionId': sessionId,
  'correlationId': correlationId,
  'payload': <String, dynamic>{
    'code': 'temporarily_unavailable',
    'message': 'No authoritative baseline is available yet.',
    'retryable': true,
  },
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

/// Returns the message IDs of every [messageType] envelope sent by [transport], in send order.
List<String> _sentIdsOfType(
  FakeDovahLinkTransport transport,
  String messageType,
) => transport.sent
    .map((String raw) => jsonDecode(raw) as JsonMap)
    .where((JsonMap envelope) => envelope['messageType'] == messageType)
    .map((JsonMap envelope) => envelope['messageId'] as String)
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
  String stateAuthorityId = 'authority-1',
  String? playContextId = 'context-1',
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
  'stateAuthorityId': stateAuthorityId,
  'playContextId': playContextId,
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

/// Connects [client] through its grouped API and admits an unpaired session.
Future<void> _connectAndHello(
  FakeDovahLinkTransport transport,
  DovahLinkClient client,
) async {
  transport.queueResponse(_rawFixture('connection/hello-ack.json'));
  transport.queueResponse(_rawFixture('capabilities/capabilities-host.json'));
  await client.connections.connectCandidate(Uri.parse('ws://127.0.0.1:58231/'));
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
  await client.connections.connectKnownHost(
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
    await client.currentHost.subscribeStateArea(area);
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

/// Builds the wire value for [area] in the live-state retention tests, varied by [n].
/// @param area The gameplay domain to build.
/// @param n A number that makes values from different baselines distinguishable.
/// @return The canonical wire value for [area].
Object? _retentionWireValue(DovahLinkStateArea area, int n) => switch (area) {
  DovahLinkStateArea.characterXp => n * 1.5,
  DovahLinkStateArea.characterVitals => <String, dynamic>{
    'health': <String, dynamic>{'current': 100.0 + n, 'max': 410.0},
    'magicka': <String, dynamic>{'current': 180.0, 'max': 250.0},
    'stamina': <String, dynamic>{'current': 120.0, 'max': 190.0},
  },
  DovahLinkStateArea.characterLevel => n,
  DovahLinkStateArea.characterIdentity => <String, dynamic>{
    'name': 'Dragonborn $n',
    'race': 'Nord',
  },
  DovahLinkStateArea.characterSupernaturalTraits => <String, dynamic>{
    'isVampire': false,
    'hasVampireLordForm': false,
    'hasWerewolfForm': false,
  },
  DovahLinkStateArea.playerLocation => <String, dynamic>{
    'cellId': 1000 + n,
    'cellKind': 'exterior',
    'cellName': 'WhiterunWorld',
    'locationId': 98765,
    'locationName': 'Whiterun',
    'worldspaceId': 1,
    'worldspaceName': 'Skyrim',
  },
  DovahLinkStateArea.gameTime => <String, dynamic>{
    'year': 201,
    'month': 9,
    'monthName': 'Hearthfire',
    'day': n,
    'hour': 17,
    'minute': 45,
  },
  DovahLinkStateArea.trackedQuests => <String, dynamic>{'quests': <Object>[]},
};

/// The distinct baseline revision each domain holds before a transport loss.
int _baselineRevision(DovahLinkStateArea area) => 3 + area.index;

/// Builds one Snapshot for [area] using the retention test wire values.
/// @param area The gameplay domain.
/// @param revision The authoritative revision.
/// @param stateAuthorityId The Host continuity epoch on the envelope.
/// @param playContextId The play context on the envelope.
/// @param correlationId The subscribe or snapshot request the Snapshot answers, if any.
/// @return The raw Snapshot envelope.
String _retentionSnapshot(
  DovahLinkStateArea area,
  int revision, {
  String stateAuthorityId = 'authority-1',
  String playContextId = 'context-1',
  String? correlationId,
}) => _rawStateSnapshot(
  stateArea: area.protocolValue,
  revision: revision,
  value: _retentionWireValue(area, revision),
  stateAuthorityId: stateAuthorityId,
  playContextId: playContextId,
  correlationId: correlationId,
);

/// Reads the replayed current value of every gameplay domain.
/// @param client The client whose public typed streams are read.
/// @return Each domain's current synchronization view, keyed by its state area.
Future<Map<DovahLinkStateArea, StateSynchronization<Object?>>> _readDomains(
  DovahLinkClient client,
) async {
  final IDovahLinkCurrentHost host = client.currentHost;
  return <DovahLinkStateArea, StateSynchronization<Object?>>{
    DovahLinkStateArea.characterXp: await host.character.xpChanges.first,
    DovahLinkStateArea.characterVitals:
        await host.character.vitalsChanges.first,
    DovahLinkStateArea.characterLevel: await host.character.levelChanges.first,
    DovahLinkStateArea.characterIdentity:
        await host.character.identityChanges.first,
    DovahLinkStateArea.characterSupernaturalTraits:
        await host.character.supernaturalTraitsChanges.first,
    DovahLinkStateArea.playerLocation: await host.playerLocationChanges.first,
    DovahLinkStateArea.gameTime: await host.gameTimeChanges.first,
    DovahLinkStateArea.trackedQuests: await host.trackedQuestsChanges.first,
  };
}

/// Records every emission of every gameplay domain stream, starting with its replayed value.
/// @param client The client whose public typed streams are recorded.
/// @return The emissions for each domain, in order.
Map<DovahLinkStateArea, List<StateSynchronization<Object?>>> _recordDomains(
  DovahLinkClient client,
) {
  final Map<DovahLinkStateArea, List<StateSynchronization<Object?>>> records =
      <DovahLinkStateArea, List<StateSynchronization<Object?>>>{
        for (final DovahLinkStateArea area in DovahLinkStateArea.values)
          area: <StateSynchronization<Object?>>[],
      };
  void record<T>(
    DovahLinkStateArea area,
    Stream<StateSynchronization<T>> changes,
  ) {
    final StreamSubscription<StateSynchronization<T>> subscription = changes
        .listen(records[area]!.add);
    addTearDown(subscription.cancel);
  }

  final IDovahLinkCurrentHost host = client.currentHost;
  record(DovahLinkStateArea.characterXp, host.character.xpChanges);
  record(DovahLinkStateArea.characterVitals, host.character.vitalsChanges);
  record(DovahLinkStateArea.characterLevel, host.character.levelChanges);
  record(DovahLinkStateArea.characterIdentity, host.character.identityChanges);
  record(
    DovahLinkStateArea.characterSupernaturalTraits,
    host.character.supernaturalTraitsChanges,
  );
  record(DovahLinkStateArea.playerLocation, host.playerLocationChanges);
  record(DovahLinkStateArea.gameTime, host.gameTimeChanges);
  record(DovahLinkStateArea.trackedQuests, host.trackedQuestsChanges);
  return records;
}

/// Connects a trusted client, subscribes [areas], and delivers a baseline for each [baselined] area.
/// @param areas The areas to request, cumulatively.
/// @param baselined The subset that receives a Snapshot; defaults to every requested area.
/// @return The transport and client of the established session.
Future<({FakeDovahLinkTransport transport, DovahLinkClient client})>
_establishRetentionSession(
  Iterable<DovahLinkStateArea> areas, {
  Iterable<DovahLinkStateArea>? baselined,
}) async {
  final FakeDovahLinkTransport transport = FakeDovahLinkTransport();
  final InMemoryClientStorage storage = InMemoryClientStorage();
  final DovahLinkClient client = _buildFastReconnectClient(transport, storage);
  addTearDown(client.close);
  await _connectAndTrustedHello(transport, client, storage);
  await _subscribeStateAreas(transport, client, areas);
  for (final DovahLinkStateArea area in baselined ?? areas) {
    transport.queueRawResponse(
      _retentionSnapshot(area, _baselineRevision(area)),
    );
  }
  await pumpEventQueue();
  return (transport: transport, client: client);
}

/// Queues one reauthentication and a restoration acknowledgement, then drops the live socket.
/// @param transport The fake Host transport.
/// @param accepted The areas the restoration acknowledgement accepts.
/// @param rejected The areas the restoration acknowledgement rejects.
void _dropTransport(
  FakeDovahLinkTransport transport, {
  required Iterable<DovahLinkStateArea> accepted,
  Iterable<DovahLinkStateArea> rejected = const <DovahLinkStateArea>[],
}) {
  transport.queueResponse(_rawFixture('connection/hello-ack-paired.json'));
  transport.queueResponse(_rawFixture('capabilities/capabilities-host.json'));
  transport.queueResponse(
    _rawSubscriptionAck(
      accepted: <String>[
        for (final DovahLinkStateArea area in accepted) area.protocolValue,
      ],
      rejected: <String>[
        for (final DovahLinkStateArea area in rejected) area.protocolValue,
      ],
    ),
  );
  transport.failMessagesWith(const SocketException('dropped'));
}

/// Pumps the event queue until [done] holds or the bounded budget is spent.
Future<void> _pumpUntil(bool Function() done) async {
  for (int turn = 0; turn < 80 && !done(); turn++) {
    await pumpEventQueue();
  }
}

/// Pumps until a restoration `subscribe` beyond the first [subscribesBefore] has been sent.
Future<void> _pumpUntilRestoreSent(
  FakeDovahLinkTransport transport,
  int subscribesBefore,
) => _pumpUntil(
  () => _sentSubscriptionUpdates(transport).length > subscribesBefore,
);

/// A session whose Character Level recovery request survived a transport loss and was
/// retransmitted into a reauthenticated session whose restored subscription is not yet acknowledged.
typedef _OrphanedRecovery = ({
  FakeDovahLinkTransport transport,
  DovahLinkClient client,
  List<DovahLinkConnectionState> states,
  String snapshotRequestId,
  String restoreId,
  int levelRevision,
});

/// Establishes [areas] with baselines, opens a Character Level revision gap, then drops the
/// transport and reauthenticates without delivering the restored subscription's acknowledgement.
/// @param areas The areas to subscribe, including Character Level.
/// @return The held-acknowledgement session and the IDs needed to answer it.
Future<_OrphanedRecovery> _startOrphanedRecovery(
  List<DovahLinkStateArea> areas,
) async {
  final s = await _establishRetentionSession(areas);
  final List<DovahLinkConnectionState> states = <DovahLinkConnectionState>[];
  final StreamSubscription<DovahLinkConnectionState> stateSubscription = s
      .client
      .connections
      .stateChanges
      .listen(states.add);
  addTearDown(stateSubscription.cancel);
  final int levelRevision = _baselineRevision(
    DovahLinkStateArea.characterLevel,
  );
  s.transport.queueRawResponse(
    _rawStateEvent(
      stateArea: 'character_level',
      baseRevision: levelRevision + 4,
      revision: levelRevision + 5,
      value: 30,
    ),
  );
  await pumpEventQueue();
  expect(_sentIdsOfType(s.transport, 'snapshot_request'), hasLength(1));
  final int subscribesBefore = _sentSubscriptionUpdates(s.transport).length;
  s.transport.queueResponse(_rawFixture('connection/hello-ack-paired.json'));
  s.transport.queueResponse(_rawFixture('capabilities/capabilities-host.json'));
  s.transport.failMessagesWith(const SocketException('dropped'));
  await _pumpUntilRestoreSent(s.transport, subscribesBefore);
  await _pumpUntil(
    () => s.client.connections.state == DovahLinkConnectionState.connected,
  );
  final List<String> requests = _sentIdsOfType(s.transport, 'snapshot_request');
  expect(requests, hasLength(2));
  return (
    transport: s.transport,
    client: s.client,
    states: states,
    snapshotRequestId: requests.last,
    restoreId:
        _sentSubscriptionUpdates(s.transport).last['messageId'] as String,
    levelRevision: levelRevision,
  );
}

/// Delivers the restored subscription's acknowledgement for [restoreId].
void _deliverRestoreAck(
  FakeDovahLinkTransport transport,
  String restoreId, {
  required Iterable<DovahLinkStateArea> accepted,
  Iterable<DovahLinkStateArea> rejected = const <DovahLinkStateArea>[],
}) {
  final JsonMap ack =
      jsonDecode(
            _rawSubscriptionAck(
              accepted: <String>[
                for (final DovahLinkStateArea area in accepted)
                  area.protocolValue,
              ],
              rejected: <String>[
                for (final DovahLinkStateArea area in rejected)
                  area.protocolValue,
              ],
            ),
          )
          as JsonMap;
  ack['correlationId'] = restoreId;
  transport.queueRawResponse(jsonEncode(ack));
}

/// Pumps a fixed number of turns so every delivered frame is fully processed.
Future<void> _settle() => _pumpUntil(() => false);

/// Expects [state] to hold a retained, non-current observation.
void _expectRetained(
  StateSynchronization<Object?> state, {
  required int revision,
  String authority = 'authority-1',
  String context = 'context-1',
}) {
  expect(state.status, DovahLinkStateStatus.recovering);
  expect(state.revision, revision);
  expect(state.value, isNotNull);
  expect(state.stateAuthorityId, authority);
  expect(state.playContextId, context);
}

/// Expects [state] to be awaiting its first baseline: recovering, with no value or revision. The
/// identity may already be the one other domains observed, but no usable observation exists.
void _expectAwaitingBaseline(StateSynchronization<Object?> state) {
  expect(state.status, DovahLinkStateStatus.recovering);
  expect(state.value, isNull);
  expect(state.revision, isNull);
}

/// Expects [state] to carry no value or metadata, with [status].
void _expectEmpty(
  StateSynchronization<Object?> state, [
  DovahLinkStateStatus status = DovahLinkStateStatus.notSubscribed,
]) {
  expect(state.status, status);
  expect(state.value, isNull);
  expect(state.revision, isNull);
  expect(state.stateAuthorityId, isNull);
  expect(state.playContextId, isNull);
}

/// Builds one unsolicited Host `gameplay_readiness` envelope for fake-host delivery.
/// @param ready Whether the Host reports gameplay as ready.
/// @param sequence The per-session publication sequence.
/// @param sessionId The session the Host publishes on.
/// @param stateAuthorityId The authority epoch the value is published under.
/// @return The raw protocol envelope.
String _rawReadiness({
  required bool ready,
  required int sequence,
  String sessionId = 'session-1',
  String stateAuthorityId = 'state-authority-1',
  String? playContextId,
}) => jsonEncode(<String, dynamic>{
  'messageType': 'gameplay_readiness',
  'messageId': 'readiness-$sessionId-$sequence',
  'sessionId': sessionId,
  'correlationId': null,
  'payload': <String, dynamic>{'ready': ready, 'sequence': sequence},
  'stateAuthorityId': stateAuthorityId,
  'playContextId': playContextId,
  'clientId': null,
});

/// Reads the canonical unpaired `hello_ack` fixture, replaced with [sessionId] and [hostVersion].
String _rawHelloAck({String sessionId = 'session-1', String? hostVersion}) {
  final JsonMap ack =
      jsonDecode(_rawFixture('connection/hello-ack.json')) as JsonMap;
  ack['sessionId'] = sessionId;
  if (hostVersion != null) {
    (ack['payload'] as JsonMap)['hostVersion'] = hostVersion;
  }
  return jsonEncode(ack);
}

/// Records every value [client] replays or emits for its gameplay readiness.
List<DovahLinkGameplayReadiness> _recordReadiness(DovahLinkClient client) {
  final List<DovahLinkGameplayReadiness> seen = <DovahLinkGameplayReadiness>[];
  final StreamSubscription<DovahLinkGameplayReadiness> subscription = client
      .currentHost
      .gameplayReadinessChanges
      .listen(seen.add);
  addTearDown(subscription.cancel);
  return seen;
}

/// An in-memory storage whose saves can be held open, to keep trusted admission pending after
/// `hello_ack` the way real storage I/O does.
class HoldableClientStorage extends InMemoryClientStorage {
  /// While set, [save] waits for it before writing.
  Completer<void>? saveGate;

  /// Writes [state] once any [saveGate] is released.
  @override
  Future<void> save(PersistedClientState state) async {
    await saveGate?.future;
    await super.save(state);
  }
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

  group('Behavior Player Location state composition behaves correctly', () {
    test(
      'Behavior Player Location state composes a typed stream across subscribe, Snapshot, unavailable, and unsubscribe',
      () async {
        await _connectAndTrustedHello(transport, client, storage);
        expect(
          (await client.currentHost.playerLocationChanges.first).status,
          DovahLinkStateStatus.notSubscribed,
        );

        transport.queueResponse(
          _rawSubscriptionAck(accepted: <String>['player_location']),
        );
        expect(
          await client.currentHost.subscribeStateArea(
            DovahLinkStateArea.playerLocation,
          ),
          isEmpty,
        );
        expect(
          (await client.currentHost.playerLocationChanges.first).status,
          DovahLinkStateStatus.recovering,
        );

        final Future<void> synchronized = expectLater(
          client.currentHost.playerLocationChanges,
          emitsThrough(
            predicate<StateSynchronization<PlayerLocationState?>>(
              (StateSynchronization<PlayerLocationState?> state) =>
                  state.status == DovahLinkStateStatus.synchronized &&
                  state.revision == 1 &&
                  state.stateAuthorityId == 'authority-1' &&
                  state.playContextId == 'context-1' &&
                  state.value?.cellId == 123456 &&
                  state.value?.cellKind == PlayerLocationCellKind.exterior &&
                  state.value?.cellName == 'WhiterunWorld' &&
                  state.value?.locationId == 98765 &&
                  state.value?.locationName == 'Whiterun' &&
                  state.value?.worldspaceId == 1 &&
                  state.value?.worldspaceName == 'Skyrim',
            ),
          ),
        );
        transport.queueRawResponse(
          _rawStateSnapshot(
            stateArea: 'player_location',
            revision: 1,
            value: <String, dynamic>{
              'cellId': 123456,
              'cellKind': 'exterior',
              'cellName': 'WhiterunWorld',
              'locationId': 98765,
              'locationName': 'Whiterun',
              'worldspaceId': 1,
              'worldspaceName': 'Skyrim',
            },
          ),
        );
        await synchronized;

        final Future<void> unavailable = expectLater(
          client.currentHost.playerLocationChanges,
          emitsThrough(
            predicate<StateSynchronization<PlayerLocationState?>>(
              (StateSynchronization<PlayerLocationState?> state) =>
                  state.status == DovahLinkStateStatus.unavailable &&
                  state.value?.cellName == 'WhiterunWorld' &&
                  state.revision == 2,
            ),
          ),
        );
        transport.queueRawResponse(
          _rawStateSnapshot(
            stateArea: 'player_location',
            revision: 2,
            value: null,
          ),
        );
        await unavailable;

        transport.queueResponse(
          _rawSubscriptionAck(accepted: const <String>[]),
        );
        expect(
          await client.currentHost.unsubscribeStateArea(
            DovahLinkStateArea.playerLocation,
          ),
          isEmpty,
        );
        expect(
          (await client.currentHost.playerLocationChanges.first).status,
          DovahLinkStateStatus.notSubscribed,
        );
      },
    );
  });

  group('Behavior Game Time state composition behaves correctly', () {
    test(
      'Behavior Game Time state composes a typed stream across subscribe, Snapshot, unavailable, and unsubscribe',
      () async {
        await _connectAndTrustedHello(transport, client, storage);
        expect(
          (await client.currentHost.gameTimeChanges.first).status,
          DovahLinkStateStatus.notSubscribed,
        );

        transport.queueResponse(
          _rawSubscriptionAck(accepted: <String>['game_time']),
        );
        expect(
          await client.currentHost.subscribeStateArea(
            DovahLinkStateArea.gameTime,
          ),
          isEmpty,
        );
        expect(
          (await client.currentHost.gameTimeChanges.first).status,
          DovahLinkStateStatus.recovering,
        );

        final Future<void> synchronized = expectLater(
          client.currentHost.gameTimeChanges,
          emitsThrough(
            predicate<StateSynchronization<GameTimeState?>>(
              (StateSynchronization<GameTimeState?> state) =>
                  state.status == DovahLinkStateStatus.synchronized &&
                  state.revision == 1 &&
                  state.stateAuthorityId == 'authority-1' &&
                  state.playContextId == 'context-1' &&
                  state.value?.year == 201 &&
                  state.value?.month == 9 &&
                  state.value?.monthName == 'Hearthfire' &&
                  state.value?.day == 17 &&
                  state.value?.hour == 17 &&
                  state.value?.minute == 45,
            ),
          ),
        );
        transport.queueRawResponse(
          _rawStateSnapshot(
            stateArea: 'game_time',
            revision: 1,
            value: <String, dynamic>{
              'year': 201,
              'month': 9,
              'monthName': 'Hearthfire',
              'day': 17,
              'hour': 17,
              'minute': 45,
            },
          ),
        );
        await synchronized;

        final Future<void> unavailable = expectLater(
          client.currentHost.gameTimeChanges,
          emitsThrough(
            predicate<StateSynchronization<GameTimeState?>>(
              (StateSynchronization<GameTimeState?> state) =>
                  state.status == DovahLinkStateStatus.unavailable &&
                  state.value?.year == 201 &&
                  state.value?.monthName == 'Hearthfire' &&
                  state.revision == 2,
            ),
          ),
        );
        transport.queueRawResponse(
          _rawStateSnapshot(stateArea: 'game_time', revision: 2, value: null),
        );
        await unavailable;

        transport.queueResponse(
          _rawSubscriptionAck(accepted: const <String>[]),
        );
        expect(
          await client.currentHost.unsubscribeStateArea(
            DovahLinkStateArea.gameTime,
          ),
          isEmpty,
        );
        expect(
          (await client.currentHost.gameTimeChanges.first).status,
          DovahLinkStateStatus.notSubscribed,
        );
      },
    );
  });

  group('Behavior Tracked Quests state composition behaves correctly', () {
    test(
      'Behavior Tracked Quests state composes a typed stream across subscribe, collection, empty, unavailable, and unsubscribe',
      () async {
        await _connectAndTrustedHello(transport, client, storage);
        expect(
          (await client.currentHost.trackedQuestsChanges.first).status,
          DovahLinkStateStatus.notSubscribed,
        );

        transport.queueResponse(
          _rawSubscriptionAck(accepted: <String>['tracked_quests']),
        );
        expect(
          await client.currentHost.subscribeStateArea(
            DovahLinkStateArea.trackedQuests,
          ),
          isEmpty,
        );
        expect(
          (await client.currentHost.trackedQuestsChanges.first).status,
          DovahLinkStateStatus.recovering,
        );

        final Future<void> synchronized = expectLater(
          client.currentHost.trackedQuestsChanges,
          emitsThrough(
            predicate<StateSynchronization<TrackedQuestsState?>>(
              (StateSynchronization<TrackedQuestsState?> state) =>
                  state.status == DovahLinkStateStatus.synchronized &&
                  state.revision == 1 &&
                  state.stateAuthorityId == 'authority-1' &&
                  state.playContextId == 'context-1' &&
                  state.value?.quests.length == 2 &&
                  state.value?.quests.first.questId == 10 &&
                  state.value?.quests.first.title == 'Cenário — Localized' &&
                  state.value?.quests.first.objectives.length == 6 &&
                  state.value?.quests.first.objectives.first.state ==
                      TrackedQuestObjectiveState.dormant &&
                  state.value?.quests.last.questId == 20 &&
                  state.value?.quests.last.title == 'Whiterun — Rescue' &&
                  state.value?.quests.last.objectives.single.state ==
                      TrackedQuestObjectiveState.displayed,
            ),
          ),
        );
        transport.queueRawResponse(
          _rawStateSnapshot(
            stateArea: 'tracked_quests',
            revision: 1,
            value: _stateFixtureValue(
              'state/state-snapshot-tracked-quests.json',
            ),
          ),
        );
        await synchronized;

        final Future<void> empty = expectLater(
          client.currentHost.trackedQuestsChanges,
          emitsThrough(
            predicate<StateSynchronization<TrackedQuestsState?>>(
              (StateSynchronization<TrackedQuestsState?> state) =>
                  state.status == DovahLinkStateStatus.synchronized &&
                  state.revision == 2 &&
                  state.value?.quests.isEmpty == true,
            ),
          ),
        );
        transport.queueRawResponse(
          _rawStateSnapshot(
            stateArea: 'tracked_quests',
            revision: 2,
            value: _stateFixtureValue(
              'state/state-snapshot-tracked-quests-empty.json',
            ),
          ),
        );
        await empty;

        final Future<void> unavailable = expectLater(
          client.currentHost.trackedQuestsChanges,
          emitsThrough(
            predicate<StateSynchronization<TrackedQuestsState?>>(
              (StateSynchronization<TrackedQuestsState?> state) =>
                  state.status == DovahLinkStateStatus.unavailable &&
                  state.value?.quests.isEmpty == true &&
                  state.revision == 3,
            ),
          ),
        );
        transport.queueRawResponse(
          _rawStateSnapshot(
            stateArea: 'tracked_quests',
            revision: 3,
            value: _stateFixtureValue(
              'state/state-snapshot-tracked-quests-unavailable.json',
            ),
          ),
        );
        await unavailable;

        transport.queueResponse(
          _rawSubscriptionAck(accepted: const <String>[]),
        );
        expect(
          await client.currentHost.unsubscribeStateArea(
            DovahLinkStateArea.trackedQuests,
          ),
          isEmpty,
        );
        expect(
          (await client.currentHost.trackedQuestsChanges.first).status,
          DovahLinkStateStatus.notSubscribed,
        );
      },
    );

    test(
      'Behavior Tracked Quests state resets to notSubscribed when the session ends',
      () async {
        await _connectAndTrustedHello(transport, client, storage);
        transport.queueResponse(
          _rawSubscriptionAck(accepted: <String>['tracked_quests']),
        );
        await client.currentHost.subscribeStateArea(
          DovahLinkStateArea.trackedQuests,
        );

        final Future<void> synchronized = expectLater(
          client.currentHost.trackedQuestsChanges,
          emitsThrough(
            predicate<StateSynchronization<TrackedQuestsState?>>(
              (StateSynchronization<TrackedQuestsState?> state) =>
                  state.status == DovahLinkStateStatus.synchronized &&
                  state.revision == 1,
            ),
          ),
        );
        transport.queueRawResponse(
          _rawStateSnapshot(
            stateArea: 'tracked_quests',
            revision: 1,
            value: _stateFixtureValue(
              'state/state-snapshot-tracked-quests.json',
            ),
          ),
        );
        await synchronized;

        await client.connections.disconnect();

        expect(
          (await client.currentHost.trackedQuestsChanges.first).status,
          DovahLinkStateStatus.notSubscribed,
        );
      },
    );
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
        expect(client.currentHost.trustState, DovahLinkTrustState.trusted);
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
          (await client.currentHost.character.xpChanges.first).status,
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
      'Behavior grouped API composition routes rename through the active trusted session',
      () async {
        await _connectAndTrustedHello(transport, client, storage);
        transport.queueResponse(
          _rawFixture('rename/rename-outcome-renamed.json'),
        );

        expect(
          await client.connections.renameDevice('Living Room PC'),
          RenameOutcome.renamed,
        );
        final List<JsonMap> sentMessages = transport.sent
            .map((String frame) => jsonDecode(frame) as JsonMap)
            .toList(growable: false);
        final JsonMap request = sentMessages.singleWhere(
          (JsonMap message) => message['messageType'] == 'rename_request',
        );
        expect(
          (request['payload'] as JsonMap)['displayName'],
          'Living Room PC',
        );
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

  group('Behavior separate client instances behave correctly', () {
    test(
      'Behavior two clients share no session, transport, or subscription state',
      () async {
        final FakeDovahLinkTransport otherTransport = FakeDovahLinkTransport();
        final DovahLinkClient other = buildDovahLinkClientForTesting(
          transport: otherTransport,
          storage: InMemoryClientStorage(),
        );
        addTearDown(other.close);

        await _connectAndHello(transport, client);
        await expectLater(
          client.currentHost.subscribeStateArea(DovahLinkStateArea.characterXp),
          throwsA(isA<DovahLinkConnectionException>()),
        );

        expect(client.connections.state, DovahLinkConnectionState.connected);
        expect(other.connections.state, DovahLinkConnectionState.disconnected);
        expect(otherTransport.connectCalls, isEmpty);
        expect(client.clientId, isNotNull);
        expect(other.clientId, isNull);
        expect(other.currentHost.trustState, isNull);
        expect(_sentSubscriptionUpdates(otherTransport), isEmpty);
      },
    );

    test('Behavior closing one client leaves another fully usable', () async {
      final FakeDovahLinkTransport otherTransport = FakeDovahLinkTransport();
      final DovahLinkClient other = buildDovahLinkClientForTesting(
        transport: otherTransport,
        storage: InMemoryClientStorage(),
      );
      addTearDown(other.close);

      await client.close();
      await _connectAndHello(otherTransport, other);

      expect(other.connections.state, DovahLinkConnectionState.connected);
      await expectLater(
        _connectAndHello(transport, client),
        throwsA(isA<DovahLinkConnectionException>()),
      );
    });

    test('Behavior close is idempotent and shares one completion', () async {
      await _connectAndHello(transport, client);

      final Future<void> first = client.close();
      final Future<void> second = client.close();

      expect(identical(first, second), isTrue);
      await first;
      expect(client.connections.state, DovahLinkConnectionState.disconnected);
      expect(transport.closeCallCount, 1);
    });
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

        await client.connections.connectKnownHost(
          DovahLinkHostId(oldHost.hostId),
        );

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
          discoveryClient.pairing.candidates,
        );
        addTearDown(candidates.cancel);

        expect(await candidates.moveNext(), isTrue);
        expect(candidates.current, isEmpty);
        expect(await discoveryClient.pairing.discoverHosts(), <DovahLinkHost>[
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

        expect(await discoveryClient.pairing.discoverHosts(), discovered);
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
            .pairing
            .discoverHosts();
        await olderStarted.future.timeout(const Duration(seconds: 5));
        final Future<List<DovahLinkHost>> newerDiscovery = discoveryClient
            .pairing
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
            .pairing
            .candidates
            .listen((List<DovahLinkHost> hosts) {
              candidateUpdates.add(hosts);
              if (!initialUpdate.isCompleted) {
                initialUpdate.complete();
              }
            }, onError: streamErrors.add);
        addTearDown(candidates.cancel);
        await initialUpdate.future;

        final Future<List<DovahLinkHost>> pendingDiscovery = closeClient.pairing
            .discoverHosts();
        await loadStarted.future;
        await closeClient.close();
        loadGate.complete();

        expect(await pendingDiscovery, isEmpty);
        await pumpEventQueue();
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
            .pairing
            .candidates
            .listen((List<DovahLinkHost> hosts) {
              candidateUpdates.add(hosts);
              if (!initialUpdate.isCompleted) {
                initialUpdate.complete();
              }
            }, onError: streamErrors.add);
        addTearDown(candidates.cancel);
        await initialUpdate.future;

        final Future<List<DovahLinkHost>> pendingDiscovery = closeClient.pairing
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

        final Future<List<DovahLinkHost>> pendingDiscovery = closeClient.pairing
            .discoverHosts();
        await loadStarted.future;
        await closeClient.close();
        pendingStorage.loadError = const DovahLinkStorageException(
          'storage failed after close',
        );
        loadGate.complete();

        expect(await pendingDiscovery, isEmpty);
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
            .pairing
            .candidates
            .listen((List<DovahLinkHost> hosts) {
              candidateUpdates.add(hosts);
              if (!initialUpdate.isCompleted) {
                initialUpdate.complete();
              }
            });
        addTearDown(candidates.cancel);
        await initialUpdate.future;

        final Future<List<DovahLinkHost>> pendingDiscovery = closeClient.pairing
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

        expect(await discoveryClient.pairing.discoverHosts(), <DovahLinkHost>[
          candidate,
        ]);
        expect(await discoveryClient.pairing.discoverHosts(), <DovahLinkHost>[
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

        expect(await discoveryClient.pairing.discoverHosts(), isEmpty);
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
          discoveryClient.pairing.candidates,
        );
        addTearDown(candidates.cancel);
        expect(await candidates.moveNext(), isTrue);
        expect(candidates.current, isEmpty);

        final Future<List<DovahLinkHost>> pendingDiscovery = discoveryClient
            .pairing
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
        await discoveryClient.pairing.authenticateCandidate(
          claimedHost.endpoint,
        );
        pairingTransport.queueResponse(
          _rawFixture('pairing/pairing-outcome-credential-issued.json'),
        );
        pairingTransport.queueResponse(
          _rawFixture('pairing/pairing-outcome-trusted.json'),
        );
        await discoveryClient.pairing.confirmCode(code: '123456');

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
          discoveryClient.pairing.discoverHosts(),
          throwsA(isA<DovahLinkStorageException>()),
        );
      },
    );
  });

  group('Property candidates behaves correctly', () {
    test(
      'Property candidates reports storage errors and receives later candidate state',
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
            discoveryClient.pairing.candidates.listen(
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
          discoveryClient.pairing.discoverHosts(),
          throwsA(isA<DovahLinkStorageException>()),
        );
        await storageFailure.future.timeout(const Duration(seconds: 5));
        failingStorage.loadError = null;
        expect(await discoveryClient.pairing.discoverHosts(), <DovahLinkHost>[
          candidate,
        ]);
        expect(await candidatePublished.future, <DovahLinkHost>[candidate]);
      },
    );

    test(
      'Property candidates removes a candidate after pairing commits its Known Host relationship',
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
          discoveryClient.pairing.candidates,
        );
        addTearDown(candidates.cancel);
        expect(await candidates.moveNext(), isTrue);
        expect(candidates.current, isEmpty);
        expect(await discoveryClient.pairing.discoverHosts(), <DovahLinkHost>[
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
        await discoveryClient.pairing.authenticateCandidate(candidate.endpoint);
        pairingTransport.queueResponse(
          _rawFixture('pairing/pairing-outcome-credential-issued.json'),
        );
        pairingTransport.queueResponse(
          _rawFixture('pairing/pairing-outcome-trusted.json'),
        );
        await discoveryClient.pairing.confirmCode(code: '123456');

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
        await runtimeClient.connections.connectKnownHost(
          DovahLinkHostId(hostId),
        );
        await Future<void>.delayed(Duration.zero);

        expect(
          snapshots.last.single.availability,
          DovahLinkHostAvailability.online,
        );
        expect(
          snapshots.last.single.sessionState,
          DovahLinkKnownHostSessionState.connected,
        );
        await runtimeClient.connections.disconnect();
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

        await runtimeClient.connections.connectCandidate(
          Uri.parse('ws://127.0.0.1:58231/'),
        );
        await Future<void>.delayed(Duration.zero);

        expect(
          runtimeClient.connections.state,
          DovahLinkConnectionState.connected,
        );
        expect(snapshots.last.single.host.hostId, hostId);
        expect(
          snapshots.last.single.sessionState,
          DovahLinkKnownHostSessionState.disconnected,
        );
        await runtimeClient.connections.disconnect();
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
        await runtimeClient.connections.connectKnownHost(
          DovahLinkHostId(hostId),
        );
        await Future<void>.delayed(Duration.zero);

        runtimeTransport.queueResponse(
          _rawSessionInvalidated('revoked', sessionId: 'session-paired-1'),
        );
        await Future<void>.delayed(Duration.zero);
        await Future<void>.delayed(Duration.zero);

        expect(
          runtimeClient.connections.state,
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
        await runtimeClient.connections.disconnect();
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

        await runtimeClient.pairing.authenticateCandidate(
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
        await runtimeClient.pairing.confirmCode(code: '123456');
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
        await runtimeClient.connections.disconnect();
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

        await runtimeClient.connections.connectCandidate(
          Uri.parse('ws://127.0.0.1:58232/'),
        );
        await Future<void>.delayed(Duration.zero);

        expect(snapshots, hasLength(1));
        expect(
          snapshots.single.single.availability,
          DovahLinkHostAvailability.unknown,
        );
        await runtimeClient.connections.disconnect();
        await Future<void>.delayed(Duration.zero);
        expect(snapshots, hasLength(1));
        await subscription.cancel();
      },
    );
  });

  group('Behavior Character state composition behaves correctly', () {
    test('Behavior Character streams replay notSubscribed state', () async {
      expect(
        (await client.currentHost.character.vitalsChanges.first).status,
        DovahLinkStateStatus.notSubscribed,
      );
      expect(
        (await client.currentHost.character.xpChanges.first).status,
        DovahLinkStateStatus.notSubscribed,
      );
      expect(
        (await client.currentHost.character.levelChanges.first).status,
        DovahLinkStateStatus.notSubscribed,
      );
      expect(
        (await client.currentHost.character.identityChanges.first).status,
        DovahLinkStateStatus.notSubscribed,
      );
      expect(
        (await client.currentHost.character.supernaturalTraitsChanges.first)
            .status,
        DovahLinkStateStatus.notSubscribed,
      );
    });

    test(
      'Behavior wire Snapshots reach coherent Vitals, XP, and Level views with recovery intact',
      () async {
        await _connectAndTrustedHello(transport, client, storage);
        await _subscribeStateAreas(
          transport,
          client,
          DovahLinkStateArea.values,
        );

        final Future<void> vitalsReceived = expectLater(
          client.currentHost.character.vitalsChanges,
          emitsThrough(
            predicate<StateSynchronization<CharacterVitalsState>>(
              (StateSynchronization<CharacterVitalsState> state) =>
                  state.status == DovahLinkStateStatus.synchronized &&
                  state.revision == 1 &&
                  state.value?.health?.current == 327.0 &&
                  state.value?.health?.max == 410.0 &&
                  state.value?.magicka?.current == 180.0 &&
                  state.value?.magicka?.max == 250.0 &&
                  state.value?.stamina?.current == 120.0 &&
                  state.value?.stamina?.max == 190.0,
            ),
          ),
        );
        final Future<void> xpReceived = expectLater(
          client.currentHost.character.xpChanges,
          emitsThrough(
            predicate<StateSynchronization<CharacterXpState>>(
              (StateSynchronization<CharacterXpState> state) =>
                  state.status == DovahLinkStateStatus.synchronized &&
                  state.value?.value == 42.5,
            ),
          ),
        );
        final Future<void> identityReceived = expectLater(
          client.currentHost.character.identityChanges,
          emitsThrough(
            predicate<StateSynchronization<CharacterIdentityState?>>(
              (StateSynchronization<CharacterIdentityState?> state) =>
                  state.status == DovahLinkStateStatus.synchronized &&
                  state.revision == 1 &&
                  state.value?.name == 'Gonçalo' &&
                  state.value?.race == 'Nord' &&
                  state.stateAuthorityId == 'authority-1' &&
                  state.playContextId == 'context-1',
            ),
          ),
        );
        final Future<void> supernaturalTraitsReceived = expectLater(
          client.currentHost.character.supernaturalTraitsChanges,
          emitsThrough(
            predicate<StateSynchronization<CharacterSupernaturalTraitsState?>>(
              (StateSynchronization<CharacterSupernaturalTraitsState?> state) =>
                  state.status == DovahLinkStateStatus.synchronized &&
                  state.revision == 1 &&
                  state.value?.isVampire == false &&
                  state.value?.hasVampireLordForm == false &&
                  state.value?.hasWerewolfForm == false &&
                  state.stateAuthorityId == 'authority-1' &&
                  state.playContextId == 'context-1',
            ),
          ),
        );
        final Future<void> levelReceived = expectLater(
          client.currentHost.character.levelChanges,
          emitsThrough(
            predicate<StateSynchronization<CharacterLevelState>>(
              (StateSynchronization<CharacterLevelState> state) =>
                  state.status == DovahLinkStateStatus.synchronized &&
                  state.value?.value == 10,
            ),
          ),
        );
        final Future<void> levelEventReceived = expectLater(
          client.currentHost.character.levelChanges,
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
          _rawStateSnapshot(
            stateArea: 'character_xp',
            revision: 1,
            value: 42.5,
          ),
        );
        transport.queueRawResponse(
          _rawStateSnapshot(
            stateArea: 'character_vitals',
            revision: 1,
            value: <String, dynamic>{
              'health': <String, dynamic>{'current': 327.0, 'max': 410.0},
              'magicka': <String, dynamic>{'current': 180.0, 'max': 250.0},
              'stamina': <String, dynamic>{'current': 120.0, 'max': 190.0},
            },
          ),
        );
        transport.queueRawResponse(
          _rawStateSnapshot(
            stateArea: 'character_identity',
            revision: 1,
            value: <String, dynamic>{'name': 'Gonçalo', 'race': 'Nord'},
          ),
        );
        transport.queueRawResponse(
          _rawStateSnapshot(
            stateArea: 'character_supernatural_traits',
            revision: 1,
            value: <String, dynamic>{
              'isVampire': false,
              'hasVampireLordForm': false,
              'hasWerewolfForm': false,
            },
          ),
        );
        transport.queueRawResponse(
          _rawStateSnapshot(
            stateArea: 'character_level',
            revision: 1,
            value: 10,
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
        await Future.wait<void>(<Future<void>>[
          vitalsReceived,
          xpReceived,
          identityReceived,
          supernaturalTraitsReceived,
          levelReceived,
          levelEventReceived,
        ]);

        transport.queueRawResponse(
          _rawStateSnapshot(
            stateArea: 'character_vitals',
            revision: 0,
            value: <String, dynamic>{
              'health': <String, dynamic>{'current': -1.0, 'max': 1.0},
              'magicka': <String, dynamic>{'current': -1.0, 'max': 1.0},
              'stamina': <String, dynamic>{'current': -1.0, 'max': 1.0},
            },
          ),
        );
        await pumpEventQueue();
        final StateSynchronization<CharacterVitalsState> retainedVitals =
            await client.currentHost.character.vitalsChanges.first;
        expect(retainedVitals.revision, 1);
        expect(retainedVitals.value?.health?.current, 327.0);

        final Future<void> unavailableVitalsReceived = expectLater(
          client.currentHost.character.vitalsChanges,
          emitsThrough(
            predicate<StateSynchronization<CharacterVitalsState>>(
              (StateSynchronization<CharacterVitalsState> state) =>
                  state.status == DovahLinkStateStatus.unavailable &&
                  state.revision == 2 &&
                  state.value?.health?.current == 327.0 &&
                  state.value?.magicka?.current == 180.0 &&
                  state.value?.stamina?.current == 120.0,
            ),
          ),
        );
        transport.queueRawResponse(
          _rawStateSnapshot(
            stateArea: 'character_vitals',
            revision: 2,
            value: null,
          ),
        );
        await unavailableVitalsReceived;

        final Future<void> recoveredLevelReceived = expectLater(
          client.currentHost.character.levelChanges,
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

        await pumpEventQueue();
        final JsonMap snapshotRequest =
            jsonDecode(transport.sent.last) as JsonMap;
        expect(snapshotRequest['messageType'], 'snapshot_request');
        expect(snapshotRequest['payload'], <String, dynamic>{
          'stateArea': 'character_level',
          'knownRevision': 2,
        });
        final StateSynchronization<CharacterLevelState> recoveredLevel =
            await client.currentHost.character.levelChanges.first;
        expect(recoveredLevel.status, DovahLinkStateStatus.synchronized);
        expect(recoveredLevel.value?.value, 14);
        expect(recoveredLevel.revision, 5);
        await recoveredLevelReceived;

        final Future<void> correlatedLevelBaselineReceived = expectLater(
          client.currentHost.character.levelChanges,
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
        await correlatedLevelBaselineReceived;

        final int sentBeforeLaterLevelEvent = transport.sent.length;
        final Future<void> laterLevelEventReceived = expectLater(
          client.currentHost.character.levelChanges,
          emitsThrough(
            predicate<StateSynchronization<CharacterLevelState>>(
              (StateSynchronization<CharacterLevelState> state) =>
                  state.status == DovahLinkStateStatus.synchronized &&
                  state.revision == 7 &&
                  state.value?.value == 16,
            ),
          ),
        );
        transport.queueRawResponse(
          _rawStateEvent(
            stateArea: 'character_level',
            baseRevision: 6,
            revision: 7,
            value: 16,
          ),
        );
        await laterLevelEventReceived;

        expect(transport.sent, hasLength(sentBeforeLaterLevelEvent));
        expect(client.connections.state, DovahLinkConnectionState.connected);
      },
    );
  });

  group('Behavior late accepted baseline recovery behaves correctly', () {
    test(
      'Behavior late accepted baseline recovery synchronizes Identity from an unsolicited Snapshot after timeout',
      () async {
        await _connectAndTrustedHello(transport, client, storage);
        final Future<void> recovering = expectLater(
          client.currentHost.character.identityChanges,
          emitsThrough(
            predicate<StateSynchronization<CharacterIdentityState?>>(
              (StateSynchronization<CharacterIdentityState?> state) =>
                  state.status == DovahLinkStateStatus.recovering,
            ),
          ),
        );
        transport.queueResponse(
          _rawSubscriptionAck(accepted: <String>['character_identity']),
        );
        await client.currentHost.subscribeStateArea(
          DovahLinkStateArea.characterIdentity,
        );
        final JsonMap subscribe = jsonDecode(transport.sent.last) as JsonMap;

        transport.queueRawResponse(
          _rawTemporarilyUnavailableError(
            correlationId: subscribe['messageId'] as String,
          ),
        );
        await recovering;
        expect(client.connections.state, DovahLinkConnectionState.connected);

        final Future<void> synchronized = expectLater(
          client.currentHost.character.identityChanges,
          emitsThrough(
            predicate<StateSynchronization<CharacterIdentityState?>>(
              (StateSynchronization<CharacterIdentityState?> state) =>
                  state.status == DovahLinkStateStatus.synchronized &&
                  state.revision == 1 &&
                  state.value?.name == 'Gonçalo' &&
                  state.value?.race == 'Nord',
            ),
          ),
        );
        final JsonMap lateSnapshot =
            jsonDecode(
                    _rawStateSnapshot(
                      stateArea: 'character_identity',
                      revision: 1,
                      value: <String, dynamic>{
                        'name': 'Gonçalo',
                        'race': 'Nord',
                      },
                    ),
                  )
                  as JsonMap
              ..['sessionId'] = 'session-paired-1';
        transport.queueRawResponse(jsonEncode(lateSnapshot));
        await synchronized;

        expect(client.connections.state, DovahLinkConnectionState.connected);
        expect(_sentSubscriptionUpdates(transport), hasLength(1));
        expect(
          transport.sent
              .map((String raw) => (jsonDecode(raw) as JsonMap)['messageType'])
              .where((Object? type) => type == 'snapshot_request'),
          isEmpty,
        );
      },
    );

    test(
      'Behavior late accepted baseline recovery establishes Level before applying its next Event',
      () async {
        await _connectAndTrustedHello(transport, client, storage);
        final Future<void> recovering = expectLater(
          client.currentHost.character.levelChanges,
          emitsThrough(
            predicate<StateSynchronization<CharacterLevelState>>(
              (StateSynchronization<CharacterLevelState> state) =>
                  state.status == DovahLinkStateStatus.recovering,
            ),
          ),
        );
        transport.queueResponse(
          _rawSubscriptionAck(accepted: <String>['character_level']),
        );
        await client.currentHost.subscribeStateArea(
          DovahLinkStateArea.characterLevel,
        );
        final JsonMap subscribe = jsonDecode(transport.sent.last) as JsonMap;

        transport.queueRawResponse(
          _rawTemporarilyUnavailableError(
            correlationId: subscribe['messageId'] as String,
          ),
        );
        await recovering;

        final Future<void> baselineReceived = expectLater(
          client.currentHost.character.levelChanges,
          emitsThrough(
            predicate<StateSynchronization<CharacterLevelState>>(
              (StateSynchronization<CharacterLevelState> state) =>
                  state.status == DovahLinkStateStatus.synchronized &&
                  state.revision == 1 &&
                  state.value?.value == 43,
            ),
          ),
        );
        final JsonMap lateSnapshot =
            jsonDecode(
                    _rawStateSnapshot(
                      stateArea: 'character_level',
                      revision: 1,
                      value: 43,
                    ),
                  )
                  as JsonMap
              ..['sessionId'] = 'session-paired-1';
        transport.queueRawResponse(jsonEncode(lateSnapshot));
        await baselineReceived;

        final Future<void> eventReceived = expectLater(
          client.currentHost.character.levelChanges,
          emitsThrough(
            predicate<StateSynchronization<CharacterLevelState>>(
              (StateSynchronization<CharacterLevelState> state) =>
                  state.status == DovahLinkStateStatus.synchronized &&
                  state.revision == 2 &&
                  state.value?.value == 44,
            ),
          ),
        );
        final JsonMap lateEvent =
            jsonDecode(
                    _rawStateEvent(
                      stateArea: 'character_level',
                      baseRevision: 1,
                      revision: 2,
                      value: 44,
                    ),
                  )
                  as JsonMap
              ..['sessionId'] = 'session-paired-1';
        transport.queueRawResponse(jsonEncode(lateEvent));
        await eventReceived;

        expect(client.connections.state, DovahLinkConnectionState.connected);
        expect(_sentSubscriptionUpdates(transport), hasLength(1));
        expect(
          transport.sent
              .map((String raw) => (jsonDecode(raw) as JsonMap)['messageType'])
              .where((Object? type) => type == 'snapshot_request'),
          isEmpty,
        );
      },
    );
  });

  group('Method queueResponsesForNextSend behaves correctly', () {
    test(
      'Method queueResponsesForNextSend rejects a second queued response batch',
      () {
        transport.queueResponsesForNextSend(<String>['{}']);

        expect(
          () => transport.queueResponsesForNextSend(<String>['{}']),
          throwsA(isA<StateError>()),
        );
      },
    );
  });

  group('Behavior fresh client baseline convergence behaves correctly', () {
    // Host-style current state for all eight required areas, in the app's request order.
    final List<({String stateArea, Object? value})>
    hostState = <({String stateArea, Object? value})>[
      (
        stateArea: 'character_vitals',
        value: _stateFixtureValue('state/state-snapshot-character-vitals.json'),
      ),
      (stateArea: 'character_xp', value: 42.5),
      (stateArea: 'character_level', value: 15),
      (
        stateArea: 'character_identity',
        value: _stateFixtureValue(
          'state/state-snapshot-character-identity.json',
        ),
      ),
      (
        stateArea: 'character_supernatural_traits',
        value: _stateFixtureValue(
          'state/state-snapshot-character-supernatural-traits.json',
        ),
      ),
      (
        stateArea: 'player_location',
        value: _stateFixtureValue('state/state-snapshot-player-location.json'),
      ),
      (
        stateArea: 'game_time',
        value: _stateFixtureValue('state/state-snapshot-game-time.json'),
      ),
      (
        stateArea: 'tracked_quests',
        value: _stateFixtureValue('state/state-snapshot-tracked-quests.json'),
      ),
    ];

    /// Requests the eight areas the way the app does -- one additive call per area, awaiting each
    /// ACK with no extra event-queue turn -- while the Host answers each request with its ACK and
    /// that area's current baseline back to back.
    Future<List<StateSynchronization<Object?>>> requestAllFromHost(
      FakeDovahLinkTransport hostTransport,
      DovahLinkClient hostClient,
    ) async {
      final Set<String> desired = <String>{};
      for (final ({String stateArea, Object? value}) baseline in hostState) {
        desired.add(baseline.stateArea);
        hostTransport.queueResponsesForNextSend(<String>[
          _rawSubscriptionAck(
            accepted: <String>[
              for (final DovahLinkStateArea area in DovahLinkStateArea.values)
                if (desired.contains(area.protocolValue)) area.protocolValue,
            ],
          ),
          _rawStateSnapshot(
            stateArea: baseline.stateArea,
            revision: 4,
            value: baseline.value,
            correlationId: 'subscribe-placeholder',
          ),
        ]);
        expect(
          await hostClient.currentHost.subscribeStateArea(
            DovahLinkStateArea.values.firstWhere(
              (DovahLinkStateArea area) =>
                  area.protocolValue == baseline.stateArea,
            ),
          ),
          isEmpty,
        );
      }
      await pumpEventQueue();
      final IDovahLinkCurrentHost host = hostClient.currentHost;
      return <StateSynchronization<Object?>>[
        await host.character.vitalsChanges.first,
        await host.character.xpChanges.first,
        await host.character.levelChanges.first,
        await host.character.identityChanges.first,
        await host.character.supernaturalTraitsChanges.first,
        await host.playerLocationChanges.first,
        await host.gameTimeChanges.first,
        await host.trackedQuestsChanges.first,
      ];
    }

    void expectAllSynchronized(List<StateSynchronization<Object?>> states) {
      expect(states, hasLength(8));
      for (final StateSynchronization<Object?> state in states) {
        expect(state.status, DovahLinkStateStatus.synchronized);
        expect(state.revision, 4);
        expect(state.value, isNotNull);
      }
    }

    test(
      'Behavior fresh client baseline convergence synchronizes all eight areas for two fresh clients when each ACK is followed by its baseline',
      () async {
        await _connectAndTrustedHello(transport, client, storage);
        expectAllSynchronized(await requestAllFromHost(transport, client));

        final FakeDovahLinkTransport secondTransport = FakeDovahLinkTransport();
        final InMemoryClientStorage secondStorage = InMemoryClientStorage();
        final DovahLinkClient secondClient = buildDovahLinkClientForTesting(
          transport: secondTransport,
          storage: secondStorage,
        );
        addTearDown(secondClient.close);
        await _connectAndTrustedHello(
          secondTransport,
          secondClient,
          secondStorage,
        );
        expectAllSynchronized(
          await requestAllFromHost(secondTransport, secondClient),
        );
      },
    );

    test(
      'Behavior fresh client baseline convergence synchronizes all eight areas when the transport delivers the ACK and baseline with no turn between frames',
      () async {
        transport.synchronousDelivery = true;
        await _connectAndTrustedHello(transport, client, storage);
        expectAllSynchronized(await requestAllFromHost(transport, client));
      },
    );
  });

  group('Behavior accepted subscription baselines behave correctly', () {
    test(
      'Behavior accepted subscription baselines synchronize all eight domains when each Snapshot follows its ACK',
      () async {
        await _connectAndTrustedHello(transport, client, storage);
        final List<({String stateArea, Object? value})> baselines =
            <({String stateArea, Object? value})>[
              (stateArea: 'character_xp', value: 42.5),
              (
                stateArea: 'character_vitals',
                value: _stateFixtureValue(
                  'state/state-snapshot-character-vitals.json',
                ),
              ),
              (stateArea: 'character_level', value: 15),
              (
                stateArea: 'character_identity',
                value: _stateFixtureValue(
                  'state/state-snapshot-character-identity.json',
                ),
              ),
              (
                stateArea: 'character_supernatural_traits',
                value: _stateFixtureValue(
                  'state/state-snapshot-character-supernatural-traits.json',
                ),
              ),
              (
                stateArea: 'player_location',
                value: _stateFixtureValue(
                  'state/state-snapshot-player-location.json',
                ),
              ),
              (
                stateArea: 'game_time',
                value: _stateFixtureValue(
                  'state/state-snapshot-game-time.json',
                ),
              ),
              (
                stateArea: 'tracked_quests',
                value: _stateFixtureValue(
                  'state/state-snapshot-tracked-quests-empty.json',
                ),
              ),
            ];
        final Set<String> desiredAreas = <String>{};

        for (final ({String stateArea, Object? value}) baseline in baselines) {
          desiredAreas.add(baseline.stateArea);
          transport.queueResponsesForNextSend(<String>[
            _rawSubscriptionAck(
              accepted: <String>[
                for (final DovahLinkStateArea area in DovahLinkStateArea.values)
                  if (desiredAreas.contains(area.protocolValue))
                    area.protocolValue,
              ],
            ),
            _rawStateSnapshot(
              stateArea: baseline.stateArea,
              revision: 1,
              value: baseline.value,
              correlationId: 'subscribe-placeholder',
            ),
          ]);
          expect(
            await client.currentHost.subscribeStateArea(
              DovahLinkStateArea.values.firstWhere(
                (DovahLinkStateArea area) =>
                    area.protocolValue == baseline.stateArea,
              ),
            ),
            isEmpty,
          );
          await pumpEventQueue();
        }

        transport.queueResponsesForNextSend(<String>[
          _rawSubscriptionAck(
            accepted: <String>[
              for (final DovahLinkStateArea area in DovahLinkStateArea.values)
                if (desiredAreas.contains(area.protocolValue))
                  area.protocolValue,
            ],
          ),
        ]);
        await client.currentHost.subscribeStateArea(
          DovahLinkStateArea.trackedQuests,
        );
        await pumpEventQueue();

        expect(
          (await client.currentHost.character.xpChanges.first).status,
          DovahLinkStateStatus.synchronized,
        );
        expect(
          (await client.currentHost.character.vitalsChanges.first).status,
          DovahLinkStateStatus.synchronized,
        );
        expect(
          (await client.currentHost.character.levelChanges.first).status,
          DovahLinkStateStatus.synchronized,
        );
        expect(
          (await client.currentHost.character.identityChanges.first).status,
          DovahLinkStateStatus.synchronized,
        );
        expect(
          (await client.currentHost.character.supernaturalTraitsChanges.first)
              .status,
          DovahLinkStateStatus.synchronized,
        );
        expect(
          (await client.currentHost.playerLocationChanges.first).status,
          DovahLinkStateStatus.synchronized,
        );
        expect(
          (await client.currentHost.gameTimeChanges.first).status,
          DovahLinkStateStatus.synchronized,
        );
        expect(
          (await client.currentHost.trackedQuestsChanges.first).status,
          DovahLinkStateStatus.synchronized,
        );
      },
    );

    test(
      'Behavior accepted subscription baselines reject timeout errors after that area baseline is applied',
      () async {
        await _connectAndTrustedHello(transport, client, storage);
        transport.queueResponsesForNextSend(<String>[
          _rawSubscriptionAck(accepted: <String>['character_xp']),
          _rawStateSnapshot(
            stateArea: 'character_xp',
            revision: 1,
            value: 42.5,
            correlationId: 'subscribe-placeholder',
          ),
        ]);
        await client.currentHost.subscribeStateArea(
          DovahLinkStateArea.characterXp,
        );
        await pumpEventQueue();

        final JsonMap subscribeRequest = _sentSubscriptionUpdates(
          transport,
        ).single;
        transport.queueRawResponse(
          _rawTemporarilyUnavailableError(
            correlationId: subscribeRequest['messageId'] as String,
          ),
        );
        await pumpEventQueue();

        expect(client.connections.state, DovahLinkConnectionState.disconnected);
      },
    );

    test(
      'Behavior accepted subscription baselines keep the session healthy after a delayed retryable unavailable error',
      () async {
        await _connectAndTrustedHello(transport, client, storage);
        transport.queueResponse(
          _rawSubscriptionAck(accepted: <String>['character_xp']),
        );
        await client.currentHost.subscribeStateArea(
          DovahLinkStateArea.characterXp,
        );
        final JsonMap subscribeRequest = _sentSubscriptionUpdates(
          transport,
        ).single;
        transport.queueRawResponse(
          _rawTemporarilyUnavailableError(
            correlationId: subscribeRequest['messageId'] as String,
          ),
        );
        await pumpEventQueue();

        final StateSynchronization<CharacterXpState> state =
            await client.currentHost.character.xpChanges.first;
        expect(state.status, DovahLinkStateStatus.recovering);
        expect(client.connections.state, DovahLinkConnectionState.connected);
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
            await client.currentHost.subscribeStateArea(
              DovahLinkStateArea.characterXp,
            ),
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
            (await client.currentHost.character.xpChanges.first).status,
            DovahLinkStateStatus.notSubscribed,
          );
          expect(client.connections.state, DovahLinkConnectionState.connected);
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
            await client.currentHost.subscribeStateArea(
              DovahLinkStateArea.characterXp,
            ),
            isEmpty,
          );
          expect(
            (jsonDecode(transport.sent.last) as JsonMap)['payload'],
            (jsonDecode(_rawFixture('subscriptions/subscribe.json'))
                as JsonMap)['payload'],
          );
          expect(
            (await client.currentHost.character.xpChanges.first).status,
            DovahLinkStateStatus.recovering,
          );

          transport.queueResponse(
            _rawSubscriptionAck(
              accepted: <String>['character_xp', 'character_vitals'],
            ),
          );
          expect(
            await client.currentHost.subscribeStateArea(
              DovahLinkStateArea.characterVitals,
            ),
            isEmpty,
          );
          expect(
            (jsonDecode(transport.sent.last) as JsonMap)['payload'],
            <String, dynamic>{
              'stateAreas': <String>['character_xp', 'character_vitals'],
            },
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
              stateArea: 'character_vitals',
              revision: 1,
              value: <String, dynamic>{
                'health': <String, dynamic>{'current': 327.0, 'max': 410.0},
                'magicka': <String, dynamic>{'current': 180.0, 'max': 250.0},
                'stamina': <String, dynamic>{'current': 120.0, 'max': 190.0},
              },
            ),
          );
          await pumpEventQueue();
          expect(
            (await client.currentHost.character.xpChanges.first).status,
            DovahLinkStateStatus.synchronized,
          );
          expect(
            (await client.currentHost.character.vitalsChanges.first).status,
            DovahLinkStateStatus.synchronized,
          );

          transport.queueResponse(
            _rawSubscriptionAck(accepted: <String>['character_vitals']),
          );
          expect(
            await client.currentHost.unsubscribeStateArea(
              DovahLinkStateArea.characterXp,
            ),
            isEmpty,
          );
          expect(
            (jsonDecode(transport.sent.last) as JsonMap)['payload'],
            <String, dynamic>{
              'stateAreas': <String>['character_vitals'],
            },
          );
          expect(
            (await client.currentHost.character.xpChanges.first).status,
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
            (await client.currentHost.character.xpChanges.first).status,
            DovahLinkStateStatus.notSubscribed,
          );
          expect(client.connections.state, DovahLinkConnectionState.connected);

          transport.queueResponse(
            _rawSubscriptionAck(accepted: const <String>[]),
          );
          expect(
            await client.currentHost.unsubscribeStateArea(
              DovahLinkStateArea.characterVitals,
            ),
            isEmpty,
          );
          expect(
            (jsonDecode(transport.sent.last) as JsonMap)['payload'],
            (jsonDecode(_rawFixture('subscriptions/subscribe-empty.json'))
                as JsonMap)['payload'],
          );
          expect(
            (await client.currentHost.character.vitalsChanges.first).status,
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
          await reconnectClient.connections.disconnect();
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

        final Future<void> disconnect = reconnectClient.connections
            .disconnect();
        closeGate.complete();
        await disconnect;
        for (int attempt = 0; attempt < 10; attempt++) {
          await pumpEventQueue();
        }

        expect(
          reconnectClient.connections.state,
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
        addTearDown(reconnectClient.connections.disconnect);
        await _connectAndTrustedHello(
          reconnectTransport,
          reconnectClient,
          reconnectStorage,
        );
        await reconnectClient.connections.disconnect();
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
                  reconnectClient.connections.state !=
                      DovahLinkConnectionState.connected);
          attempt++
        ) {
          await pumpEventQueue();
        }

        expect(reconnectTransport.connectCalls, hasLength(3));
        expect(
          reconnectClient.connections.state,
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
          client.currentHost.subscribeStateArea(DovahLinkStateArea.characterXp),
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
          await client.pairing.recoverPendingPairing(),
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
      'Behavior trusted initial admission restores remembered subscriptions once',
      () async {
        await expectLater(
          client.currentHost.subscribeStateArea(DovahLinkStateArea.characterXp),
          throwsA(isA<DovahLinkConnectionException>()),
        );
        await storage.save(
          Fixtures.buildPersistedClientState(
            clientId: 'client-1',
            hostId: '81869993-955c-4ba3-a7d0-d35ca86078ea',
            hostName: 'Soneka-Desktop',
            credential: 'trusted-credential',
          ),
        );
        transport.queueResponse(
          _rawFixture('connection/hello-ack-paired.json'),
        );
        transport.queueResponse(
          _rawFixture('capabilities/capabilities-host.json'),
        );
        transport.queueResponse(
          _rawSubscriptionAck(accepted: <String>['character_xp']),
        );

        await client.connections.connectKnownHost(
          DovahLinkHostId('81869993-955c-4ba3-a7d0-d35ca86078ea'),
        );
        for (
          int attempt = 0;
          attempt < 20 && _sentSubscriptionUpdates(transport).isEmpty;
          attempt++
        ) {
          await pumpEventQueue();
        }
        await pumpEventQueue();

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
      'Behavior an unpaired admission does not restore remembered subscriptions',
      () async {
        await expectLater(
          client.currentHost.subscribeStateArea(DovahLinkStateArea.characterXp),
          throwsA(isA<DovahLinkConnectionException>()),
        );

        await _connectAndHello(transport, client);
        for (int attempt = 0; attempt < 10; attempt++) {
          await pumpEventQueue();
        }

        expect(client.currentHost.trustState, DovahLinkTrustState.unpaired);
        expect(_sentSubscriptionUpdates(transport), isEmpty);
      },
    );

    test(
      'Behavior candidate authentication that recovers a pending pairing restores remembered subscriptions',
      () async {
        await storage.save(
          Fixtures.buildPersistedClientState(
            clientId: 'client-1',
            credential: 'a1b2c3d4e5f6',
            recoveryState: PairingRecoveryState.confirming,
          ),
        );
        await expectLater(
          client.currentHost.subscribeStateArea(DovahLinkStateArea.characterXp),
          throwsA(isA<DovahLinkConnectionException>()),
        );
        transport.queueResponse(_rawFixture('connection/hello-ack.json'));
        transport.queueResponse(
          _rawFixture('capabilities/capabilities-host.json'),
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

        final DovahLinkPairingHandshake handshake = await client.pairing
            .authenticateCandidate(Uri.parse('ws://127.0.0.1:58231/'));
        for (
          int attempt = 0;
          attempt < 20 && _sentSubscriptionUpdates(transport).isEmpty;
          attempt++
        ) {
          await pumpEventQueue();
        }

        expect(handshake.hello.trustState, DovahLinkTrustState.unpaired);
        expect(handshake.trustState, DovahLinkTrustState.trusted);
        expect(_sentSubscriptionUpdates(transport), hasLength(1));
      },
    );

    test(
      'Behavior ordinary reconnect restores only desired areas, retains their value as recovering, and replaces it with a fresh Snapshot',
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
            DovahLinkStateArea.characterVitals,
          ],
        );

        reconnectTransport.queueResponse(
          _rawSubscriptionAck(accepted: <String>['character_vitals']),
        );
        await reconnectClient.currentHost.unsubscribeStateArea(
          DovahLinkStateArea.characterXp,
        );
        reconnectTransport.queueRawResponse(
          _rawStateSnapshot(
            stateArea: 'character_vitals',
            revision: 7,
            value: <String, dynamic>{
              'health': <String, dynamic>{'current': 87.5, 'max': 410.0},
              'magicka': <String, dynamic>{'current': 180.0, 'max': 250.0},
              'stamina': <String, dynamic>{'current': 120.0, 'max': 190.0},
            },
          ),
        );
        await pumpEventQueue();
        expect(
          (await reconnectClient.currentHost.character.vitalsChanges.first)
              .status,
          DovahLinkStateStatus.synchronized,
        );

        reconnectTransport.queueResponse(
          _rawFixture('connection/hello-ack-paired.json'),
        );
        reconnectTransport.queueResponse(
          _rawFixture('capabilities/capabilities-host.json'),
        );
        reconnectTransport.queueResponse(
          _rawSubscriptionAck(accepted: <String>['character_vitals']),
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
          'stateAreas': <String>['character_vitals'],
        });
        final StateSynchronization<CharacterVitalsState> retained =
            await reconnectClient.currentHost.character.vitalsChanges.first;
        expect(retained.status, DovahLinkStateStatus.recovering);
        expect(retained.revision, 7);
        expect(retained.value?.health?.current, 87.5);
        expect(
          (await reconnectClient.currentHost.character.xpChanges.first).status,
          DovahLinkStateStatus.notSubscribed,
        );

        reconnectTransport.queueRawResponse(
          _rawStateSnapshot(
            stateArea: 'character_vitals',
            revision: 8,
            value: <String, dynamic>{
              'health': <String, dynamic>{'current': 75.0, 'max': 410.0},
              'magicka': <String, dynamic>{'current': 180.0, 'max': 250.0},
              'stamina': <String, dynamic>{'current': 120.0, 'max': 190.0},
            },
            correlationId:
                _sentSubscriptionUpdates(reconnectTransport).last['messageId']
                    as String,
          ),
        );
        await pumpEventQueue();
        final StateSynchronization<CharacterVitalsState> recovered =
            await reconnectClient.currentHost.character.vitalsChanges.first;
        expect(recovered.status, DovahLinkStateStatus.synchronized);
        expect(recovered.revision, 8);
        expect(recovered.value?.health?.current, 75.0);
        expect(
          (await reconnectClient.currentHost.character.xpChanges.first).status,
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
          (await client.currentHost.character.xpChanges.first).status,
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
          client.connections.state,
          DovahLinkConnectionState.administrativelyInvalidated,
        );
        expect(transport.connectCalls, hasLength(1));
        expect(_sentSubscriptionUpdates(transport), hasLength(1));
        expect(
          (await client.currentHost.character.xpChanges.first).status,
          DovahLinkStateStatus.notSubscribed,
        );

        transport.queueResponse(_rawFixture('connection/hello-ack.json'));
        transport.queueResponse(
          _rawFixture('capabilities/capabilities-host.json'),
        );
        final HelloResult retry = await client.connections.connectCandidate(
          Uri.parse('ws://127.0.0.1:58231/'),
        );
        expect(retry.trustState, DovahLinkTrustState.unpaired);
        expect(_sentSubscriptionUpdates(transport), hasLength(1));

        transport.queueResponse(
          _rawFixture('pairing/pairing-status-available.json'),
        );
        await client.pairing.requestCode();
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
          (await client.currentHost.character.xpChanges.first).status,
          DovahLinkStateStatus.synchronized,
        );
        expect(client.currentHost.trustState, DovahLinkTrustState.trusted);
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
              reconnectClient.connections.state !=
                  DovahLinkConnectionState.reconnecting;
          attempt++
        ) {
          await pumpEventQueue();
        }
        expect(
          reconnectClient.connections.state,
          DovahLinkConnectionState.reconnecting,
        );

        await reconnectClient.connections.disconnect();
        expect(
          reconnectClient.connections.state,
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
          (await reconnectClient.currentHost.character.xpChanges.first).status,
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
        addTearDown(defaultClient.connections.disconnect);
        final Future<WebSocket> acceptedSocket = server.connections.first
            .timeout(timeout);

        final Future<HelloResult> hello = defaultClient.connections
            .connectCandidate(server.uri)
            .timeout(timeout);
        final WebSocket socket = await acceptedSocket;
        addTearDown(socket.close);

        final Completer<String> requestFrame = Completer<String>();
        socket.listen((Object? message) {
          if (message is String && !requestFrame.isCompleted) {
            requestFrame.complete(message);
          }
        });
        final JsonMap request =
            jsonDecode(await requestFrame.future.timeout(timeout)) as JsonMap;
        final JsonMap helloAck =
            jsonDecode(_rawFixture('connection/hello-ack.json')) as JsonMap;
        helloAck['correlationId'] = request['messageId'];
        socket.add(jsonEncode(helloAck));
        socket.add(_rawFixture('capabilities/capabilities-host.json'));

        final HelloResult result = await hello;

        expect(
          defaultClient.connections.state,
          DovahLinkConnectionState.connected,
        );
        expect(result.hostVersion, '0.6.0');
        expect(result.hostId, '81869993-955c-4ba3-a7d0-d35ca86078ea');
        expect(result.hostName, 'Soneka-Desktop');
        expect(result.trustState, DovahLinkTrustState.unpaired);
      },
    );
  });

  group(
    'Behavior gameplay readiness over the default WebSocket transport behaves correctly',
    () {
      test(
        'Behavior readiness sent right after hello_ack follows Host reports and then transport loss',
        () async {
          const Duration timeout = Duration(seconds: 5);
          final FakeWebSocketServer server = await FakeWebSocketServer.start()
              .timeout(timeout);
          addTearDown(server.close);
          // Reconnection is disabled so the closed socket ends the test's connection for good
          // instead of starting a retry loop against this server.
          final DovahLinkClient realClient = buildDovahLinkClientForTesting(
            transport: WebSocketTransport(),
            storage: InMemoryClientStorage(),
            reconnectEnabled: false,
          );
          addTearDown(realClient.close);
          addTearDown(realClient.connections.disconnect);
          final List<DovahLinkGameplayReadiness> seen = _recordReadiness(
            realClient,
          );
          final Future<WebSocket> acceptedSocket = server.connections.first
              .timeout(timeout);

          final Future<HelloResult> hello = realClient.connections
              .connectCandidate(server.uri)
              .timeout(timeout);
          final WebSocket socket = await acceptedSocket;
          addTearDown(socket.close);
          final Completer<String> requestFrame = Completer<String>();
          socket.listen((Object? message) {
            if (message is String && !requestFrame.isCompleted) {
              requestFrame.complete(message);
            }
          });
          final JsonMap request =
              jsonDecode(await requestFrame.future.timeout(timeout)) as JsonMap;
          final JsonMap helloAck =
              jsonDecode(_rawFixture('connection/hello-ack.json')) as JsonMap;
          helloAck['correlationId'] = request['messageId'];

          // The Host writes all three frames back to back, as it does on every admission.
          socket.add(jsonEncode(helloAck));
          socket.add(_rawFixture('capabilities/capabilities-host.json'));
          socket.add(_rawReadiness(ready: false, sequence: 1));
          await hello;

          await realClient.currentHost.gameplayReadinessChanges
              .firstWhere(
                (DovahLinkGameplayReadiness value) =>
                    value == DovahLinkGameplayReadiness.notReady,
              )
              .timeout(timeout);
          expect(
            realClient.connections.state,
            DovahLinkConnectionState.connected,
          );
          expect(
            realClient.currentHost.trustState,
            DovahLinkTrustState.unpaired,
          );

          socket.add(_rawReadiness(ready: true, sequence: 2));
          await realClient.currentHost.gameplayReadinessChanges
              .firstWhere(
                (DovahLinkGameplayReadiness value) =>
                    value == DovahLinkGameplayReadiness.ready,
              )
              .timeout(timeout);
          socket.add(_rawReadiness(ready: false, sequence: 3));
          await realClient.currentHost.gameplayReadinessChanges
              .firstWhere(
                (DovahLinkGameplayReadiness value) =>
                    value == DovahLinkGameplayReadiness.notReady,
              )
              .timeout(timeout);
          expect(
            realClient.connections.state,
            DovahLinkConnectionState.connected,
          );

          await socket.close();
          await realClient.currentHost.gameplayReadinessChanges
              .firstWhere(
                (DovahLinkGameplayReadiness value) =>
                    value == DovahLinkGameplayReadiness.unknown,
              )
              .timeout(timeout);
          expect(
            realClient.currentHost.gameplayReadiness,
            DovahLinkGameplayReadiness.unknown,
          );
          expect(seen, <DovahLinkGameplayReadiness>[
            DovahLinkGameplayReadiness.unknown,
            DovahLinkGameplayReadiness.notReady,
            DovahLinkGameplayReadiness.ready,
            DovahLinkGameplayReadiness.notReady,
            DovahLinkGameplayReadiness.unknown,
          ]);
        },
      );

      test(
        'Behavior readiness that arrives while trusted admission awaits storage is kept and published once admitted',
        () async {
          const Duration timeout = Duration(seconds: 5);
          final FakeWebSocketServer server = await FakeWebSocketServer.start()
              .timeout(timeout);
          addTearDown(server.close);
          final HoldableClientStorage holdable = HoldableClientStorage();
          await holdable.save(
            Fixtures.buildPersistedClientState(
              clientId: 'client-1',
              host: Fixtures.buildDovahLinkHost(
                hostName: 'Renamed-Desktop',
                endpoint: server.uri.toString(),
              ),
              credential: 'trusted-credential',
            ),
          );
          final DovahLinkClient realClient = buildDovahLinkClientForTesting(
            transport: WebSocketTransport(),
            storage: holdable,
          );
          addTearDown(realClient.close);
          addTearDown(realClient.connections.disconnect);
          final Future<WebSocket> acceptedSocket = server.connections.first
              .timeout(timeout);
          holdable.saveGate = Completer<void>();
          addTearDown(() {
            if (!holdable.saveGate!.isCompleted) {
              holdable.saveGate!.complete();
            }
          });

          final Future<HelloResult> hello = realClient.connections
              .connectKnownHost(
                DovahLinkHostId('81869993-955c-4ba3-a7d0-d35ca86078ea'),
              )
              .timeout(timeout);
          final WebSocket socket = await acceptedSocket;
          addTearDown(socket.close);
          final Completer<String> requestFrame = Completer<String>();
          socket.listen((Object? message) {
            if (message is String && !requestFrame.isCompleted) {
              requestFrame.complete(message);
            }
          });
          final JsonMap request =
              jsonDecode(await requestFrame.future.timeout(timeout)) as JsonMap;
          final JsonMap helloAck =
              jsonDecode(_rawFixture('connection/hello-ack-paired.json'))
                  as JsonMap;
          helloAck['correlationId'] = request['messageId'];

          socket.add(jsonEncode(helloAck));
          socket.add(_rawFixture('capabilities/capabilities-host.json'));
          socket.add(
            _rawReadiness(
              ready: true,
              sequence: 1,
              sessionId: 'session-paired-1',
            ),
          );
          for (int turn = 0; turn < 40; turn++) {
            await pumpEventQueue();
          }

          expect(realClient.currentHost.sessionId, isNull);
          expect(
            realClient.currentHost.gameplayReadiness,
            DovahLinkGameplayReadiness.unknown,
          );

          holdable.saveGate!.complete();
          await hello;
          await realClient.currentHost.gameplayReadinessChanges
              .firstWhere(
                (DovahLinkGameplayReadiness value) =>
                    value == DovahLinkGameplayReadiness.ready,
              )
              .timeout(timeout);
          expect(realClient.currentHost.sessionId, 'session-paired-1');
          expect(
            realClient.currentHost.trustState,
            DovahLinkTrustState.trusted,
          );
        },
      );
    },
  );

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
        final JsonMap helloAck =
            jsonDecode(_rawFixture('connection/hello-ack.json')) as JsonMap;
        (helloAck['payload'] as JsonMap)['hostId'] = reportedHostId;
        transport.queueResponse(jsonEncode(helloAck));

        await expectLater(
          client.connections.connectCandidate(
            Uri.parse('ws://127.0.0.1:58231/'),
          ),
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
        expect(client.connections.state, DovahLinkConnectionState.disconnected);
        expect(client.currentHost.sessionId, isNull);
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
        final String helloAckFixture = _rawFixture('connection/hello-ack.json');
        final JsonMap helloAckPayload =
            (jsonDecode(helloAckFixture) as JsonMap)['payload'] as JsonMap;
        transport.queueResponse(helloAckFixture);
        transport.queueResponse(
          _rawFixture('capabilities/capabilities-host.json'),
        );

        final HelloResult result = await client.connections.connectCandidate(
          Uri.parse('ws://127.0.0.1:58231/'),
        );

        expect(result.hostVersion, helloAckPayload['hostVersion'] as String);
        expect(result.hostId, helloAckPayload['hostId'] as String);
        expect(result.hostName, helloAckPayload['hostName'] as String);
        expect(result.trustState, DovahLinkTrustState.unpaired);
        expect(client.currentHost.trustState, DovahLinkTrustState.unpaired);
        expect(client.currentHost.sessionId, 'session-1');
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
          client.connections.connectCandidate(
            Uri.parse('ws://127.0.0.1:58231/'),
          ),
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
        expect(client.currentHost.trustState, isNull);
        expect(client.currentHost.sessionId, isNull);
        // The host already closed this socket (every HandleHello failure path does); the
        // transport must be reset so the next connect() attempt does not find a stale socket
        // WebSocketTransport still considers open.
        expect(transport.closeCalled, isTrue);
      },
    );

    test(
      'Method hello disconnects and reports an incompatible Host before exposing a session',
      () async {
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
          client.connections.connectCandidate(
            Uri.parse('ws://127.0.0.1:58231/'),
          ),
          throwsA(
            isA<DovahLinkCompatibilityException>().having(
              (DovahLinkCompatibilityException error) => error.failure,
              'failure',
              HostVersionCompatibilityFailure.hostTooOld,
            ),
          ),
        );

        expect(client.connections.state, DovahLinkConnectionState.disconnected);
        expect(client.currentHost.trustState, isNull);
        expect(client.currentHost.sessionId, isNull);
        expect(transport.closeCalled, isTrue);
      },
    );

    test(
      'Method hello the original rejection still surfaces even when cleanup itself fails',
      () async {
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
          client.connections.connectCandidate(
            Uri.parse('ws://127.0.0.1:58231/'),
          ),
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
        expect(client.connections.state, DovahLinkConnectionState.disconnected);
        expect(client.currentHost.trustState, isNull);
        expect(client.currentHost.sessionId, isNull);
      },
    );

    test('Method hello a malformed message arriving after hello succeeds still resets session state, via the '
        'background receiver', () async {
      // hello_ack resolves hello() as soon as it is correlated -- hello() does not wait for
      // capabilities. Queuing a malformed protocol message in its place proves the persistent receiver's own
      // cleanup covers state set moments earlier in this same call, not just the "never got
      // that far" case above -- even though it now runs after hello() has already returned.
      transport.queueResponse(_rawFixture('connection/hello-ack.json'));
      transport.queueResponse('not valid json');
      transport.failCloseWith = const SocketException('socket already gone');

      final HelloResult result = await client.connections.connectCandidate(
        Uri.parse('ws://127.0.0.1:58231/'),
      );
      expect(result.trustState, DovahLinkTrustState.unpaired);

      await pumpEventQueue();

      expect(transport.closeCalled, isTrue);
      expect(client.connections.state, DovahLinkConnectionState.disconnected);
      expect(client.currentHost.trustState, isNull);
      expect(client.currentHost.sessionId, isNull);
    });

    test(
      'Method hello a freshly generated clientId is still persisted even when the hello_ack is rejected',
      () async {
        transport.queueResponse(
          _rawFixture('errors/error-unauthenticated-invalid-token.json'),
        );

        await expectLater(
          client.connections.connectCandidate(
            Uri.parse('ws://127.0.0.1:58231/'),
          ),
          throwsA(isA<Exception>()),
        );

        final PersistedClientState stored = await storage.load();
        expect(stored.clientId, isNotNull);
        expect(stored.clientId, client.clientId);
      },
    );

    test(
      'Method hello a malformed JSON response throws malformed_message',
      () async {
        final Future<HelloResult> hello = client.connections.connectCandidate(
          Uri.parse('ws://127.0.0.1:58231/'),
        );
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

        await client.connections.connectCandidate(
          Uri.parse('ws://127.0.0.1:58232/'),
        );

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

        final HelloResult result = await client.connections.connectCandidate(
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
          await client.connections.disconnect();
        });
        transport.queueResponse(_rawFixture('errors/error-revoked.json'));

        final Future<HelloResult> authentication = client.connections
            .connectKnownHost(
              DovahLinkHostId('81869993-955c-4ba3-a7d0-d35ca86078ea'),
            );
        await saveStarted.future;
        await client.connections.disconnect();
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
              'hostVersion': '0.6.0',
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
        await client.connections.connectKnownHost(hostId);
        await client.connections.disconnect();

        transport.queueResponse(
          jsonEncode(<String, dynamic>{
            'messageType': 'hello_ack',
            'messageId': 'message-hello-ack-2',
            'sessionId': 'session-2',
            'correlationId': 'irrelevant',
            'payload': <String, dynamic>{
              'hostVersion': '0.6.0',
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
        await client.connections.connectKnownHost(hostId);

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

        final PairingChallengeStatus status = await client.pairing
            .requestCode();

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

        await client.pairing.requestCode();

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
        client.pairing.confirmCode(code: '123456'),
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
      await client.connections.disconnect();
      expect(client.connections.state, DovahLinkConnectionState.disconnected);
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

        final PairingRenotifyResult result = await client.pairing.renotify();

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

        final PairingCancelOutcome outcome = await client.pairing.cancel();

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

        await client.pairing.confirmCode(code: '123456', displayName: 'My PC');

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
        expect(client.currentHost.trustState, DovahLinkTrustState.trusted);
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

        await client.pairing.confirmCode(code: '123456', displayName: 'My PC');

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
          client.pairing.confirmCode(code: '000000'),
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
          client.pairing.confirmCode(code: '000000'),
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
          client.pairing.recoverPendingPairing(),
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
        transport.queueResponse(_rawFixture('connection/hello-ack.json'));
        transport.queueResponse(
          _rawFixture('capabilities/capabilities-host.json'),
        );
        await client.connections.connectCandidate(
          Uri.parse('ws://127.0.0.1:58231/'),
        );

        await client.connections.disconnect();

        expect(transport.closeCalled, isTrue);
        expect(client.connections.state, DovahLinkConnectionState.disconnected);
        expect(client.currentHost.trustState, isNull);
        expect(client.currentHost.sessionId, isNull);
      },
    );

    test(
      'Method disconnect is observable through connectionStateChanges for an ordinary, '
      'non-administrative transition',
      () async {
        final List<DovahLinkConnectionState> observed =
            <DovahLinkConnectionState>[];
        final StreamSubscription<DovahLinkConnectionState> subscription = client
            .connections
            .stateChanges
            .listen(observed.add);
        addTearDown(subscription.cancel);

        transport.queueResponse(_rawFixture('connection/hello-ack.json'));
        transport.queueResponse(
          _rawFixture('capabilities/capabilities-host.json'),
        );
        await client.connections.connectCandidate(
          Uri.parse('ws://127.0.0.1:58231/'),
        );

        await client.connections.disconnect();
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

      await client.connections.disconnect();

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
          .hosts
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
        transport.queueResponse(_rawFixture('connection/hello-ack.json'));
        transport.queueResponse(
          _rawFixture('capabilities/capabilities-host.json'),
        );

        await client.connections.connectCandidate(
          Uri.parse('ws://127.0.0.1:58231/'),
        );

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
        transport.queueResponse(_rawFixture('connection/hello-ack.json'));
        transport.queueResponse(
          _rawFixture('capabilities/capabilities-host.json'),
        );
        final HelloResult helloResult = await client.connections
            .connectCandidate(Uri.parse('ws://127.0.0.1:58231/'));
        expect(helloResult.trustState, DovahLinkTrustState.unpaired);

        transport.queueResponse(
          _rawFixture('pairing/pairing-status-available.json'),
        );
        final PairingChallengeStatus status = await client.pairing
            .requestCode();

        expect(status.availability, PairingAvailability.available);
        expect(transport.sent, hasLength(2));
      },
    );

    test('Behavior inbound message routing does not consume an unsolicited message as a pending '
        'request reply', () async {
      // capabilities is queued before hello-ack here, unlike every other test above, to prove
      // the router does not treat "whatever arrives first" as the pending operation's reply.
      transport.queueResponse(
        _rawFixture('capabilities/capabilities-host.json'),
      );
      transport.queueResponse(_rawFixture('connection/hello-ack.json'));

      final HelloResult result = await client.connections.connectCandidate(
        Uri.parse('ws://127.0.0.1:58231/'),
      );

      expect(result.trustState, DovahLinkTrustState.unpaired);
    });

    test(
      'Behavior inbound message routing fails closed for an unmatched correlationId',
      () async {
        final Future<HelloResult> helloFuture = client.connections
            .connectCandidate(Uri.parse('ws://127.0.0.1:58231/'));
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
        expect(client.connections.state, DovahLinkConnectionState.disconnected);
        expect(transport.closeCalled, isTrue);
      },
    );

    test(
      'Behavior inbound message routing contains malformed background JSON as a protocol failure',
      () async {
        final List<Object> uncaughtErrors = <Object>[];
        final Completer<void> done = Completer<void>();

        runZonedGuarded(() async {
          transport.queueResponse(_rawFixture('connection/hello-ack.json'));
          await client.connections.connectCandidate(
            Uri.parse('ws://127.0.0.1:58231/'),
          );

          transport.queueRawResponse('not valid json');
          await pumpEventQueue();
          done.complete();
        }, (Object error, StackTrace stackTrace) => uncaughtErrors.add(error));

        await done.future;

        expect(uncaughtErrors, isEmpty);
        expect(client.connections.state, DovahLinkConnectionState.disconnected);
        expect(transport.closeCalled, isTrue);
      },
    );
  });

  group('Behavior session_invalidated handling behaves correctly', () {
    test(
      'Behavior session_invalidated handling exposes the typed invalidationReason',
      () async {
        transport.queueResponse(_rawFixture('connection/hello-ack.json'));
        transport.queueResponse(
          _rawFixture('capabilities/capabilities-host.json'),
        );
        await client.connections.connectCandidate(
          Uri.parse('ws://127.0.0.1:58231/'),
        );

        transport.queueResponse(_rawSessionInvalidated('revoked'));
        await pumpEventQueue();

        expect(
          client.connections.state,
          DovahLinkConnectionState.administrativelyInvalidated,
        );
        expect(
          client.connections.invalidationReason,
          AdministrativeInvalidationReason.revoked,
        );
      },
    );

    test(
      'Behavior session_invalidated handling emits its Known Host and reason together',
      () async {
        final List<DovahLinkKnownHostInvalidation> invalidations =
            <DovahLinkKnownHostInvalidation>[];
        final StreamSubscription<DovahLinkKnownHostInvalidation> subscription =
            client.connections.knownHostInvalidations.listen(invalidations.add);
        addTearDown(subscription.cancel);

        await _connectAndTrustedHello(transport, client, storage);
        transport.queueResponse(_rawSessionInvalidated('trust_reset'));
        await pumpEventQueue();

        expect(invalidations, hasLength(1));
        expect(
          invalidations.single.hostId,
          DovahLinkHostId('81869993-955c-4ba3-a7d0-d35ca86078ea'),
        );
        expect(
          invalidations.single.reason,
          AdministrativeInvalidationReason.trustReset,
        );
      },
    );

    test(
      'Behavior session_invalidated handling cleans only the invalidated Host credential',
      () async {
        const String hostAId = '81869993-955c-4ba3-a7d0-d35ca86078ea';
        const String hostBId = '81f6cc90-3a88-40c7-8351-104d4a36c971';
        final DovahLinkHost hostA = Fixtures.buildDovahLinkHost(
          hostId: hostAId,
        );
        final DovahLinkHost hostB = Fixtures.buildDovahLinkHost(
          hostId: hostBId,
        );
        final InMemoryClientStorage multiHostStorage = InMemoryClientStorage();
        await multiHostStorage.save(
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
        final FakeDovahLinkTransport multiHostTransport =
            FakeDovahLinkTransport();
        final DovahLinkClient multiHostClient = buildDovahLinkClientForTesting(
          transport: multiHostTransport,
          storage: multiHostStorage,
        );
        addTearDown(multiHostClient.close);

        multiHostTransport.queueResponse(
          _rawFixture('connection/hello-ack-paired.json'),
        );
        multiHostTransport.queueResponse(
          _rawFixture('capabilities/capabilities-host.json'),
        );
        await multiHostClient.connections.connectKnownHost(
          DovahLinkHostId(hostAId),
        );
        multiHostTransport.queueResponse(_rawSessionInvalidated('revoked'));
        await pumpEventQueue();

        final PersistedClientState stored = await multiHostStorage.load();
        expect(stored.clientId, 'client-1');
        expect(stored.knownHosts[hostAId]?.credential, isNull);
        expect(stored.knownHosts[hostAId]?.pairingRequired, isTrue);
        expect(stored.knownHosts[hostBId]?.credential, 'credential-b');
        expect(stored.knownHosts[hostBId]?.pairingRequired, isFalse);
      },
    );

    test('Behavior session_invalidated handling is observable through '
        'connectionStateChanges without waiting for another request', () async {
      final List<DovahLinkConnectionState> observed =
          <DovahLinkConnectionState>[];
      final StreamSubscription<DovahLinkConnectionState> subscription = client
          .connections
          .stateChanges
          .listen(observed.add);
      addTearDown(subscription.cancel);

      transport.queueResponse(_rawFixture('connection/hello-ack.json'));
      transport.queueResponse(
        _rawFixture('capabilities/capabilities-host.json'),
      );
      await client.connections.connectCandidate(
        Uri.parse('ws://127.0.0.1:58231/'),
      );

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
          expect(
            stored.knownHosts.values.single.pairingRequired,
            entry.key != AdministrativeInvalidationReason.blocked,
          );
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
        addTearDown(trackingClient.connections.disconnect);

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
        addTearDown(failingClient.connections.disconnect);

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
        transport.queueResponse(_rawFixture('connection/hello-ack.json'));
        transport.queueResponse(
          _rawFixture('capabilities/capabilities-host.json'),
        );
        await client.connections.connectCandidate(
          Uri.parse('ws://127.0.0.1:58231/'),
        );

        final Future<PairingChallengeStatus> pending = client.pairing
            .requestCode();
        await pumpEventQueue();
        transport.queueResponse(_rawSessionInvalidated('revoked'));

        await expectLater(
          pending,
          throwsA(isA<DovahLinkConnectionException>()),
        );
        expect(
          client.connections.state,
          DovahLinkConnectionState.administrativelyInvalidated,
        );
        expect(
          client.connections.invalidationReason,
          AdministrativeInvalidationReason.revoked,
        );
      },
    );

    test(
      'Behavior session_invalidated handling fails closed when no session is authenticated',
      () async {
        // hello() never ran -- no sessionId/trustState exists yet.
        final Future<HelloResult> attempt = client.connections.connectCandidate(
          Uri.parse('ws://127.0.0.1:58231/'),
        );
        final Future<void> attemptFails = expectLater(
          attempt,
          throwsA(
            isA<DovahLinkProtocolException>().having(
              (DovahLinkProtocolException error) => error.code,
              'code',
              ProtocolErrorCode.malformedMessage,
            ),
          ),
        );
        await pumpEventQueue();
        transport.queueResponse(_rawSessionInvalidated('revoked'));
        await pumpEventQueue();
        await attemptFails;

        expect(client.connections.state, DovahLinkConnectionState.disconnected);
        expect(client.connections.invalidationReason, isNull);
        expect(transport.closeCalled, isTrue);
      },
    );

    test('Behavior session_invalidated handling preserves its typed reason during a transport '
        'failure race', () async {
      transport.queueResponse(_rawFixture('connection/hello-ack.json'));
      transport.queueResponse(
        _rawFixture('capabilities/capabilities-host.json'),
      );
      await client.connections.connectCandidate(
        Uri.parse('ws://127.0.0.1:58231/'),
      );

      // Both delivered on the same still-active subscription before either is processed,
      // simulating the host's own follow-up socket close racing this SDK's own
      // subscription-cancellation cleanup for session_invalidated.
      transport.queueResponse(_rawSessionInvalidated('blocked'));
      transport.failMessagesWith(const SocketException('closed by host'));
      await pumpEventQueue();

      expect(
        client.connections.state,
        DovahLinkConnectionState.administrativelyInvalidated,
      );
      expect(
        client.connections.invalidationReason,
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
        client.connections.state,
        DovahLinkConnectionState.administrativelyInvalidated,
      );
      expect(transport.connectCalls, hasLength(1));

      // Nothing further happens on its own: no automatic reconnect fires while this client
      // just sits invalidated.
      await pumpEventQueue();
      expect(
        client.connections.state,
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
        client.connections.invalidationReason,
        AdministrativeInvalidationReason.revoked,
      );

      transport.queueResponse(_rawFixture('connection/hello-ack.json'));
      transport.queueResponse(
        _rawFixture('capabilities/capabilities-host.json'),
      );
      final HelloResult result = await client.connections.connectCandidate(
        Uri.parse('ws://127.0.0.1:58231/'),
      );

      expect(transport.connectCalls, hasLength(2));
      expect(result.trustState, DovahLinkTrustState.unpaired);
      expect(client.connections.state, DovahLinkConnectionState.connected);
      expect(client.connections.invalidationReason, isNull);
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
      final Future<HelloResult> retry = client.connections.connectCandidate(
        Uri.parse('ws://127.0.0.1:58231/'),
      );
      final Future<void> retryFails = expectLater(
        retry,
        throwsA(isA<DovahLinkConnectionException>()),
      );
      await client.connections.initialConnectionRetryChanges.firstWhere(
        (DovahLinkInitialConnectionRetryStatus status) =>
            status == DovahLinkInitialConnectionRetryStatus.retrying,
      );
      await client.connections.disconnect();
      await retryFails;

      expect(client.connections.state, DovahLinkConnectionState.disconnected);
      expect(client.connections.invalidationReason, isNull);
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

        final Future<DovahLinkTrustState> pending = reconnectClient.pairing
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
              reconnectClient.connections.state !=
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
          reconnectClient.connections.state,
          DovahLinkConnectionState.disconnected,
        );
        expect(await reconnectStorage.load(), pendingState);
      },
    );

    test('Behavior retry-safe reconnect retransmits an orphaned operation and resolves its caller, '
        'via automatic reconnect', () async {
      transport.queueResponse(_rawFixture('connection/hello-ack.json'));
      transport.queueResponse(
        _rawFixture('capabilities/capabilities-host.json'),
      );
      await client.connections.connectCandidate(
        Uri.parse('ws://127.0.0.1:58231/'),
      );

      final Future<PairingChallengeStatus> pending = client.pairing
          .requestCode();
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
      expect(client.connections.state, DovahLinkConnectionState.connected);
      // hello#1, pairing_request#1 (orphaned), hello#2 (automatic), pairing_request#2 (the one
      // retry).
      expect(transport.sent, hasLength(4));
    });

    test('Behavior retry-safe reconnect rejects a paired claim without Known Host authentication, via '
        'automatic reconnect', () async {
      transport.queueResponse(_rawFixture('connection/hello-ack.json'));
      transport.queueResponse(
        _rawFixture('capabilities/capabilities-host.json'),
      );
      await client.connections.connectCandidate(
        Uri.parse('ws://127.0.0.1:58231/'),
      );

      final Future<PairingChallengeStatus> pending = client.pairing
          .requestCode();
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
            'hostVersion': '0.6.0',
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
      expect(client.connections.state, DovahLinkConnectionState.disconnected);
      // hello#1, pairing_request#1 (orphaned, already sent before the drop), hello#2
      // (automatic) -- no pairing_request#2: the orphaned request was never retransmitted.
      expect(transport.sent, hasLength(3));
    });

    test(
      'Behavior retry-safe reconnect does not orphan a retried operation a second time',
      () async {
        transport.queueResponse(_rawFixture('connection/hello-ack.json'));
        transport.queueResponse(
          _rawFixture('capabilities/capabilities-host.json'),
        );
        await client.connections.connectCandidate(
          Uri.parse('ws://127.0.0.1:58231/'),
        );

        final Future<PairingChallengeStatus> pending = client.pairing
            .requestCode();
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
              client.connections.state != DovahLinkConnectionState.connected;
          i++
        ) {
          await pumpEventQueue();
        }
        expect(client.connections.state, DovahLinkConnectionState.connected);

        // A third connect/hello round must not resurrect it for a second retry.
        await client.connections.disconnect();
        transport.queueResponse(_rawFixture('connection/hello-ack.json'));
        transport.queueResponse(
          _rawFixture('capabilities/capabilities-host.json'),
        );
        await client.connections.connectCandidate(
          Uri.parse('ws://127.0.0.1:58231/'),
        );

        // hello#1, pairing_request#1, hello#2(automatic), pairing_request#2(retry),
        // hello#3(automatic), hello#4(manual) -- no third pairing_request.
        expect(transport.sent, hasLength(6));
      },
    );

    test(
      'Behavior retry-safe reconnect fails a non-retry-safe operation immediately',
      () async {
        transport.queueResponse(_rawFixture('connection/hello-ack.json'));
        transport.queueResponse(
          _rawFixture('capabilities/capabilities-host.json'),
        );
        await client.connections.connectCandidate(
          Uri.parse('ws://127.0.0.1:58231/'),
        );

        final Future<void> pending = client.pairing.confirmCode(code: '123456');
        await pumpEventQueue();
        transport.failMessagesWith(const SocketException('dropped'));

        await expectLater(
          pending,
          throwsA(isA<DovahLinkConnectionException>()),
        );
        // Cancels the automatic reconnect the drop above also started (this test is only about
        // the non-retry-safe operation's own immediate failure), so it cannot leak into a later
        // test's transport/client instances.
        await client.connections.disconnect();
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

        await reconnectClient.connections.connectKnownHost(
          DovahLinkHostId(hostAId),
        );
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
          reconnectClient.connections.state,
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
          reconnectClient.connections.state,
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
        await reconnectClient.connections.disconnect();
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
        await reconnectClient.connections.connectKnownHost(
          DovahLinkHostId(hostAId),
        );
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
              reconnectClient.connections.state !=
                  DovahLinkConnectionState.disconnected;
          attempt++
        ) {
          await pumpEventQueue();
        }

        expect(
          reconnectClient.connections.state,
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
        await reconnectClient.connections.disconnect();
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
          reconnectClient.connections.stateChanges.listen(observed.add);
      addTearDown(subscription.cancel);

      reconnectTransport.queueResponse(
        _rawFixture('connection/hello-ack.json'),
      );
      reconnectTransport.queueResponse(
        _rawFixture('capabilities/capabilities-host.json'),
      );
      await reconnectClient.connections.connectCandidate(
        Uri.parse('ws://127.0.0.1:58231/'),
      );

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
        // Ordinary transport loss resolves directly to reconnecting -- consumers must never see a
        // transient disconnected they would have to mistake for a terminal state.
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
        reconnectClient.connections.state,
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

    test(
      'Behavior automatic reconnect stays inside the recovery lifecycle across a failed '
      'attempt, never publishing disconnected between connected and reconnecting',
      () async {
        final FakeDovahLinkTransport reconnectTransport =
            FakeDovahLinkTransport();
        final DovahLinkClient reconnectClient = _buildFastReconnectClient(
          reconnectTransport,
          InMemoryClientStorage(),
        );
        final List<DovahLinkConnectionState> observed =
            <DovahLinkConnectionState>[];
        final StreamSubscription<DovahLinkConnectionState> subscription =
            reconnectClient.connections.stateChanges.listen(observed.add);
        addTearDown(subscription.cancel);
        await _connectAndHello(reconnectTransport, reconnectClient);
        observed.clear();

        reconnectTransport.queueResponse(
          jsonEncode(<String, dynamic>{
            'messageType': 'error',
            'messageId': 'message-error-rate-limited-retry-1',
            'sessionId': null,
            'correlationId': 'message-hello-placeholder',
            'payload': <String, dynamic>{
              'code': 'rate_limited',
              'message':
                  'Inbound message rate exceeded 100 messages per second',
              'retryable': true,
              'details': null,
            },
            'playContextId': null,
            'clientId': null,
          }),
        );
        reconnectTransport.queueResponse(
          _rawFixture('connection/hello-ack.json'),
        );
        reconnectTransport.queueResponse(
          _rawFixture('capabilities/capabilities-host.json'),
        );
        reconnectTransport.failMessagesWith(const SocketException('dropped'));
        for (int i = 0; i < 30; i++) {
          await pumpEventQueue();
        }

        expect(observed, <DovahLinkConnectionState>[
          DovahLinkConnectionState.reconnecting,
          DovahLinkConnectionState.reauthenticating,
          DovahLinkConnectionState.reconnecting,
          DovahLinkConnectionState.reauthenticating,
          DovahLinkConnectionState.connected,
        ]);
      },
    );

    test(
      'Behavior automatic reconnect ends in disconnected only when recovery gives up, with '
      'no earlier disconnected',
      () async {
        final FakeDovahLinkTransport reconnectTransport =
            FakeDovahLinkTransport();
        final DovahLinkClient reconnectClient = _buildFastReconnectClient(
          reconnectTransport,
          InMemoryClientStorage(),
        );
        final List<DovahLinkConnectionState> observed =
            <DovahLinkConnectionState>[];
        final StreamSubscription<DovahLinkConnectionState> subscription =
            reconnectClient.connections.stateChanges.listen(observed.add);
        addTearDown(subscription.cancel);
        await _connectAndHello(reconnectTransport, reconnectClient);
        observed.clear();

        reconnectTransport.failConnectWith = const SocketException(
          'unreachable',
        );
        reconnectTransport.failMessagesWith(const SocketException('dropped'));
        for (int i = 0; i < 40; i++) {
          await pumpEventQueue();
        }

        expect(observed, <DovahLinkConnectionState>[
          DovahLinkConnectionState.reconnecting,
          DovahLinkConnectionState.disconnected,
        ]);
        expect(
          reconnectClient.connections.state,
          DovahLinkConnectionState.disconnected,
        );
      },
    );

    test(
      'Behavior explicit disconnect while recovery is in flight ends in a single '
      'disconnected and starts no further recovery',
      () async {
        final FakeDovahLinkTransport reconnectTransport =
            FakeDovahLinkTransport();
        final DovahLinkClient reconnectClient = _buildFastReconnectClient(
          reconnectTransport,
          InMemoryClientStorage(),
        );
        final List<DovahLinkConnectionState> observed =
            <DovahLinkConnectionState>[];
        final StreamSubscription<DovahLinkConnectionState> subscription =
            reconnectClient.connections.stateChanges.listen(observed.add);
        addTearDown(subscription.cancel);
        await _connectAndHello(reconnectTransport, reconnectClient);
        observed.clear();

        // No hello answer is queued, so recovery parks in reauthenticating.
        reconnectTransport.failMessagesWith(const SocketException('dropped'));
        for (
          int i = 0;
          i < 30 &&
              reconnectClient.connections.state ==
                  DovahLinkConnectionState.connected;
          i++
        ) {
          await pumpEventQueue();
        }
        await reconnectClient.connections.disconnect();
        final int sentAfterDisconnect = reconnectTransport.sent.length;
        for (int i = 0; i < 20; i++) {
          await pumpEventQueue();
        }

        expect(observed.first, DovahLinkConnectionState.reconnecting);
        expect(observed.last, DovahLinkConnectionState.disconnected);
        expect(
          observed.where(
            (DovahLinkConnectionState state) =>
                state == DovahLinkConnectionState.disconnected,
          ),
          hasLength(1),
        );
        expect(reconnectTransport.sent, hasLength(sentAfterDisconnect));
      },
    );

    test(
      'Behavior explicit disconnect racing the transport drop never starts recovery',
      () async {
        final FakeDovahLinkTransport reconnectTransport =
            FakeDovahLinkTransport();
        final DovahLinkClient reconnectClient = _buildFastReconnectClient(
          reconnectTransport,
          InMemoryClientStorage(),
        );
        final List<DovahLinkConnectionState> observed =
            <DovahLinkConnectionState>[];
        final StreamSubscription<DovahLinkConnectionState> subscription =
            reconnectClient.connections.stateChanges.listen(observed.add);
        addTearDown(subscription.cancel);
        await _connectAndHello(reconnectTransport, reconnectClient);
        observed.clear();
        final int sentBefore = reconnectTransport.sent.length;

        reconnectTransport.failMessagesWith(const SocketException('dropped'));
        await reconnectClient.connections.disconnect();
        for (int i = 0; i < 20; i++) {
          await pumpEventQueue();
        }

        expect(observed, <DovahLinkConnectionState>[
          DovahLinkConnectionState.disconnected,
        ]);
        expect(reconnectTransport.sent, hasLength(sentBefore));
      },
    );

    test(
      'Behavior administrative invalidation stays terminal, with no recovery from the '
      'transport close that follows it',
      () async {
        final FakeDovahLinkTransport reconnectTransport =
            FakeDovahLinkTransport();
        final DovahLinkClient reconnectClient = _buildFastReconnectClient(
          reconnectTransport,
          InMemoryClientStorage(),
        );
        final List<DovahLinkConnectionState> observed =
            <DovahLinkConnectionState>[];
        final StreamSubscription<DovahLinkConnectionState> subscription =
            reconnectClient.connections.stateChanges.listen(observed.add);
        addTearDown(subscription.cancel);
        await _connectAndHello(reconnectTransport, reconnectClient);
        observed.clear();
        final int sentBefore = reconnectTransport.sent.length;

        reconnectTransport.queueResponse(_rawSessionInvalidated('revoked'));
        reconnectTransport.failMessagesWith(const SocketException('dropped'));
        for (int i = 0; i < 30; i++) {
          await pumpEventQueue();
        }

        expect(observed, <DovahLinkConnectionState>[
          DovahLinkConnectionState.administrativelyInvalidated,
        ]);
        expect(
          reconnectClient.connections.invalidationReason,
          AdministrativeInvalidationReason.revoked,
        );
        expect(reconnectTransport.sent, hasLength(sentBefore));
      },
    );

    test(
      'Behavior overlapping onError and onDone for one dropped transport start exactly one '
      'recovery',
      () async {
        final FakeDovahLinkTransport reconnectTransport =
            FakeDovahLinkTransport();
        final DovahLinkClient reconnectClient = _buildFastReconnectClient(
          reconnectTransport,
          InMemoryClientStorage(),
        );
        await _connectAndHello(reconnectTransport, reconnectClient);
        final int helloCountBefore = reconnectTransport.sent
            .where(
              (String raw) =>
                  (jsonDecode(raw) as JsonMap)['messageType'] == 'hello',
            )
            .length;

        reconnectTransport.queueResponse(
          _rawFixture('connection/hello-ack.json'),
        );
        reconnectTransport.queueResponse(
          _rawFixture('capabilities/capabilities-host.json'),
        );
        reconnectTransport.failMessagesWith(const SocketException('dropped'));
        for (int i = 0; i < 30; i++) {
          await pumpEventQueue();
        }

        final int helloCountAfter = reconnectTransport.sent
            .where(
              (String raw) =>
                  (jsonDecode(raw) as JsonMap)['messageType'] == 'hello',
            )
            .length;
        expect(
          reconnectClient.connections.state,
          DovahLinkConnectionState.connected,
        );
        expect(helloCountAfter - helloCountBefore, 1);
      },
    );

    test(
      'Behavior Known Host session projection goes connected, reconnecting, '
      'reauthenticating, connected without a transient disconnected',
      () async {
        const String hostAId = '81869993-955c-4ba3-a7d0-d35ca86078ea';
        final DovahLinkHost hostA = Fixtures.buildDovahLinkHost(
          hostId: hostAId,
          hostName: 'Soneka-Desktop',
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
            },
          ),
        );
        final FakeDovahLinkTransport reconnectTransport =
            FakeDovahLinkTransport();
        final DovahLinkClient reconnectClient = _buildFastReconnectClient(
          reconnectTransport,
          reconnectStorage,
        );
        final List<DovahLinkKnownHostSessionState> sessionStates =
            <DovahLinkKnownHostSessionState>[];
        final StreamSubscription<List<DovahLinkKnownHostState>> subscription =
            reconnectClient.hosts.knownHostStatesChanges.listen((
              List<DovahLinkKnownHostState> snapshot,
            ) {
              final DovahLinkKnownHostSessionState next =
                  snapshot.single.sessionState;
              if (sessionStates.isEmpty || sessionStates.last != next) {
                sessionStates.add(next);
              }
            });
        addTearDown(subscription.cancel);
        reconnectTransport.queueResponse(
          _rawFixture('connection/hello-ack-paired.json'),
        );
        reconnectTransport.queueResponse(
          _rawFixture('capabilities/capabilities-host.json'),
        );
        await reconnectClient.connections.connectKnownHost(
          DovahLinkHostId(hostAId),
        );
        await pumpEventQueue();

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
          sessionStates.skipWhile(
            (DovahLinkKnownHostSessionState state) =>
                state != DovahLinkKnownHostSessionState.connected,
          ),
          <DovahLinkKnownHostSessionState>[
            DovahLinkKnownHostSessionState.connected,
            DovahLinkKnownHostSessionState.reconnecting,
            DovahLinkKnownHostSessionState.reauthenticating,
            DovahLinkKnownHostSessionState.connected,
          ],
        );
      },
    );
  });

  group('Behavior late transport failures from an ended connection behave correctly', () {
    /// Establishes a trusted Known Host session on a fresh fast-reconnect client.
    Future<
      ({
        FakeDovahLinkTransport transport,
        InMemoryClientStorage storage,
        DovahLinkClient client,
        List<DovahLinkConnectionState> observed,
      })
    >
    establish({Map<TimeoutClass, Duration>? timeoutDurations}) async {
      final FakeDovahLinkTransport transport = FakeDovahLinkTransport();
      final InMemoryClientStorage storage = InMemoryClientStorage();
      final DovahLinkClient client = timeoutDurations == null
          ? _buildFastReconnectClient(transport, storage)
          : buildDovahLinkClientForTesting(
              transport: transport,
              storage: storage,
              timeoutDurations: timeoutDurations,
              reconnectAttemptDelays: const <Duration>[
                Duration.zero,
                Duration.zero,
              ],
              reconnectDeadline: const Duration(seconds: 30),
            );
      final List<DovahLinkConnectionState> observed =
          <DovahLinkConnectionState>[];
      final StreamSubscription<DovahLinkConnectionState> subscription = client
          .connections
          .stateChanges
          .listen(observed.add);
      addTearDown(subscription.cancel);
      addTearDown(client.connections.disconnect);
      await _connectAndTrustedHello(transport, client, storage);
      return (
        transport: transport,
        storage: storage,
        client: client,
        observed: observed,
      );
    }

    /// Starts a rename whose transport send stays open until [hold] is completed, returning the
    /// request's own eventual outcome (the error, if it fails) without ever throwing.
    Future<Object?> startHeldRename(
      FakeDovahLinkTransport transport,
      DovahLinkClient client,
      Completer<void> hold,
    ) {
      transport.heldSends.add(hold);
      return client.connections
          .renameDevice('Late')
          .then<Object?>((_) => null, onError: (Object error) => error);
    }

    Future<void> pump([int turns = 30]) async {
      for (int i = 0; i < turns; i++) {
        await pumpEventQueue();
      }
    }

    test(
      'Behavior a send failing after explicit disconnect does not restart recovery',
      () async {
        final s = await establish();
        final Completer<void> hold = Completer<void>();
        final Future<Object?> rename = startHeldRename(
          s.transport,
          s.client,
          hold,
        );
        await pump(5);
        await s.client.connections.disconnect();
        await pump(5);
        final int observedBefore = s.observed.length;
        final int closesBefore = s.transport.closeCallCount;
        final int connectsBefore = s.transport.connectCalls.length;

        hold.completeError(const SocketException('late send failure'));
        await pump();

        expect(
          s.client.connections.state,
          DovahLinkConnectionState.disconnected,
        );
        expect(s.observed, hasLength(observedBefore));
        expect(s.transport.closeCallCount, closesBefore);
        expect(s.transport.connectCalls, hasLength(connectsBefore));
        expect(await rename, isA<DovahLinkConnectionException>());
      },
    );

    test(
      'Behavior a send failing after recovery gave up does not begin a second cycle',
      () async {
        final s = await establish();
        final Completer<void> hold = Completer<void>();
        final Future<Object?> rename = startHeldRename(
          s.transport,
          s.client,
          hold,
        );
        await pump(5);
        s.transport.failConnectWith = const SocketException('unreachable');
        s.transport.failMessagesWith(const SocketException('dropped'));
        await pump(40);
        expect(
          s.client.connections.state,
          DovahLinkConnectionState.disconnected,
        );
        final int observedBefore = s.observed.length;
        final int closesBefore = s.transport.closeCallCount;

        hold.completeError(const SocketException('late send failure'));
        await pump();

        expect(
          s.client.connections.state,
          DovahLinkConnectionState.disconnected,
        );
        expect(s.observed, hasLength(observedBefore));
        expect(s.transport.closeCallCount, closesBefore);
        expect(await rename, isA<DovahLinkConnectionException>());
      },
    );

    test(
      'Behavior session A\'s late send failure does not tear down session B',
      () async {
        final s = await establish();
        final Completer<void> hold = Completer<void>();
        final Future<Object?> rename = startHeldRename(
          s.transport,
          s.client,
          hold,
        );
        await pump(5);
        await s.client.connections.disconnect();
        s.transport.queueResponse(
          _rawFixture('connection/hello-ack-paired.json'),
        );
        s.transport.queueResponse(
          _rawFixture('capabilities/capabilities-host.json'),
        );
        await s.client.connections.connectKnownHost(
          DovahLinkHostId('81869993-955c-4ba3-a7d0-d35ca86078ea'),
        );
        await pump(5);
        expect(s.client.connections.state, DovahLinkConnectionState.connected);
        final int observedBefore = s.observed.length;
        final int closesBefore = s.transport.closeCallCount;

        hold.completeError(const SocketException('session A send failure'));
        await pump();

        expect(s.client.connections.state, DovahLinkConnectionState.connected);
        expect(s.observed, hasLength(observedBefore));
        expect(s.transport.closeCallCount, closesBefore);
        expect(await rename, isA<DovahLinkConnectionException>());
      },
    );

    test(
      'Behavior several obsolete failures cause no duplicate teardown or recovery',
      () async {
        final s = await establish();
        final Completer<void> firstHold = Completer<void>();
        final Completer<void> secondHold = Completer<void>();
        final Future<Object?> first = startHeldRename(
          s.transport,
          s.client,
          firstHold,
        );
        final Future<Object?> second = startHeldRename(
          s.transport,
          s.client,
          secondHold,
        );
        await pump(5);
        await s.client.connections.disconnect();
        await pump(5);
        final int observedBefore = s.observed.length;
        final int closesBefore = s.transport.closeCallCount;

        firstHold.completeError(const SocketException('late one'));
        secondHold.completeError(const SocketException('late two'));
        await pump();

        expect(
          s.client.connections.state,
          DovahLinkConnectionState.disconnected,
        );
        expect(s.observed, hasLength(observedBefore));
        expect(s.transport.closeCallCount, closesBefore);
        expect(await first, isA<DovahLinkConnectionException>());
        expect(await second, isA<DovahLinkConnectionException>());
      },
    );

    test(
      'Behavior administrative invalidation stays terminal when an earlier send then fails',
      () async {
        final s = await establish();
        final Completer<void> hold = Completer<void>();
        final Future<Object?> rename = startHeldRename(
          s.transport,
          s.client,
          hold,
        );
        await pump(5);
        s.transport.queueResponse(_rawSessionInvalidated('revoked'));
        await pump();
        expect(
          s.client.connections.state,
          DovahLinkConnectionState.administrativelyInvalidated,
        );
        final int observedBefore = s.observed.length;
        final int closesBefore = s.transport.closeCallCount;

        hold.completeError(const SocketException('late send failure'));
        await pump();

        expect(
          s.client.connections.state,
          DovahLinkConnectionState.administrativelyInvalidated,
        );
        expect(s.observed, hasLength(observedBefore));
        expect(s.transport.closeCallCount, closesBefore);
        expect(await rename, isA<DovahLinkConnectionException>());
      },
    );

    test(
      'Behavior a send failure from the active session still starts ordinary recovery',
      () async {
        final s = await establish();
        final Completer<void> hold = Completer<void>();
        final Future<Object?> rename = startHeldRename(
          s.transport,
          s.client,
          hold,
        );
        await pump(5);
        s.observed.clear();
        s.transport.queueResponse(
          _rawFixture('connection/hello-ack-paired.json'),
        );
        s.transport.queueResponse(
          _rawFixture('capabilities/capabilities-host.json'),
        );

        hold.completeError(const SocketException('active send failure'));
        await pump(40);

        expect(s.observed, <DovahLinkConnectionState>[
          DovahLinkConnectionState.reconnecting,
          DovahLinkConnectionState.reauthenticating,
          DovahLinkConnectionState.connected,
        ]);
        expect(await rename, isA<DovahLinkConnectionException>());
      },
    );

    test(
      'Behavior a timeout from the active session still follows the recovery policy',
      () async {
        final s = await establish(
          timeoutDurations: const <TimeoutClass, Duration>{
            TimeoutClass.short: Duration(milliseconds: 20),
            TimeoutClass.normal: Duration(milliseconds: 20),
            TimeoutClass.heavy: Duration(milliseconds: 20),
          },
        );
        s.observed.clear();
        s.transport.failConnectWith = const SocketException('unreachable');

        // No reply is queued, so the rename's own timeout is the failure under test.
        final Future<Object?> rename = s.client.connections
            .renameDevice('Late')
            .then<Object?>((_) => null, onError: (Object error) => error);
        for (
          int i = 0;
          i < 400 &&
              s.client.connections.state !=
                  DovahLinkConnectionState.disconnected;
          i++
        ) {
          await Future<void>.delayed(const Duration(milliseconds: 5));
        }

        expect(s.observed, <DovahLinkConnectionState>[
          DovahLinkConnectionState.reconnecting,
          DovahLinkConnectionState.disconnected,
        ]);
        expect(await rename, isA<DovahLinkConnectionException>());
      },
    );
  });

  group('Behavior retry-safe snapshot recovery across reconnect behaves correctly', () {
    /// Message IDs of every client-sent [messageType] on [transport], in send order.
    List<String> sentIds(
      FakeDovahLinkTransport transport,
      String messageType,
    ) => transport.sent
        .map((String raw) => jsonDecode(raw) as JsonMap)
        .where((JsonMap envelope) => envelope['messageType'] == messageType)
        .map((JsonMap envelope) => envelope['messageId'] as String)
        .toList();

    Future<void> pump([int turns = 30]) async {
      for (int i = 0; i < turns; i++) {
        await pumpEventQueue();
      }
    }

    /// Connects a trusted client with Character Level subscribed and synchronized at revision 1,
    /// then delivers a revision gap so it sends one `snapshot_request` that stays unanswered.
    Future<
      ({
        FakeDovahLinkTransport transport,
        DovahLinkClient client,
        List<DovahLinkConnectionState> states,
        List<StateSynchronization<CharacterLevelState>> levels,
        String requestId,
      })
    >
    startRecovering() async {
      final FakeDovahLinkTransport transport = FakeDovahLinkTransport();
      final InMemoryClientStorage storage = InMemoryClientStorage();
      final DovahLinkClient client = _buildFastReconnectClient(
        transport,
        storage,
      );
      final List<DovahLinkConnectionState> states =
          <DovahLinkConnectionState>[];
      final List<StateSynchronization<CharacterLevelState>> levels =
          <StateSynchronization<CharacterLevelState>>[];
      final StreamSubscription<DovahLinkConnectionState> stateSubscription =
          client.connections.stateChanges.listen(states.add);
      addTearDown(stateSubscription.cancel);
      addTearDown(client.connections.disconnect);
      await _connectAndTrustedHello(transport, client, storage);
      final StreamSubscription<StateSynchronization<CharacterLevelState>>
      levelSubscription = client.currentHost.character.levelChanges.listen(
        levels.add,
      );
      addTearDown(levelSubscription.cancel);
      await _subscribeStateAreas(transport, client, <DovahLinkStateArea>[
        DovahLinkStateArea.characterLevel,
      ]);
      transport.queueRawResponse(
        _rawStateSnapshot(stateArea: 'character_level', revision: 1, value: 10),
      );
      await pump(5);
      transport.queueRawResponse(
        _rawStateEvent(
          stateArea: 'character_level',
          baseRevision: 5,
          revision: 6,
          value: 12,
        ),
      );
      await pump(5);
      final List<String> requests = sentIds(transport, 'snapshot_request');
      expect(requests, hasLength(1));
      return (
        transport: transport,
        client: client,
        states: states,
        levels: levels,
        requestId: requests.single,
      );
    }

    /// Drops the transport so recovery reconnects, re-authenticates, retransmits the orphaned
    /// snapshot request, and restores the desired subscription. Acknowledges that restoration so
    /// the domain is subscribed again. Returns the retransmitted request's message ID.
    Future<String> reconnectAndRestore(FakeDovahLinkTransport transport) async {
      transport.queueResponse(_rawFixture('connection/hello-ack-paired.json'));
      transport.queueResponse(
        _rawFixture('capabilities/capabilities-host.json'),
      );
      transport.failMessagesWith(const SocketException('dropped'));
      await pump(40);
      final List<String> requests = sentIds(transport, 'snapshot_request');
      expect(requests, hasLength(2));
      final JsonMap ack =
          jsonDecode(_rawSubscriptionAck(accepted: <String>['character_level']))
              as JsonMap;
      ack['correlationId'] = sentIds(transport, 'subscribe').last;
      transport.queueRawResponse(jsonEncode(ack));
      await pump(10);
      return requests.last;
    }

    String rawHostError({
      required String correlationId,
      required String code,
      required bool retryable,
    }) => jsonEncode(<String, dynamic>{
      'messageType': 'error',
      'messageId': 'message-error-$code',
      'sessionId': 'session-paired-1',
      'correlationId': correlationId,
      'payload': <String, dynamic>{
        'code': code,
        'message': 'Snapshot could not be produced.',
        'retryable': retryable,
      },
      'playContextId': null,
      'clientId': null,
    });

    test(
      'a retransmitted snapshot request receives and applies its valid response',
      () async {
        final s = await startRecovering();
        final String retransmitted = await reconnectAndRestore(s.transport);

        s.transport.queueRawResponse(
          _rawStateSnapshot(
            stateArea: 'character_level',
            revision: 6,
            value: 12,
            correlationId: retransmitted,
          ),
        );
        await pump(10);

        expect(s.levels.last.status, DovahLinkStateStatus.synchronized);
        expect(s.levels.last.revision, 6);
        expect(s.levels.last.value?.value, 12);
        expect(s.client.connections.state, DovahLinkConnectionState.connected);
      },
    );

    test(
      'a retryable Host error on the retransmitted request is a current-connection '
      'failure and follows the recovery policy',
      () async {
        final s = await startRecovering();
        final String retransmitted = await reconnectAndRestore(s.transport);
        final int statesBefore = s.states.length;
        s.transport.queueResponse(
          _rawFixture('connection/hello-ack-paired.json'),
        );
        s.transport.queueResponse(
          _rawFixture('capabilities/capabilities-host.json'),
        );

        s.transport.queueRawResponse(
          rawHostError(
            correlationId: retransmitted,
            code: 'internal_error',
            retryable: true,
          ),
        );
        await pump(40);

        expect(
          s.states.skip(statesBefore),
          containsAllInOrder(<DovahLinkConnectionState>[
            DovahLinkConnectionState.reconnecting,
            DovahLinkConnectionState.reauthenticating,
            DovahLinkConnectionState.connected,
          ]),
        );
      },
    );

    test(
      'a non-retryable Host error on the retransmitted request fails recovery without '
      'tearing the connection down',
      () async {
        final s = await startRecovering();
        final String retransmitted = await reconnectAndRestore(s.transport);
        final int closesBefore = s.transport.closeCallCount;
        final int statesBefore = s.states.length;

        s.transport.queueRawResponse(
          rawHostError(
            correlationId: retransmitted,
            code: 'internal_error',
            retryable: false,
          ),
        );
        await pump(20);

        expect(s.client.connections.state, DovahLinkConnectionState.connected);
        expect(s.transport.closeCallCount, closesBefore);
        expect(s.states, hasLength(statesBefore));
      },
    );

    test(
      'a transport failure on the retransmitted wire attempt starts recovery again',
      () async {
        final s = await startRecovering();
        await reconnectAndRestore(s.transport);
        final int statesBefore = s.states.length;
        s.transport.queueResponse(
          _rawFixture('connection/hello-ack-paired.json'),
        );
        s.transport.queueResponse(
          _rawFixture('capabilities/capabilities-host.json'),
        );

        s.transport.failMessagesWith(const SocketException('dropped again'));
        await pump(40);

        expect(
          s.states.skip(statesBefore),
          containsAllInOrder(<DovahLinkConnectionState>[
            DovahLinkConnectionState.reconnecting,
            DovahLinkConnectionState.reauthenticating,
            DovahLinkConnectionState.connected,
          ]),
        );
      },
    );

    test(
      'a request failed by explicit disconnect cannot mark or tear down the next session',
      () async {
        final s = await startRecovering();
        await s.client.connections.disconnect();
        await pump(10);

        s.transport.queueResponse(
          _rawFixture('connection/hello-ack-paired.json'),
        );
        s.transport.queueResponse(
          _rawFixture('capabilities/capabilities-host.json'),
        );
        await s.client.connections.connectKnownHost(
          DovahLinkHostId('81869993-955c-4ba3-a7d0-d35ca86078ea'),
        );
        await pump(10);
        final int closesBefore = s.transport.closeCallCount;
        final int levelsBefore = s.levels.length;
        await pump(20);

        expect(s.client.connections.state, DovahLinkConnectionState.connected);
        expect(s.transport.closeCallCount, closesBefore);
        expect(s.levels, hasLength(levelsBefore));
        expect(
          s.levels.any(
            (StateSynchronization<CharacterLevelState> level) =>
                level.status == DovahLinkStateStatus.failed,
          ),
          isFalse,
        );
      },
    );

    test(
      'removing the subscription during recovery makes the late response inert',
      () async {
        final s = await startRecovering();
        s.transport.queueResponse(
          _rawSubscriptionAck(accepted: const <String>[]),
        );
        await s.client.currentHost.unsubscribeStateArea(
          DovahLinkStateArea.characterLevel,
        );
        await pump(5);
        final int closesBefore = s.transport.closeCallCount;

        s.transport.queueRawResponse(
          _rawStateSnapshot(
            stateArea: 'character_level',
            revision: 6,
            value: 12,
            correlationId: s.requestId,
          ),
        );
        await pump(10);

        expect(s.levels.last.status, DovahLinkStateStatus.notSubscribed);
        expect(s.client.connections.state, DovahLinkConnectionState.connected);
        expect(s.transport.closeCallCount, closesBefore);
      },
    );

    test(
      'a newer play context is not overwritten by an older recovery result',
      () async {
        final s = await startRecovering();
        s.transport.queueRawResponse(
          _rawStateSnapshot(
            stateArea: 'character_level',
            revision: 1,
            value: 30,
            stateAuthorityId: 'authority-2',
            playContextId: 'context-2',
          ),
        );
        await pump(5);

        s.transport.queueRawResponse(
          _rawStateSnapshot(
            stateArea: 'character_level',
            revision: 6,
            value: 12,
            correlationId: s.requestId,
          ),
        );
        await pump(10);

        expect(s.levels.last.playContextId, 'context-2');
        expect(s.levels.last.stateAuthorityId, 'authority-2');
        expect(s.levels.last.value?.value, 30);
        expect(s.client.connections.state, DovahLinkConnectionState.connected);
      },
    );

    test(
      'administrative invalidation stays terminal with a recovery request pending',
      () async {
        final s = await startRecovering();
        final int closesBefore = s.transport.closeCallCount;

        s.transport.queueRawResponse(_rawSessionInvalidated('revoked'));
        await pump(20);

        expect(
          s.client.connections.state,
          DovahLinkConnectionState.administrativelyInvalidated,
        );
        expect(s.transport.closeCallCount, closesBefore + 1);
        expect(
          s.states.where(
            (DovahLinkConnectionState state) =>
                state == DovahLinkConnectionState.reconnecting,
          ),
          isEmpty,
        );
      },
    );
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

        expect(client.connections.state, DovahLinkConnectionState.disconnected);
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
        transport.queueResponse(_rawFixture('connection/hello-ack.json'));
        transport.queueResponse(
          _rawFixture('capabilities/capabilities-host.json'),
        );
        await client.connections.connectCandidate(
          Uri.parse('ws://127.0.0.1:58231/'),
        );

        // confirmPairingCode (not retry-safe) rather than requestPairing here: the point of this
        // test is what happens to a stale reply arriving late for an old, dead generation, not
        // retry behavior -- a retry-safe first request would itself get auto-retried by the
        // second hello() below, which is exactly the mechanism the sibling group above already
        // covers and would confuse this test's own generation-isolation assertion.
        final Future<void> firstRequest = client.pairing.confirmCode(
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
        await client.connections.disconnect();

        transport.queueResponse(_rawFixture('connection/hello-ack.json'));
        transport.queueResponse(
          _rawFixture('capabilities/capabilities-host.json'),
        );
        await client.connections.connectCandidate(
          Uri.parse('ws://127.0.0.1:58231/'),
        );

        final Future<PairingChallengeStatus> secondRequest = client.pairing
            .requestCode();
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
        final DovahLinkClient realTransportClient =
            buildDovahLinkClientForTesting(
              transport: WebSocketTransport(),
              storage: storage,
              initialConnectionRetryDelay: const Duration(seconds: 30),
            );
        addTearDown(realTransportClient.close);

        final Future<HelloResult> connection = realTransportClient.connections
            .connectCandidate(Uri.parse('ws://127.0.0.1:58231/'));
        final Future<void> connectionFails = expectLater(
          connection,
          throwsA(isA<DovahLinkConnectionException>()),
        );
        await realTransportClient.connections.initialConnectionRetryChanges
            .firstWhere(
              (DovahLinkInitialConnectionRetryStatus status) =>
                  status == DovahLinkInitialConnectionRetryStatus.retrying,
            );
        await realTransportClient.connections.disconnect();
        await connectionFails;
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
          initialConnectionRetryDelay: const Duration(seconds: 30),
        );

        // No hello reply is queued; disconnect cancels the SDK's next initial retry.
        final Future<HelloResult> connection = timeoutClient.connections
            .connectCandidate(Uri.parse('ws://127.0.0.1:58231/'));
        final Future<void> connectionFails = expectLater(
          connection,
          throwsA(isA<DovahLinkConnectionException>()),
        );
        await timeoutClient.connections.initialConnectionRetryChanges
            .firstWhere(
              (DovahLinkInitialConnectionRetryStatus status) =>
                  status == DovahLinkInitialConnectionRetryStatus.retrying,
            );
        await timeoutClient.connections.disconnect();
        await connectionFails;
        expect(
          timeoutClient.connections.state,
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
      transport.queueResponse(_rawFixture('connection/hello-ack.json'));
      transport.queueResponse(
        _rawFixture('capabilities/capabilities-host.json'),
      );
      await client.connections.connectCandidate(
        Uri.parse('ws://127.0.0.1:58231/'),
      );

      final Future<DovahLinkTrustState> pending = client.pairing
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
      expect(client.connections.state, DovahLinkConnectionState.connected);
      expect(client.currentHost.trustState, DovahLinkTrustState.trusted);
    });

    test(
      'Behavior request timeout handling fails a pending retry-safe operation immediately after a '
      'protocol violation',
      () async {
        transport.queueResponse(_rawFixture('connection/hello-ack.json'));
        transport.queueResponse(
          _rawFixture('capabilities/capabilities-host.json'),
        );
        await client.connections.connectCandidate(
          Uri.parse('ws://127.0.0.1:58231/'),
        );

        final Future<PairingChallengeStatus> pending = client.pairing
            .requestCode(); // retrySafe
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
        transport.queueResponse(_rawFixture('connection/hello-ack.json'));
        transport.queueResponse(
          _rawFixture('capabilities/capabilities-host.json'),
        );
        await client.connections.connectCandidate(
          Uri.parse('ws://127.0.0.1:58231/'),
        );
        expect(transport.sent, hasLength(3));
      },
    );

    test(
      'disconnect() also fails an already-orphaned operation, not just a currently pending one',
      () async {
        transport.queueResponse(_rawFixture('connection/hello-ack.json'));
        transport.queueResponse(
          _rawFixture('capabilities/capabilities-host.json'),
        );
        await client.connections.connectCandidate(
          Uri.parse('ws://127.0.0.1:58231/'),
        );

        final Future<PairingChallengeStatus> pending = client.pairing
            .requestCode();
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
          client.connections.state,
          DovahLinkConnectionState.reauthenticating,
        );

        // Deliberate disconnect while automatic reconnect is still awaiting re-authentication and
        // has not yet retried the orphaned operation -- must not leave it hanging forever.
        await client.connections.disconnect();

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
      transport.queueResponse(_rawFixture('connection/hello-ack.json'));
      transport.queueResponse(
        _rawFixture('capabilities/capabilities-host.json'),
      );
      await client.connections.connectCandidate(
        Uri.parse('ws://127.0.0.1:58231/'),
      );

      transport.failMessagesWithBoth(const SocketException('dropped'));
      await pumpEventQueue();

      expect(transport.closeCallCount, 1);
      // Ordinary transport loss with a known endpoint hands off to bounded automatic
      // reconnect; disconnect before its own delayed next attempt runs so this test does not
      // leave a live background reconnect cycle behind it, mirroring the established pattern
      // above.
      await client.connections.disconnect();
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
        await reconnectClient.connections.connectKnownHost(
          DovahLinkHostId(hostId),
        );
        await Future<void>.delayed(Duration.zero);
        reconnectTransport.failMessagesWith(const SocketException('dropped'));
        for (int i = 0; i < 20; i++) {
          await pumpEventQueue();
        }
        expect(
          reconnectClient.connections.state,
          DovahLinkConnectionState.reauthenticating,
        );

        final int closeCountBeforeDisconnect =
            reconnectTransport.closeCallCount;
        final Completer<void> closeGate = Completer<void>();
        reconnectTransport.closeGate = closeGate;
        final Future<void> disconnect = reconnectClient.connections
            .disconnect();
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
          reconnectClient.connections.state,
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
        addTearDown(client.connections.disconnect);

        transport.queueResponse(_rawFixture('connection/hello-ack.json'));
        transport.queueResponse(
          _rawFixture('capabilities/capabilities-host.json'),
        );
        await client.connections.connectCandidate(
          Uri.parse('ws://127.0.0.1:58231/'),
        );
        transport.failMessagesWith(const SocketException('dropped'));
        for (
          int attempt = 0;
          attempt < 20 && !transport.closeCalled;
          attempt++
        ) {
          await pumpEventQueue();
        }
        await pumpEventQueue();

        expect(client.connections.state, DovahLinkConnectionState.disconnected);
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

          await monitorClient.connections.disconnect();
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
    test('Method close prevents a later authentication attempt', () async {
      final FakeDovahLinkTransport closeTransport = FakeDovahLinkTransport();
      final DovahLinkClient closeClient = buildDovahLinkClientForTesting(
        transport: closeTransport,
        storage: InMemoryClientStorage(),
      );
      await closeClient.close();

      await expectLater(
        closeClient.connections.connectCandidate(
          Uri.parse('ws://127.0.0.1:58232/'),
        ),
        throwsA(isA<DovahLinkConnectionException>()),
      );
      expect(closeTransport.connectCalls, isEmpty);
    });

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
        await closeClient.connections.connectKnownHost(DovahLinkHostId(hostId));
        final Future<DovahLinkConnectionState> disconnected = closeClient
            .connections
            .stateChanges
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
      final Future<HelloResult> pendingAuthentication = closeClient.connections
          .connectCandidate(Uri.parse('ws://127.0.0.1:58232/'));
      final Future<void> authenticationFails = expectLater(
        pendingAuthentication,
        throwsA(isA<DovahLinkConnectionException>()),
      );
      await pumpEventQueue();

      await closeClient.close();
      await authenticationFails;

      expect(closeTransport.closeCalled, isTrue);
      expect(
        closeClient.connections.state,
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
      await closeClient.connections.connectKnownHost(DovahLinkHostId(hostId));

      closeTransport.failMessagesWith(const SocketException('dropped'));
      for (int attempt = 0; attempt < 20; attempt++) {
        await pumpEventQueue();
        if (closeClient.connections.state ==
            DovahLinkConnectionState.reauthenticating) {
          break;
        }
      }
      expect(
        closeClient.connections.state,
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
        closeClient.connections.state,
        DovahLinkConnectionState.disconnected,
      );
      expect(closeClient.currentHost.sessionId, isNull);
      expect(closeClient.currentHost.trustState, isNull);
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
          closeClient.connections.state,
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

  group('Behavior live-state retention across automatic recovery behaves '
      'correctly', () {
    const List<DovahLinkStateArea> allAreas = DovahLinkStateArea.values;

    test(
      'Behavior every gameplay domain keeps its exact value, identity, and revision as recovering '
      'through reconnecting and reauthenticating without a transient disconnected',
      () async {
        final s = await _establishRetentionSession(allAreas);
        final Map<DovahLinkStateArea, List<StateSynchronization<Object?>>>
        records = _recordDomains(s.client);
        final List<DovahLinkConnectionState> states =
            <DovahLinkConnectionState>[];
        final StreamSubscription<DovahLinkConnectionState> stateSubscription = s
            .client
            .connections
            .stateChanges
            .listen(states.add);
        addTearDown(stateSubscription.cancel);
        final Completer<void> helloGate = Completer<void>();
        s.transport.heldSends.add(helloGate);
        final int subscribesBefore = _sentSubscriptionUpdates(
          s.transport,
        ).length;

        _dropTransport(s.transport, accepted: allAreas);
        await _pumpUntil(
          () =>
              s.client.connections.state ==
              DovahLinkConnectionState.reauthenticating,
        );

        final Map<DovahLinkStateArea, StateSynchronization<Object?>> during =
            await _readDomains(s.client);
        for (final DovahLinkStateArea area in allAreas) {
          _expectRetained(during[area]!, revision: _baselineRevision(area));
        }
        expect(
          (during[DovahLinkStateArea.characterXp]!.value as CharacterXpState)
              .value,
          _baselineRevision(DovahLinkStateArea.characterXp) * 1.5,
        );
        expect(
          (during[DovahLinkStateArea.characterVitals]!.value
                  as CharacterVitalsState)
              .health
              ?.current,
          100.0 + _baselineRevision(DovahLinkStateArea.characterVitals),
        );
        expect(
          (during[DovahLinkStateArea.characterLevel]!.value
                  as CharacterLevelState)
              .value,
          _baselineRevision(DovahLinkStateArea.characterLevel),
        );
        expect(
          (during[DovahLinkStateArea.characterSupernaturalTraits]!.value
                  as CharacterSupernaturalTraitsState)
              .isVampire,
          isFalse,
        );
        expect(
          (during[DovahLinkStateArea.trackedQuests]!.value
                  as TrackedQuestsState)
              .quests,
          isEmpty,
        );

        helloGate.complete();
        await _pumpUntilRestoreSent(s.transport, subscribesBefore);
        await _pumpUntil(
          () =>
              s.client.connections.state == DovahLinkConnectionState.connected,
        );
        await pumpEventQueue();

        expect(states, <DovahLinkConnectionState>[
          DovahLinkConnectionState.connected,
          DovahLinkConnectionState.reconnecting,
          DovahLinkConnectionState.reauthenticating,
          DovahLinkConnectionState.connected,
        ]);
        final Map<DovahLinkStateArea, StateSynchronization<Object?>> after =
            await _readDomains(s.client);
        for (final DovahLinkStateArea area in allAreas) {
          _expectRetained(after[area]!, revision: _baselineRevision(area));
          expect(
            records[area]!.map(
              (StateSynchronization<Object?> state) => state.status,
            ),
            <DovahLinkStateStatus>[
              DovahLinkStateStatus.synchronized,
              DovahLinkStateStatus.recovering,
            ],
            reason:
                '${area.protocolValue} must never pass through notSubscribed',
          );
        }
      },
    );

    test(
      'Behavior a domain without an accepted baseline gains no value, revision, or identity '
      'through retention',
      () async {
        final s = await _establishRetentionSession(
          <DovahLinkStateArea>[
            DovahLinkStateArea.characterXp,
            DovahLinkStateArea.playerLocation,
          ],
          baselined: <DovahLinkStateArea>[DovahLinkStateArea.characterXp],
        );
        final int subscribesBefore = _sentSubscriptionUpdates(
          s.transport,
        ).length;

        _dropTransport(
          s.transport,
          accepted: <DovahLinkStateArea>[
            DovahLinkStateArea.characterXp,
            DovahLinkStateArea.playerLocation,
          ],
        );
        await _pumpUntilRestoreSent(s.transport, subscribesBefore);
        await _pumpUntil(
          () =>
              s.client.connections.state == DovahLinkConnectionState.connected,
        );

        final Map<DovahLinkStateArea, StateSynchronization<Object?>> domains =
            await _readDomains(s.client);
        _expectRetained(
          domains[DovahLinkStateArea.characterXp]!,
          revision: _baselineRevision(DovahLinkStateArea.characterXp),
        );
        _expectAwaitingBaseline(domains[DovahLinkStateArea.playerLocation]!);
        _expectEmpty(domains[DovahLinkStateArea.gameTime]!);
      },
    );

    test(
      'Behavior baselines replace retained values per domain, rejecting an obsolete revision '
      'and letting a never-baselined domain gain its first value only from a Snapshot',
      () async {
        final s = await _establishRetentionSession(
          <DovahLinkStateArea>[
            DovahLinkStateArea.characterXp,
            DovahLinkStateArea.characterVitals,
            DovahLinkStateArea.playerLocation,
          ],
          baselined: <DovahLinkStateArea>[
            DovahLinkStateArea.characterXp,
            DovahLinkStateArea.characterVitals,
          ],
        );
        final int subscribesBefore = _sentSubscriptionUpdates(
          s.transport,
        ).length;
        _dropTransport(
          s.transport,
          accepted: <DovahLinkStateArea>[
            DovahLinkStateArea.characterXp,
            DovahLinkStateArea.characterVitals,
            DovahLinkStateArea.playerLocation,
          ],
        );
        await _pumpUntilRestoreSent(s.transport, subscribesBefore);
        await _pumpUntil(
          () =>
              s.client.connections.state == DovahLinkConnectionState.connected,
        );
        final String restoreId =
            _sentSubscriptionUpdates(s.transport).last['messageId'] as String;
        final int xpRevision = _baselineRevision(
          DovahLinkStateArea.characterXp,
        );
        final int vitalsRevision = _baselineRevision(
          DovahLinkStateArea.characterVitals,
        );

        s.transport.queueRawResponse(
          _retentionSnapshot(
            DovahLinkStateArea.characterVitals,
            vitalsRevision + 5,
            correlationId: restoreId,
          ),
        );
        await pumpEventQueue();
        Map<DovahLinkStateArea, StateSynchronization<Object?>> domains =
            await _readDomains(s.client);
        expect(
          domains[DovahLinkStateArea.characterVitals]!.status,
          DovahLinkStateStatus.synchronized,
        );
        expect(
          domains[DovahLinkStateArea.characterVitals]!.revision,
          vitalsRevision + 5,
        );
        expect(
          (domains[DovahLinkStateArea.characterVitals]!.value
                  as CharacterVitalsState)
              .health
              ?.current,
          100.0 + vitalsRevision + 5,
        );
        _expectRetained(
          domains[DovahLinkStateArea.characterXp]!,
          revision: xpRevision,
        );
        _expectAwaitingBaseline(domains[DovahLinkStateArea.playerLocation]!);

        s.transport.queueRawResponse(
          _retentionSnapshot(
            DovahLinkStateArea.characterXp,
            xpRevision - 1,
            correlationId: restoreId,
          ),
        );
        await pumpEventQueue();
        domains = await _readDomains(s.client);
        _expectRetained(
          domains[DovahLinkStateArea.characterXp]!,
          revision: xpRevision,
        );

        s.transport.queueRawResponse(
          _retentionSnapshot(
            DovahLinkStateArea.characterXp,
            xpRevision + 1,
            correlationId: restoreId,
          ),
        );
        s.transport.queueRawResponse(
          _retentionSnapshot(
            DovahLinkStateArea.playerLocation,
            1,
            correlationId: restoreId,
          ),
        );
        await pumpEventQueue();
        domains = await _readDomains(s.client);
        expect(
          domains[DovahLinkStateArea.characterXp]!.status,
          DovahLinkStateStatus.synchronized,
        );
        expect(
          domains[DovahLinkStateArea.characterXp]!.revision,
          xpRevision + 1,
        );
        expect(
          domains[DovahLinkStateArea.playerLocation]!.status,
          DovahLinkStateStatus.synchronized,
        );
        expect(domains[DovahLinkStateArea.playerLocation]!.revision, 1);
      },
    );

    test(
      'Behavior a baseline delivered in the same frame burst as its subscription acknowledgement '
      'is not lost',
      () async {
        final s = await _establishRetentionSession(<DovahLinkStateArea>[
          DovahLinkStateArea.characterXp,
        ]);
        final int subscribesBefore = _sentSubscriptionUpdates(
          s.transport,
        ).length;
        final Completer<void> subscribeGate = Completer<void>();
        s.transport.heldSends
          ..add(Completer<void>()..complete())
          ..add(subscribeGate);
        s.transport.queueResponse(
          _rawFixture('connection/hello-ack-paired.json'),
        );
        s.transport.queueResponse(
          _rawFixture('capabilities/capabilities-host.json'),
        );
        s.transport.failMessagesWith(const SocketException('dropped'));
        await _pumpUntilRestoreSent(s.transport, subscribesBefore);

        s.transport.queueResponsesForNextSend(<String>[
          _rawSubscriptionAck(accepted: <String>['character_xp']),
          _retentionSnapshot(
            DovahLinkStateArea.characterXp,
            _baselineRevision(DovahLinkStateArea.characterXp) + 2,
            correlationId: 'rewritten-to-the-subscribe-id',
          ),
        ]);
        subscribeGate.complete();
        await _pumpUntil(
          () =>
              s.client.connections.state == DovahLinkConnectionState.connected,
        );
        await pumpEventQueue();

        final StateSynchronization<Object?> xp = (await _readDomains(
          s.client,
        ))[DovahLinkStateArea.characterXp]!;
        expect(xp.status, DovahLinkStateStatus.synchronized);
        expect(
          xp.revision,
          _baselineRevision(DovahLinkStateArea.characterXp) + 2,
        );
      },
    );

    for (final ({String authority, String context, String label}) change
        in <({String authority, String context, String label})>[
          (
            authority: 'authority-2',
            context: 'context-1',
            label: 'stateAuthorityId',
          ),
          (
            authority: 'authority-1',
            context: 'context-2',
            label: 'playContextId',
          ),
        ]) {
      test(
        'Behavior a new ${change.label} clears every retained value of the old identity and '
        'establishes its own baselines',
        () async {
          final s = await _establishRetentionSession(<DovahLinkStateArea>[
            DovahLinkStateArea.characterXp,
            DovahLinkStateArea.characterVitals,
          ]);
          final int subscribesBefore = _sentSubscriptionUpdates(
            s.transport,
          ).length;
          _dropTransport(
            s.transport,
            accepted: <DovahLinkStateArea>[
              DovahLinkStateArea.characterXp,
              DovahLinkStateArea.characterVitals,
            ],
          );
          await _pumpUntilRestoreSent(s.transport, subscribesBefore);
          await _pumpUntil(
            () =>
                s.client.connections.state ==
                DovahLinkConnectionState.connected,
          );
          final String restoreId =
              _sentSubscriptionUpdates(s.transport).last['messageId'] as String;

          s.transport.queueRawResponse(
            _retentionSnapshot(
              DovahLinkStateArea.characterVitals,
              1,
              stateAuthorityId: change.authority,
              playContextId: change.context,
              correlationId: restoreId,
            ),
          );
          await pumpEventQueue();

          Map<DovahLinkStateArea, StateSynchronization<Object?>> domains =
              await _readDomains(s.client);
          final StateSynchronization<Object?> vitals =
              domains[DovahLinkStateArea.characterVitals]!;
          expect(vitals.status, DovahLinkStateStatus.synchronized);
          expect(vitals.revision, 1);
          expect(vitals.stateAuthorityId, change.authority);
          expect(vitals.playContextId, change.context);
          final StateSynchronization<Object?> xp =
              domains[DovahLinkStateArea.characterXp]!;
          expect(xp.status, DovahLinkStateStatus.recovering);
          expect(xp.value, isNull);
          expect(xp.revision, isNull);
          expect(xp.stateAuthorityId, change.authority);
          expect(xp.playContextId, change.context);

          // The new identity's revision sequence is independent, so a lower revision is accepted.
          s.transport.queueRawResponse(
            _retentionSnapshot(
              DovahLinkStateArea.characterXp,
              1,
              stateAuthorityId: change.authority,
              playContextId: change.context,
              correlationId: restoreId,
            ),
          );
          await pumpEventQueue();
          domains = await _readDomains(s.client);
          expect(
            domains[DovahLinkStateArea.characterXp]!.status,
            DovahLinkStateStatus.synchronized,
          );
          expect(domains[DovahLinkStateArea.characterXp]!.revision, 1);
        },
      );
    }

    test(
      'Behavior a rejected restoration clears that domain while accepted domains stay '
      'retained and non-current, and a late message for it is ignored',
      () async {
        final s = await _establishRetentionSession(<DovahLinkStateArea>[
          DovahLinkStateArea.characterXp,
          DovahLinkStateArea.characterVitals,
        ]);
        final int subscribesBefore = _sentSubscriptionUpdates(
          s.transport,
        ).length;
        _dropTransport(
          s.transport,
          accepted: <DovahLinkStateArea>[DovahLinkStateArea.characterVitals],
          rejected: <DovahLinkStateArea>[DovahLinkStateArea.characterXp],
        );
        await _pumpUntilRestoreSent(s.transport, subscribesBefore);
        await _pumpUntil(
          () =>
              s.client.connections.state == DovahLinkConnectionState.connected,
        );
        await pumpEventQueue();

        s.transport.queueRawResponse(
          _retentionSnapshot(DovahLinkStateArea.characterXp, 99),
        );
        await pumpEventQueue();

        final Map<DovahLinkStateArea, StateSynchronization<Object?>> domains =
            await _readDomains(s.client);
        _expectEmpty(domains[DovahLinkStateArea.characterXp]!);
        _expectRetained(
          domains[DovahLinkStateArea.characterVitals]!,
          revision: _baselineRevision(DovahLinkStateArea.characterVitals),
        );
      },
    );

    test(
      'Behavior a state message that arrives before the new session is admitted cannot modify '
      'retained data',
      () async {
        final s = await _establishRetentionSession(<DovahLinkStateArea>[
          DovahLinkStateArea.characterXp,
        ]);
        final Completer<void> helloGate = Completer<void>();
        s.transport.heldSends.add(helloGate);
        _dropTransport(
          s.transport,
          accepted: <DovahLinkStateArea>[DovahLinkStateArea.characterXp],
        );
        await _pumpUntil(
          () =>
              s.client.connections.state ==
              DovahLinkConnectionState.reauthenticating,
        );

        s.transport.queueRawResponse(
          _retentionSnapshot(DovahLinkStateArea.characterXp, 99),
        );
        await pumpEventQueue();

        _expectRetained(
          (await _readDomains(s.client))[DovahLinkStateArea.characterXp]!,
          revision: _baselineRevision(DovahLinkStateArea.characterXp),
        );
        helloGate.complete();
        await _pumpUntil(
          () =>
              s.client.connections.state == DovahLinkConnectionState.connected,
        );
      },
    );

    test(
      'Behavior explicit disconnect during recovery resets every domain to notSubscribed with no '
      'obsolete metadata and a late pending operation changes nothing',
      () async {
        final s = await _establishRetentionSession(allAreas);
        final Completer<void> helloGate = Completer<void>();
        s.transport.heldSends.add(helloGate);
        _dropTransport(s.transport, accepted: allAreas);
        await _pumpUntil(
          () =>
              s.client.connections.state ==
              DovahLinkConnectionState.reauthenticating,
        );

        await s.client.connections.disconnect();
        helloGate.complete();
        await _pumpUntil(() => false);

        expect(
          s.client.connections.state,
          DovahLinkConnectionState.disconnected,
        );
        expect(s.transport.connectCalls, hasLength(2));
        final Map<DovahLinkStateArea, StateSynchronization<Object?>> domains =
            await _readDomains(s.client);
        for (final DovahLinkStateArea area in allAreas) {
          _expectEmpty(domains[area]!);
        }
      },
    );

    test(
      'Behavior a second transport loss while reauthenticating keeps the retained observations '
      'non-current until the session ends',
      () async {
        final s = await _establishRetentionSession(<DovahLinkStateArea>[
          DovahLinkStateArea.characterXp,
        ]);
        final Completer<void> helloGate = Completer<void>();
        s.transport.heldSends.add(helloGate);
        _dropTransport(
          s.transport,
          accepted: <DovahLinkStateArea>[DovahLinkStateArea.characterXp],
        );
        await _pumpUntil(
          () =>
              s.client.connections.state ==
              DovahLinkConnectionState.reauthenticating,
        );
        final List<DovahLinkConnectionState> states =
            <DovahLinkConnectionState>[];
        final StreamSubscription<DovahLinkConnectionState> stateSubscription = s
            .client
            .connections
            .stateChanges
            .listen(states.add);
        addTearDown(stateSubscription.cancel);
        final List<StateSynchronization<Object?>> xp = _recordDomains(
          s.client,
        )[DovahLinkStateArea.characterXp]!;

        s.transport.failMessagesWith(const SocketException('dropped again'));
        await _pumpUntil(
          () => states.contains(DovahLinkConnectionState.reconnecting),
        );
        final int retainedEmissions = xp.length;
        for (final StateSynchronization<Object?> state in xp) {
          _expectRetained(
            state,
            revision: _baselineRevision(DovahLinkStateArea.characterXp),
          );
        }
        helloGate.complete();
        await s.client.connections.disconnect();

        expect(retainedEmissions, 1);
        expect(
          xp.map((StateSynchronization<Object?> state) => state.status),
          <DovahLinkStateStatus>[
            DovahLinkStateStatus.recovering,
            DovahLinkStateStatus.notSubscribed,
          ],
        );
        _expectEmpty(xp.last);
      },
    );

    test(
      'Behavior a session invalidation received while reauthenticating is nonrecoverable and '
      'clears retained values',
      () async {
        final s = await _establishRetentionSession(allAreas);
        final Completer<void> helloGate = Completer<void>();
        s.transport.heldSends.add(helloGate);
        _dropTransport(s.transport, accepted: allAreas);
        await _pumpUntil(
          () =>
              s.client.connections.state ==
              DovahLinkConnectionState.reauthenticating,
        );

        s.transport.queueRawResponse(
          _rawSessionInvalidated('revoked', sessionId: 'session-paired-1'),
        );
        await _pumpUntil(
          () =>
              s.client.connections.state ==
              DovahLinkConnectionState.disconnected,
        );
        helloGate.complete();
        await pumpEventQueue();

        expect(
          s.client.connections.state,
          DovahLinkConnectionState.disconnected,
        );
        final Map<DovahLinkStateArea, StateSynchronization<Object?>> domains =
            await _readDomains(s.client);
        for (final DovahLinkStateArea area in allAreas) {
          _expectEmpty(domains[area]!);
        }
      },
    );

    test(
      'Behavior explicit disconnect from a stable connected session resets synchronized domains',
      () async {
        final s = await _establishRetentionSession(allAreas);

        await s.client.connections.disconnect();

        final Map<DovahLinkStateArea, StateSynchronization<Object?>> domains =
            await _readDomains(s.client);
        for (final DovahLinkStateArea area in allAreas) {
          _expectEmpty(domains[area]!);
        }
      },
    );

    test(
      'Behavior duplicate transport close events start one recovery and keep the retained '
      'observations intact until baselines arrive',
      () async {
        final s = await _establishRetentionSession(<DovahLinkStateArea>[
          DovahLinkStateArea.characterXp,
        ]);
        final int subscribesBefore = _sentSubscriptionUpdates(
          s.transport,
        ).length;
        s.transport.queueResponse(
          _rawFixture('connection/hello-ack-paired.json'),
        );
        s.transport.queueResponse(
          _rawFixture('capabilities/capabilities-host.json'),
        );
        s.transport.queueResponse(
          _rawSubscriptionAck(accepted: <String>['character_xp']),
        );

        s.transport.failMessagesWithBoth(const SocketException('dropped'));
        await _pumpUntilRestoreSent(s.transport, subscribesBefore);
        await _pumpUntil(
          () =>
              s.client.connections.state == DovahLinkConnectionState.connected,
        );
        await pumpEventQueue();

        expect(s.transport.connectCalls, hasLength(2));
        _expectRetained(
          (await _readDomains(s.client))[DovahLinkStateArea.characterXp]!,
          revision: _baselineRevision(DovahLinkStateArea.characterXp),
        );
      },
    );

    test(
      'Behavior administrative invalidation clears retained values and starts no automatic '
      'recovery',
      () async {
        final s = await _establishRetentionSession(allAreas);

        s.transport.queueRawResponse(
          _rawSessionInvalidated('revoked', sessionId: 'session-paired-1'),
        );
        await pumpEventQueue();
        await pumpEventQueue();

        expect(
          s.client.connections.state,
          DovahLinkConnectionState.administrativelyInvalidated,
        );
        expect(s.transport.connectCalls, hasLength(1));
        final Map<DovahLinkStateArea, StateSynchronization<Object?>> domains =
            await _readDomains(s.client);
        for (final DovahLinkStateArea area in allAreas) {
          _expectEmpty(domains[area]!);
        }
      },
    );

    test(
      'Behavior exhausted reconnect attempts clear retained values when the lifecycle becomes '
      'disconnected',
      () async {
        final s = await _establishRetentionSession(allAreas);
        final Map<DovahLinkStateArea, List<StateSynchronization<Object?>>>
        records = _recordDomains(s.client);
        s.transport.failConnectWith = const SocketException('unreachable');

        s.transport.failMessagesWith(const SocketException('dropped'));
        await _pumpUntil(
          () =>
              s.client.connections.state ==
              DovahLinkConnectionState.disconnected,
        );

        expect(
          s.client.connections.state,
          DovahLinkConnectionState.disconnected,
        );
        final Map<DovahLinkStateArea, StateSynchronization<Object?>> domains =
            await _readDomains(s.client);
        for (final DovahLinkStateArea area in allAreas) {
          _expectEmpty(domains[area]!);
          expect(
            records[area]!.map(
              (StateSynchronization<Object?> state) => state.status,
            ),
            <DovahLinkStateStatus>[
              DovahLinkStateStatus.synchronized,
              DovahLinkStateStatus.recovering,
              DovahLinkStateStatus.notSubscribed,
            ],
          );
        }
      },
    );

    test(
      'Behavior Character Level revision-gap recovery works on a retained domain after its '
      'baseline is restored',
      () async {
        final s = await _establishRetentionSession(<DovahLinkStateArea>[
          DovahLinkStateArea.characterLevel,
        ]);
        final int subscribesBefore = _sentSubscriptionUpdates(
          s.transport,
        ).length;
        _dropTransport(
          s.transport,
          accepted: <DovahLinkStateArea>[DovahLinkStateArea.characterLevel],
        );
        await _pumpUntilRestoreSent(s.transport, subscribesBefore);
        await _pumpUntil(
          () =>
              s.client.connections.state == DovahLinkConnectionState.connected,
        );
        final String restoreId =
            _sentSubscriptionUpdates(s.transport).last['messageId'] as String;
        final int levelRevision = _baselineRevision(
          DovahLinkStateArea.characterLevel,
        );
        s.transport.queueRawResponse(
          _retentionSnapshot(
            DovahLinkStateArea.characterLevel,
            levelRevision + 1,
            correlationId: restoreId,
          ),
        );
        await pumpEventQueue();

        s.transport.queueRawResponse(
          _rawStateEvent(
            stateArea: 'character_level',
            baseRevision: levelRevision + 4,
            revision: levelRevision + 5,
            value: 30,
          ),
        );
        await pumpEventQueue();

        final JsonMap request = jsonDecode(s.transport.sent.last) as JsonMap;
        expect(request['messageType'], 'snapshot_request');
        expect(request['payload'], <String, dynamic>{
          'stateArea': 'character_level',
          'knownRevision': levelRevision + 1,
        });
        s.transport.queueRawResponse(
          _retentionSnapshot(
            DovahLinkStateArea.characterLevel,
            levelRevision + 5,
            correlationId: request['messageId'] as String,
          ),
        );
        await pumpEventQueue();
        final StateSynchronization<Object?> level = (await _readDomains(
          s.client,
        ))[DovahLinkStateArea.characterLevel]!;
        expect(level.status, DovahLinkStateStatus.synchronized);
        expect(level.revision, levelRevision + 5);
      },
    );

    test(
      'Behavior an orphaned recovery Snapshot answered before the restored subscription is '
      'acknowledged does not make the domain current, and the restored baseline then does',
      () async {
        const List<DovahLinkStateArea> areas = <DovahLinkStateArea>[
          DovahLinkStateArea.characterLevel,
          DovahLinkStateArea.characterXp,
        ];
        final s = await _establishRetentionSession(areas);
        final int levelRevision = _baselineRevision(
          DovahLinkStateArea.characterLevel,
        );
        final int xpRevision = _baselineRevision(
          DovahLinkStateArea.characterXp,
        );
        s.transport.queueRawResponse(
          _rawStateEvent(
            stateArea: 'character_level',
            baseRevision: levelRevision + 4,
            revision: levelRevision + 5,
            value: 30,
          ),
        );
        await pumpEventQueue();
        expect(_sentIdsOfType(s.transport, 'snapshot_request'), hasLength(1));
        final int subscribesBefore = _sentSubscriptionUpdates(
          s.transport,
        ).length;

        // Reauthenticate, but never queue the restoration acknowledgement: it is held.
        s.transport.queueResponse(
          _rawFixture('connection/hello-ack-paired.json'),
        );
        s.transport.queueResponse(
          _rawFixture('capabilities/capabilities-host.json'),
        );
        s.transport.failMessagesWith(const SocketException('dropped'));
        await _pumpUntilRestoreSent(s.transport, subscribesBefore);
        await _pumpUntil(
          () =>
              s.client.connections.state == DovahLinkConnectionState.connected,
        );
        final List<String> snapshotRequests = _sentIdsOfType(
          s.transport,
          'snapshot_request',
        );
        expect(snapshotRequests, hasLength(2));
        final String restoreId =
            _sentSubscriptionUpdates(s.transport).last['messageId'] as String;

        // The Host answers the retransmitted request ahead of the held acknowledgement.
        s.transport.queueRawResponse(
          _retentionSnapshot(
            DovahLinkStateArea.characterLevel,
            levelRevision + 5,
            correlationId: snapshotRequests.last,
          ),
        );
        await _pumpUntil(() => false);

        Map<DovahLinkStateArea, StateSynchronization<Object?>> domains =
            await _readDomains(s.client);
        _expectRetained(
          domains[DovahLinkStateArea.characterLevel]!,
          revision: levelRevision,
        );
        _expectRetained(
          domains[DovahLinkStateArea.characterXp]!,
          revision: xpRevision,
        );

        expect(
          _sentIdsOfType(s.transport, 'snapshot_request'),
          hasLength(2),
          reason:
              'a refused recovery Snapshot must not trigger another request',
        );

        // The acknowledgement and restored baselines arrive: the domain becomes current.
        final JsonMap ack =
            jsonDecode(
                  _rawSubscriptionAck(
                    accepted: <String>['character_level', 'character_xp'],
                  ),
                )
                as JsonMap;
        ack['correlationId'] = restoreId;
        s.transport.queueRawResponse(jsonEncode(ack));
        s.transport.queueRawResponse(
          _retentionSnapshot(
            DovahLinkStateArea.characterLevel,
            levelRevision + 5,
            correlationId: restoreId,
          ),
        );
        s.transport.queueRawResponse(
          _retentionSnapshot(
            DovahLinkStateArea.characterXp,
            xpRevision,
            correlationId: restoreId,
          ),
        );
        await pumpEventQueue();

        domains = await _readDomains(s.client);
        final StateSynchronization<Object?> level =
            domains[DovahLinkStateArea.characterLevel]!;
        expect(level.status, DovahLinkStateStatus.synchronized);
        expect(level.revision, levelRevision + 5);
        expect(
          domains[DovahLinkStateArea.characterXp]!.status,
          DovahLinkStateStatus.synchronized,
        );
      },
    );

    const DovahLinkStateArea level = DovahLinkStateArea.characterLevel;
    const DovahLinkStateArea xp = DovahLinkStateArea.characterXp;
    const List<DovahLinkStateArea> levelAndXp = <DovahLinkStateArea>[level, xp];

    test(
      'Behavior an orphaned recovery Snapshot answered after the acknowledgement applies and '
      'later revision-gap recovery still works',
      () async {
        final o = await _startOrphanedRecovery(levelAndXp);
        _deliverRestoreAck(o.transport, o.restoreId, accepted: levelAndXp);
        await _settle();

        o.transport.queueRawResponse(
          _retentionSnapshot(
            level,
            o.levelRevision + 5,
            correlationId: o.snapshotRequestId,
          ),
        );
        await _settle();

        Map<DovahLinkStateArea, StateSynchronization<Object?>> domains =
            await _readDomains(o.client);
        expect(domains[level]!.status, DovahLinkStateStatus.synchronized);
        expect(domains[level]!.revision, o.levelRevision + 5);
        _expectRetained(domains[xp]!, revision: _baselineRevision(xp));

        o.transport.queueRawResponse(
          _retentionSnapshot(
            xp,
            _baselineRevision(xp),
            correlationId: o.restoreId,
          ),
        );
        o.transport.queueRawResponse(
          _rawStateEvent(
            stateArea: 'character_level',
            baseRevision: o.levelRevision + 8,
            revision: o.levelRevision + 9,
            value: 40,
          ),
        );
        await _settle();
        final List<String> requests = _sentIdsOfType(
          o.transport,
          'snapshot_request',
        );
        expect(requests, hasLength(3));
        o.transport.queueRawResponse(
          _retentionSnapshot(
            level,
            o.levelRevision + 9,
            correlationId: requests.last,
          ),
        );
        await _settle();

        domains = await _readDomains(o.client);
        expect(domains[level]!.status, DovahLinkStateStatus.synchronized);
        expect(domains[level]!.revision, o.levelRevision + 9);
        expect(domains[xp]!.status, DovahLinkStateStatus.synchronized);
      },
    );

    for (final ({String label, String authority, String context}) change
        in <({String label, String authority, String context})>[
          (
            label: 'play context',
            authority: 'authority-1',
            context: 'context-2',
          ),
          (
            label: 'state authority',
            authority: 'authority-2',
            context: 'context-1',
          ),
        ]) {
      test(
        'Behavior an orphaned recovery Snapshot with a changed ${change.label} before the '
        'acknowledgement leaves every domain on its old retained identity, then the restored '
        'baselines establish the new identity for all domains',
        () async {
          final o = await _startOrphanedRecovery(levelAndXp);

          o.transport.queueRawResponse(
            _retentionSnapshot(
              level,
              o.levelRevision + 5,
              stateAuthorityId: change.authority,
              playContextId: change.context,
              correlationId: o.snapshotRequestId,
            ),
          );
          await _settle();
          Map<DovahLinkStateArea, StateSynchronization<Object?>> domains =
              await _readDomains(o.client);
          for (final DovahLinkStateArea area in levelAndXp) {
            _expectRetained(domains[area]!, revision: _baselineRevision(area));
          }

          _deliverRestoreAck(o.transport, o.restoreId, accepted: levelAndXp);
          for (final DovahLinkStateArea area in levelAndXp) {
            o.transport.queueRawResponse(
              _retentionSnapshot(
                area,
                _baselineRevision(area) + 10,
                stateAuthorityId: change.authority,
                playContextId: change.context,
                correlationId: o.restoreId,
              ),
            );
          }
          await _settle();

          domains = await _readDomains(o.client);
          for (final DovahLinkStateArea area in levelAndXp) {
            expect(domains[area]!.status, DovahLinkStateStatus.synchronized);
            expect(domains[area]!.stateAuthorityId, change.authority);
            expect(domains[area]!.playContextId, change.context);
            expect(domains[area]!.revision, _baselineRevision(area) + 10);
          }
        },
      );

      test(
        'Behavior an admitted recovery Snapshot with a changed ${change.label} clears the old '
        'identity from every other domain instead of mixing it',
        () async {
          final o = await _startOrphanedRecovery(levelAndXp);
          _deliverRestoreAck(o.transport, o.restoreId, accepted: levelAndXp);
          await _settle();

          o.transport.queueRawResponse(
            _retentionSnapshot(
              level,
              o.levelRevision + 5,
              stateAuthorityId: change.authority,
              playContextId: change.context,
              correlationId: o.snapshotRequestId,
            ),
          );
          await _settle();

          final Map<DovahLinkStateArea, StateSynchronization<Object?>> domains =
              await _readDomains(o.client);
          expect(domains[level]!.status, DovahLinkStateStatus.synchronized);
          expect(domains[level]!.stateAuthorityId, change.authority);
          expect(domains[level]!.playContextId, change.context);
          _expectAwaitingBaseline(domains[xp]!);
          expect(domains[xp]!.stateAuthorityId, change.authority);
          expect(domains[xp]!.playContextId, change.context);
        },
      );
    }

    test(
      'Behavior a rejected area cannot stay current because of an earlier orphaned recovery '
      'response',
      () async {
        final o = await _startOrphanedRecovery(levelAndXp);
        o.transport.queueRawResponse(
          _retentionSnapshot(
            level,
            o.levelRevision + 5,
            correlationId: o.snapshotRequestId,
          ),
        );
        await _settle();

        _deliverRestoreAck(
          o.transport,
          o.restoreId,
          accepted: <DovahLinkStateArea>[xp],
          rejected: <DovahLinkStateArea>[level],
        );
        await _settle();

        final Map<DovahLinkStateArea, StateSynchronization<Object?>> domains =
            await _readDomains(o.client);
        _expectEmpty(domains[level]!);
        _expectRetained(domains[xp]!, revision: _baselineRevision(xp));
      },
    );

    test(
      'Behavior a baseline delivered in the same transport burst as the acknowledgement is '
      'applied even after an orphaned recovery response was refused',
      () async {
        final FakeDovahLinkTransport transport = FakeDovahLinkTransport()
          ..synchronousDelivery = true;
        final InMemoryClientStorage storage = InMemoryClientStorage();
        final DovahLinkClient client = _buildFastReconnectClient(
          transport,
          storage,
        );
        addTearDown(client.close);
        await _connectAndTrustedHello(transport, client, storage);
        await _subscribeStateAreas(transport, client, levelAndXp);
        for (final DovahLinkStateArea area in levelAndXp) {
          transport.queueRawResponse(
            _retentionSnapshot(area, _baselineRevision(area)),
          );
        }
        await _settle();
        final int levelRevision = _baselineRevision(level);
        transport.queueRawResponse(
          _rawStateEvent(
            stateArea: 'character_level',
            baseRevision: levelRevision + 4,
            revision: levelRevision + 5,
            value: 30,
          ),
        );
        await _settle();
        final int subscribesBefore = _sentSubscriptionUpdates(transport).length;
        transport.queueResponse(
          _rawFixture('connection/hello-ack-paired.json'),
        );
        transport.queueResponse(
          _rawFixture('capabilities/capabilities-host.json'),
        );
        transport.failMessagesWith(const SocketException('dropped'));
        await _pumpUntilRestoreSent(transport, subscribesBefore);
        await _pumpUntil(
          () => client.connections.state == DovahLinkConnectionState.connected,
        );
        final String requestId = _sentIdsOfType(
          transport,
          'snapshot_request',
        ).last;
        final String restoreId =
            _sentSubscriptionUpdates(transport).last['messageId'] as String;

        transport.queueRawResponse(
          _retentionSnapshot(
            level,
            levelRevision + 5,
            correlationId: requestId,
          ),
        );
        await _settle();
        final StateSynchronization<Object?> beforeAck = (await _readDomains(
          client,
        ))[level]!;
        _expectRetained(beforeAck, revision: levelRevision);

        _deliverRestoreAck(transport, restoreId, accepted: levelAndXp);
        transport.queueRawResponse(
          _retentionSnapshot(
            xp,
            _baselineRevision(xp),
            correlationId: restoreId,
          ),
        );
        transport.queueRawResponse(
          _retentionSnapshot(
            level,
            levelRevision + 5,
            correlationId: restoreId,
          ),
        );
        await _settle();

        final Map<DovahLinkStateArea, StateSynchronization<Object?>> domains =
            await _readDomains(client);
        expect(domains[xp]!.status, DovahLinkStateStatus.synchronized);
        expect(domains[level]!.status, DovahLinkStateStatus.synchronized);
        expect(domains[level]!.revision, levelRevision + 5);
      },
    );

    test(
      'Behavior a failed restored subscribe closes the gate so a late orphaned recovery '
      'response establishes nothing',
      () async {
        final o = await _startOrphanedRecovery(levelAndXp);
        o.transport.queueRawResponse(
          jsonEncode(<String, dynamic>{
            'messageType': 'error',
            'messageId': 'message-error-restore',
            'sessionId': 'session-paired-1',
            'correlationId': o.restoreId,
            'payload': <String, dynamic>{
              'code': 'temporarily_unavailable',
              'message': 'Subscription could not be restored.',
              'retryable': true,
            },
            'playContextId': null,
            'clientId': null,
          }),
        );
        await _settle();
        o.transport.queueRawResponse(
          _retentionSnapshot(
            level,
            o.levelRevision + 5,
            correlationId: o.snapshotRequestId,
          ),
        );
        await _settle();

        final Map<DovahLinkStateArea, StateSynchronization<Object?>> domains =
            await _readDomains(o.client);
        for (final DovahLinkStateArea area in levelAndXp) {
          _expectRetained(domains[area]!, revision: _baselineRevision(area));
        }
      },
    );

    test(
      'Behavior an explicit disconnect with an orphaned recovery request resurrects nothing '
      'when its response arrives late',
      () async {
        final o = await _startOrphanedRecovery(levelAndXp);
        await o.client.connections.disconnect();

        o.transport.queueRawResponse(
          _retentionSnapshot(
            level,
            o.levelRevision + 5,
            correlationId: o.snapshotRequestId,
          ),
        );
        await _settle();

        final Map<DovahLinkStateArea, StateSynchronization<Object?>> domains =
            await _readDomains(o.client);
        for (final DovahLinkStateArea area in levelAndXp) {
          _expectEmpty(domains[area]!);
        }
        expect(
          o.client.connections.state,
          DovahLinkConnectionState.disconnected,
        );
      },
    );

    test(
      'Behavior unrelated Snapshot-only and non-recovering domains keep their retained '
      'observations and the lifecycle keeps one subscription round',
      () async {
        const List<DovahLinkStateArea> areas = <DovahLinkStateArea>[
          level,
          xp,
          DovahLinkStateArea.gameTime,
          DovahLinkStateArea.characterVitals,
        ];
        final o = await _startOrphanedRecovery(areas);
        o.transport.queueRawResponse(
          _retentionSnapshot(
            level,
            o.levelRevision + 5,
            correlationId: o.snapshotRequestId,
          ),
        );
        await _settle();

        Map<DovahLinkStateArea, StateSynchronization<Object?>> domains =
            await _readDomains(o.client);
        for (final DovahLinkStateArea area in areas) {
          _expectRetained(domains[area]!, revision: _baselineRevision(area));
        }

        _deliverRestoreAck(o.transport, o.restoreId, accepted: areas);
        for (final DovahLinkStateArea area in areas) {
          o.transport.queueRawResponse(
            _retentionSnapshot(
              area,
              area == level ? o.levelRevision + 5 : _baselineRevision(area),
              correlationId: o.restoreId,
            ),
          );
        }
        await _settle();

        domains = await _readDomains(o.client);
        for (final DovahLinkStateArea area in areas) {
          expect(domains[area]!.status, DovahLinkStateStatus.synchronized);
        }
        expect(o.states, <DovahLinkConnectionState>[
          DovahLinkConnectionState.connected,
          DovahLinkConnectionState.reconnecting,
          DovahLinkConnectionState.reauthenticating,
          DovahLinkConnectionState.connected,
        ]);
        expect(
          _sentSubscriptionUpdates(o.transport).length - areas.length,
          1,
          reason:
              'one cumulative subscribe per area, plus exactly one restoration round',
        );
      },
    );
  });
  group('Behavior gameplay readiness composition behaves correctly', () {
    const DovahLinkGameplayReadiness unknown =
        DovahLinkGameplayReadiness.unknown;
    const DovahLinkGameplayReadiness notReady =
        DovahLinkGameplayReadiness.notReady;
    const DovahLinkGameplayReadiness ready = DovahLinkGameplayReadiness.ready;

    test('Behavior readiness is unknown before any connection', () async {
      expect(client.currentHost.gameplayReadiness, unknown);
      expect(await client.currentHost.gameplayReadinessChanges.first, unknown);
    });

    test(
      'Behavior a restricted session receives the initial value without subscribing and without changing its state',
      () async {
        final List<DovahLinkGameplayReadiness> seen = _recordReadiness(client);
        transport.queueResponse(_rawFixture('connection/hello-ack.json'));
        transport.queueResponse(
          _rawFixture('capabilities/capabilities-host.json'),
        );
        transport.queueResponse(_rawReadiness(ready: false, sequence: 1));

        await client.connections.connectCandidate(
          Uri.parse('ws://127.0.0.1:58231/'),
        );
        await _settle();

        expect(client.currentHost.gameplayReadiness, notReady);
        expect(seen, <DovahLinkGameplayReadiness>[unknown, notReady]);
        expect(client.connections.state, DovahLinkConnectionState.connected);
        expect(client.currentHost.trustState, DovahLinkTrustState.unpaired);
        expect(_sentIdsOfType(transport, 'subscribe'), isEmpty);
      },
    );

    test(
      'Behavior one session follows notReady, ready, notReady without reconnecting or changing connection or trust state',
      () async {
        final List<DovahLinkGameplayReadiness> seen = _recordReadiness(client);
        final List<DovahLinkConnectionState> states =
            <DovahLinkConnectionState>[];
        final StreamSubscription<DovahLinkConnectionState> stateSubscription =
            client.connections.stateChanges.listen(states.add);
        addTearDown(stateSubscription.cancel);
        transport.queueResponse(_rawFixture('connection/hello-ack.json'));
        transport.queueResponse(
          _rawFixture('capabilities/capabilities-host.json'),
        );
        transport.queueResponse(_rawReadiness(ready: false, sequence: 1));
        await client.connections.connectCandidate(
          Uri.parse('ws://127.0.0.1:58231/'),
        );
        await _settle();
        final List<DovahLinkConnectionState> statesBefore =
            List<DovahLinkConnectionState>.of(states);

        transport.queueRawResponse(_rawReadiness(ready: true, sequence: 2));
        await _settle();
        expect(client.currentHost.gameplayReadiness, ready);
        transport.queueRawResponse(_rawReadiness(ready: false, sequence: 3));
        await _settle();

        expect(client.currentHost.gameplayReadiness, notReady);
        expect(seen, <DovahLinkGameplayReadiness>[
          unknown,
          notReady,
          ready,
          notReady,
        ]);
        expect(states, statesBefore);
        expect(transport.connectCalls, hasLength(1));
        expect(client.currentHost.trustState, DovahLinkTrustState.unpaired);
      },
    );

    test(
      'Behavior a duplicate, lower, and wrong-session readiness message changes nothing',
      () async {
        await _connectAndHello(transport, client);
        transport.queueRawResponse(_rawReadiness(ready: true, sequence: 5));
        await _settle();

        transport.queueRawResponse(_rawReadiness(ready: false, sequence: 5));
        transport.queueRawResponse(_rawReadiness(ready: false, sequence: 4));
        transport.queueRawResponse(
          _rawReadiness(ready: false, sequence: 9, sessionId: 'session-other'),
        );
        await _settle();

        expect(client.currentHost.gameplayReadiness, ready);
        expect(client.connections.state, DovahLinkConnectionState.connected);
      },
    );

    test(
      'Behavior the first readiness message is kept when the transport delivers it with no turn after hello_ack',
      () async {
        transport.synchronousDelivery = true;
        transport.queueResponse(_rawFixture('connection/hello-ack.json'));
        transport.queueResponse(
          _rawFixture('capabilities/capabilities-host.json'),
        );
        transport.queueResponse(_rawReadiness(ready: true, sequence: 1));

        await client.connections.connectCandidate(
          Uri.parse('ws://127.0.0.1:58231/'),
        );
        await _settle();

        expect(client.currentHost.gameplayReadiness, ready);
      },
    );

    test(
      'Behavior a trusted session holds the first readiness message unpublished until admission completes',
      () async {
        final HoldableClientStorage holdable = HoldableClientStorage();
        final FakeDovahLinkTransport heldTransport = FakeDovahLinkTransport()
          ..synchronousDelivery = true;
        final DovahLinkClient heldClient = buildDovahLinkClientForTesting(
          transport: heldTransport,
          storage: holdable,
        );
        addTearDown(heldClient.close);
        await holdable.save(
          Fixtures.buildPersistedClientState(
            clientId: 'client-1',
            hostId: '81869993-955c-4ba3-a7d0-d35ca86078ea',
            // A stale name makes trusted admission write the refreshed Host name.
            hostName: 'Renamed-Desktop',
            credential: 'trusted-credential',
          ),
        );
        holdable.saveGate = Completer<void>();
        heldTransport.queueResponse(
          _rawFixture('connection/hello-ack-paired.json'),
        );
        heldTransport.queueResponse(
          _rawFixture('capabilities/capabilities-host.json'),
        );
        heldTransport.queueResponse(
          _rawReadiness(
            ready: true,
            sequence: 1,
            sessionId: 'session-paired-1',
          ),
        );

        final Future<void> connecting = heldClient.connections.connectKnownHost(
          DovahLinkHostId('81869993-955c-4ba3-a7d0-d35ca86078ea'),
        );
        await _settle();

        expect(heldClient.currentHost.sessionId, isNull);
        expect(heldClient.currentHost.gameplayReadiness, unknown);

        holdable.saveGate!.complete();
        await connecting;
        await _settle();

        expect(heldClient.currentHost.sessionId, 'session-paired-1');
        expect(heldClient.currentHost.trustState, DovahLinkTrustState.trusted);
        expect(heldClient.currentHost.gameplayReadiness, ready);
      },
    );

    test(
      'Behavior a 0.5.x Host is rejected as too old before admission even when its readiness arrives with no turn after hello_ack',
      () async {
        transport.synchronousDelivery = true;
        transport.queueResponse(_rawHelloAck(hostVersion: '0.5.0'));
        transport.queueResponse(
          _rawFixture('capabilities/capabilities-host.json'),
        );
        transport.queueResponse(_rawReadiness(ready: true, sequence: 1));

        await expectLater(
          client.connections.connectCandidate(
            Uri.parse('ws://127.0.0.1:58231/'),
          ),
          throwsA(
            isA<DovahLinkCompatibilityException>()
                .having(
                  (DovahLinkCompatibilityException error) => error.failure,
                  'failure',
                  HostVersionCompatibilityFailure.hostTooOld,
                )
                .having(
                  (DovahLinkCompatibilityException error) =>
                      error.supportedHostVersionRange,
                  'supportedHostVersionRange',
                  '0.6.x',
                ),
          ),
        );
        await _settle();

        expect(client.currentHost.sessionId, isNull);
        expect(client.currentHost.gameplayReadiness, unknown);
      },
    );

    test(
      'Behavior a failed authentication leaves no readiness staged for the next session',
      () async {
        transport.queueResponse(_rawHelloAck(hostVersion: '9.0.0'));
        transport.queueResponse(
          _rawFixture('capabilities/capabilities-host.json'),
        );
        transport.queueResponse(_rawReadiness(ready: true, sequence: 1));

        await expectLater(
          client.connections.connectCandidate(
            Uri.parse('ws://127.0.0.1:58231/'),
          ),
          throwsA(isA<DovahLinkCompatibilityException>()),
        );
        await _settle();
        expect(client.currentHost.gameplayReadiness, unknown);

        await _connectAndHello(transport, client);
        await _settle();

        expect(client.currentHost.gameplayReadiness, unknown);
      },
    );

    test(
      'Behavior transport loss makes readiness unknown through reconnection while gameplay values stay retained',
      () async {
        final s = await _establishRetentionSession(<DovahLinkStateArea>[
          DovahLinkStateArea.characterXp,
        ]);
        final List<DovahLinkGameplayReadiness> seen = _recordReadiness(
          s.client,
        );
        s.transport.queueRawResponse(
          _rawReadiness(
            ready: true,
            sequence: 1,
            sessionId: 'session-paired-1',
            stateAuthorityId: 'authority-1',
          ),
        );
        await _settle();
        expect(s.client.currentHost.gameplayReadiness, ready);

        _dropTransport(
          s.transport,
          accepted: <DovahLinkStateArea>[DovahLinkStateArea.characterXp],
        );
        await _pumpUntil(
          () =>
              s.client.connections.state == DovahLinkConnectionState.connected,
        );
        await _settle();

        expect(s.client.currentHost.gameplayReadiness, unknown);
        expect(seen, <DovahLinkGameplayReadiness>[unknown, ready, unknown]);
        _expectRetained(
          (await _readDomains(s.client))[DovahLinkStateArea.characterXp]!,
          revision: _baselineRevision(DovahLinkStateArea.characterXp),
        );

        s.transport.queueRawResponse(
          _rawReadiness(
            ready: false,
            sequence: 1,
            sessionId: 'session-paired-1',
            stateAuthorityId: 'authority-1',
          ),
        );
        await _settle();

        expect(s.client.currentHost.gameplayReadiness, notReady);
        expect(seen.last, notReady);
      },
    );

    test(
      'Behavior a state authority rotation invalidates readiness, ignores the superseded authority, and accepts the new epoch',
      () async {
        final s = await _establishRetentionSession(<DovahLinkStateArea>[
          DovahLinkStateArea.characterXp,
        ]);
        s.transport.queueRawResponse(
          _rawReadiness(
            ready: true,
            sequence: 1,
            sessionId: 'session-paired-1',
            stateAuthorityId: 'authority-1',
          ),
        );
        await _settle();
        expect(s.client.currentHost.gameplayReadiness, ready);

        s.transport.queueRawResponse(
          _rawStateSnapshot(
            stateArea: 'character_xp',
            revision: 1,
            value: 42.5,
            stateAuthorityId: 'authority-2',
          ),
        );
        await _settle();
        expect(s.client.currentHost.gameplayReadiness, unknown);

        s.transport.queueRawResponse(
          _rawReadiness(
            ready: true,
            sequence: 2,
            sessionId: 'session-paired-1',
            stateAuthorityId: 'authority-1',
          ),
        );
        await _settle();
        expect(s.client.currentHost.gameplayReadiness, unknown);

        s.transport.queueRawResponse(
          _rawReadiness(
            ready: false,
            sequence: 3,
            sessionId: 'session-paired-1',
            stateAuthorityId: 'authority-2',
          ),
        );
        await _settle();
        expect(s.client.currentHost.gameplayReadiness, notReady);
      },
    );

    test(
      'Behavior explicit disconnect makes readiness unknown and a delayed message from the ended session cannot restore it',
      () async {
        await _connectAndHello(transport, client);
        transport.queueRawResponse(_rawReadiness(ready: true, sequence: 1));
        await _settle();
        expect(client.currentHost.gameplayReadiness, ready);

        await client.connections.disconnect();
        expect(client.currentHost.gameplayReadiness, unknown);

        transport.queueResponse(_rawHelloAck(sessionId: 'session-2'));
        transport.queueResponse(
          _rawFixture('capabilities/capabilities-host.json'),
        );
        await client.connections.connectCandidate(
          Uri.parse('ws://127.0.0.1:58231/'),
        );
        transport.queueRawResponse(_rawReadiness(ready: true, sequence: 50));
        await _settle();
        expect(client.currentHost.gameplayReadiness, unknown);

        transport.queueRawResponse(
          _rawReadiness(ready: false, sequence: 1, sessionId: 'session-2'),
        );
        await _settle();
        expect(client.currentHost.gameplayReadiness, notReady);
      },
    );

    test(
      'Behavior administrative invalidation makes readiness unknown',
      () async {
        await _connectAndHello(transport, client);
        transport.queueRawResponse(_rawReadiness(ready: true, sequence: 1));
        await _settle();
        expect(client.currentHost.gameplayReadiness, ready);

        transport.queueRawResponse(_rawSessionInvalidated('revoked'));
        await _settle();

        expect(
          client.connections.state,
          DovahLinkConnectionState.administrativelyInvalidated,
        );
        expect(client.currentHost.gameplayReadiness, unknown);
      },
    );

    test(
      'Behavior a malformed readiness message fails the session and leaves readiness unknown',
      () async {
        await _connectAndHello(transport, client);
        transport.queueRawResponse(_rawReadiness(ready: true, sequence: 1));
        await _settle();
        expect(client.currentHost.gameplayReadiness, ready);

        transport.queueRawResponse(_rawReadiness(ready: true, sequence: 0));
        await _settle();

        expect(client.currentHost.gameplayReadiness, unknown);
        expect(
          client.connections.state,
          isNot(DovahLinkConnectionState.connected),
        );
      },
    );

    test('Behavior closing the client leaves readiness unknown', () async {
      await _connectAndHello(transport, client);
      transport.queueRawResponse(_rawReadiness(ready: true, sequence: 1));
      await _settle();
      expect(client.currentHost.gameplayReadiness, ready);

      await client.close();

      expect(client.currentHost.gameplayReadiness, unknown);
    });

    test('Behavior two clients keep independent readiness', () async {
      final FakeDovahLinkTransport otherTransport = FakeDovahLinkTransport();
      final DovahLinkClient other = buildDovahLinkClientForTesting(
        transport: otherTransport,
        storage: InMemoryClientStorage(),
      );
      addTearDown(other.close);

      await _connectAndHello(transport, client);
      transport.queueRawResponse(_rawReadiness(ready: true, sequence: 1));
      await _settle();

      expect(client.currentHost.gameplayReadiness, ready);
      expect(other.currentHost.gameplayReadiness, unknown);
    });
  });
}
