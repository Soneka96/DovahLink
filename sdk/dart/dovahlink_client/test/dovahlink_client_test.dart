import 'dart:async';
import 'dart:convert';
import 'dart:io';

import 'package:test/test.dart';

import 'package:dovahlink_client_sdk/dovahlink_client.dart';
import 'package:dovahlink_client_sdk/src/persistence/in_memory_client_storage.dart';
import 'package:dovahlink_client_sdk/src/protocol/json_map.dart';
import 'package:dovahlink_client_sdk/src/shared/enums.dart' show TimeoutClass;
import 'package:dovahlink_client_sdk/src/transport/websocket_transport.dart';
import 'fixtures/fixtures.dart';
import 'support/pending_reply.dart';

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

/// A controllable [DovahLinkTransport] double modeling the *real* [WebSocketTransport]'s
/// semantics for the single continuous, single-subscription inbound stream this SDK's receiver
/// depends on: [connect] establishes a fresh single-subscription stream (mirroring the real
/// transport's fresh socket per connection, and its "only ever listened to once" constraint), and
/// [messages] waits for the next message rather than erroring once nothing more is queued yet
/// (mirroring how a real socket simply has nothing to deliver until something arrives).
///
/// [queueResponse] auto-correlates: a queued reply whose own `correlationId` is non-null (every
/// canonical fixture answering a specific request) has that field rewritten to the `messageId` of
/// the next not-yet-matched [send] call, in queue order, before delivery -- this lets tests reuse
/// canonical fixtures (whose hardcoded `correlationId` values do not know this client's real,
/// randomly generated `messageId`) without hand-matching them, while still exercising this
/// client's own real correlationId-matching logic on the rewritten value. A queued reply already
/// carrying `correlationId: null` (an unsolicited push, e.g. `capabilities`/`session_invalidated`,
/// or malformed text with no `correlationId` to read at all) releases as soon as it reaches the
/// front of the queue, without waiting for any particular [send] -- but never jumping ahead of an
/// earlier-queued reply still waiting on its own [send], so relative queue order is always
/// preserved. [queueRawResponse] is the escape hatch for a test that deliberately wants an
/// unrewritten -- including deliberately mismatched -- `correlationId`, delivered immediately.
class FakeDovahLinkTransport implements DovahLinkTransport {
  /// Every raw text message sent, in order.
  final List<String> sent = <String>[];

  /// Queued [queueResponse] replies not yet released, in queue order.
  final PendingReplyQueue _pendingReplies = PendingReplyQueue();

  /// The current connection's inbound stream, or `null` before [connect]/after [close].
  StreamController<String>? _incoming;

  /// The URI passed to [connect], or `null` if not yet called.
  Uri? connectedUri;

  /// Every URI passed to [connect], in order -- unlike [connectedUri], proves how many times and
  /// with what arguments [connect] was called across a retry.
  final List<Uri> connectCalls = <Uri>[];

  /// Whether [close] was called.
  bool closeCalled = false;

  /// The number of times [close] was called -- unlike [closeCalled], distinguishes one real
  /// teardown from a duplicate one that should have been deduplicated.
  int closeCallCount = 0;

  /// Makes the next [connect] call throw [error] instead of succeeding.
  Object? failConnectWith;

  /// Makes every [send] call throw [error] instead of succeeding.
  Object? failSendWith;

  /// Makes the next [close] call throw [error] instead of succeeding.
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

  /// See [DovahLinkTransport.connect].
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

  /// See [DovahLinkTransport.send].
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

  /// See [DovahLinkTransport.messages].
  @override
  Stream<String> get messages => _requireIncoming().stream;

  /// See [DovahLinkTransport.close].
  @override
  Future<void> close() async {
    closeCalled = true;
    closeCallCount++;
    _incoming = null;
    final Object? failure = failCloseWith;
    if (failure != null) {
      throw failure;
    }
  }

  /// Returns the current inbound stream, creating one on first use if [connect] was never
  /// explicitly called -- many tests below exercise a single request/reply exchange directly
  /// without a preceding [connect], the same way the fake this replaces always allowed. [connect]
  /// itself always installs a genuinely fresh one, which is what matters for this fake to
  /// correctly model one connection's stream being independent of the next.
  StreamController<String> _requireIncoming() =>
      _incoming ??= StreamController<String>();
}

/// Tracks persistence writes for composition-root invalidation tests.
class TrackingClientStorage implements ClientStorage {
  /// Creates storage seeded with [state].
  TrackingClientStorage(this._state);

  /// State returned by [load].
  PersistedClientState _state;

  /// Optional error thrown by [save].
  Object? saveError;

  /// Number of attempted [save] calls.
  int saveCount = 0;

  /// See [ClientStorage.load].
  @override
  Future<PersistedClientState> load() async => _state;

  /// See [ClientStorage.save].
  @override
  Future<void> save(PersistedClientState state) async {
    saveCount++;
    final Object? error = saveError;
    if (error != null) {
      throw error;
    }
    _state = state;
  }

  /// See [ClientStorage.clear].
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
String _rawSessionInvalidated(String reason) => jsonEncode(<String, dynamic>{
  'messageType': 'session_invalidated',
  'messageId': 'message-session-invalidated-1',
  'sessionId': 'session-1',
  'correlationId': null,
  'payload': <String, dynamic>{'reason': reason},
  'bridgeInstanceId': 'bridge-1',
  'playContextId': null,
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

/// Connects [client] to the fake transport and admits an unpaired session for a public-client test.
Future<void> _connectAndHello(
  FakeDovahLinkTransport transport,
  DovahLinkClient client,
) async {
  await client.connect(Uri.parse('ws://127.0.0.1:58231/'));
  transport.queueResponse(_rawFixture('connection/hello-ack.json'));
  transport.queueResponse(_rawFixture('capabilities/capabilities-bridge.json'));
  await client.hello();
}

/// Runs public-client behavior tests.
void main() {
  late FakeDovahLinkTransport transport;
  late InMemoryClientStorage storage;
  late DovahLinkClient client;

  setUp(() {
    transport = FakeDovahLinkTransport();
    storage = InMemoryClientStorage();
    client = DovahLinkClient(transport: transport, storage: storage);
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
      'Method hello an unpaired hello (no stored credential) sets sessionId and trustState from the real fixtures',
      () async {
        await client.connect(Uri.parse('ws://127.0.0.1:58231/'));
        transport.queueResponse(_rawFixture('connection/hello-ack.json'));
        transport.queueResponse(
          _rawFixture('capabilities/capabilities-bridge.json'),
        );

        final HelloResult result = await client.hello();

        expect(result.bridgeVersion, '0.3.3');
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
        // The bridge already closed this socket (every HandleHello failure path does); the
        // transport must be reset so the next connect() attempt does not find a stale socket
        // WebSocketTransport still considers open.
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
        transport.queueResponse('not valid json');

        await expectLater(
          client.hello(),
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
      'Method authenticate delegates to connect and hello when nothing is rejected',
      () async {
        transport.queueResponse(_rawFixture('connection/hello-ack.json'));
        transport.queueResponse(
          _rawFixture('capabilities/capabilities-bridge.json'),
        );

        final HelloResult result = await client.authenticate(
          Uri.parse('ws://127.0.0.1:58231/'),
        );

        expect(result.trustState, DovahLinkTrustState.unpaired);
        expect(result.recoveredFromRejectedCredential, isNull);
        expect(transport.connectedUri, Uri.parse('ws://127.0.0.1:58231/'));
      },
    );

    test(
      'Method authenticate reconnects after disconnect even though the client was last trusted',
      () async {
        transport.queueResponse(
          jsonEncode(<String, dynamic>{
            'messageType': 'hello_ack',
            'messageId': 'message-hello-ack-1',
            'sessionId': 'session-1',
            'correlationId': 'irrelevant',
            'payload': <String, dynamic>{
              'bridgeVersion': '0.2.0',
              'clientIdentityKind': 'paired',
            },
            'bridgeInstanceId': 'bridge-1',
            'playContextId': null,
            'clientId': 'client-1',
          }),
        );
        transport.queueResponse(
          _rawFixture('capabilities/capabilities-bridge.json'),
        );
        final Uri uri = Uri.parse('ws://127.0.0.1:58231/');
        await client.authenticate(uri);
        await client.disconnect();

        transport.queueResponse(
          jsonEncode(<String, dynamic>{
            'messageType': 'hello_ack',
            'messageId': 'message-hello-ack-2',
            'sessionId': 'session-2',
            'correlationId': 'irrelevant',
            'payload': <String, dynamic>{
              'bridgeVersion': '0.2.0',
              'clientIdentityKind': 'paired',
            },
            'bridgeInstanceId': 'bridge-1',
            'playContextId': null,
            'clientId': 'client-1',
          }),
        );
        transport.queueResponse(
          _rawFixture('capabilities/capabilities-bridge.json'),
        );
        await client.authenticate(uri);

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
      // The send failure is ordinary transport loss, so SessionServiceImpl hands off to bounded
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
        expect(result.retryAfterSeconds, isNull);
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
      'Method confirmPairingCode returns the issued credential and persists it with CONFIRMING recovery',
      () async {
        await storage.save(
          Fixtures.buildPersistedClientState(clientId: 'client-1'),
        );
        await _connectAndHello(transport, client);
        transport.queueResponse(
          _rawFixture('pairing/pairing-outcome-credential-issued.json'),
        );

        final String credential = await client.confirmPairingCode(
          code: '123456',
          displayName: 'My PC',
        );

        expect(credential, 'a1b2c3d4e5f6');
        final PersistedClientState stored = await storage.load();
        expect(stored.clientId, 'client-1');
        expect(stored.credential, 'a1b2c3d4e5f6');
        expect(stored.recoveryState, PairingRecoveryState.confirming);
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
        expect(stored.credential, 'already-confirming-credential');
        expect(stored.recoveryState, PairingRecoveryState.confirming);
      },
    );
  });

  group('Method acknowledgeTrustedCredential behaves correctly', () {
    test(
      'Method acknowledgeTrustedCredential sets trustState to trusted and clears recovery to none on a trusted outcome',
      () async {
        await storage.save(
          Fixtures.buildPersistedClientState(
            clientId: 'client-1',
            credential: 'a1b2c3d4e5f6',
            recoveryState: PairingRecoveryState.confirming,
          ),
        );
        await _connectAndHello(transport, client);
        transport.queueResponse(
          _rawFixture('pairing/pairing-outcome-trusted.json'),
        );

        await client.acknowledgeTrustedCredential('a1b2c3d4e5f6');

        expect(client.trustState, DovahLinkTrustState.trusted);
        final PersistedClientState stored = await storage.load();
        expect(stored.credential, 'a1b2c3d4e5f6');
        expect(stored.recoveryState, PairingRecoveryState.none);
      },
    );

    test(
      'Method acknowledgeTrustedCredential persists the previously stored credential, not the method argument, on success',
      () async {
        await storage.save(
          Fixtures.buildPersistedClientState(
            clientId: null,
            credential: 'stored-credential',
            recoveryState: PairingRecoveryState.confirming,
          ),
        );
        await _connectAndHello(transport, client);
        transport.queueResponse(
          _rawFixture('pairing/pairing-outcome-trusted.json'),
        );

        // The argument is what the caller sends over the wire in pairing_ack; storage is never
        // overwritten with it, only its recoveryState is cleared -- pass a deliberately different
        // value than what is stored to prove that.
        await client.acknowledgeTrustedCredential('argument-credential');

        final PersistedClientState stored = await storage.load();
        expect(stored.credential, 'stored-credential');
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
        expect(stored.credential, 'a1b2c3d4e5f6');
        expect(stored.recoveryState, PairingRecoveryState.confirming);
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
          _rawFixture('capabilities/capabilities-bridge.json'),
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
          _rawFixture('capabilities/capabilities-bridge.json'),
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
      await client.connect(Uri.parse('ws://127.0.0.1:58231/'));
      transport.queueResponse(_rawFixture('connection/hello-ack-paired.json'));
      transport.queueResponse(
        _rawFixture('capabilities/capabilities-bridge.json'),
      );
      await client.hello();

      await client.disconnect();

      final PersistedClientState stored = await storage.load();
      expect(stored.clientId, 'client-1');
      expect(stored.credential, 'credential-1');
    });
  });

  group('Method forgetCredential behaves correctly', () {
    test(
      'Method forgetCredential a later hello presents unpaired instead of the forgotten credential',
      () async {
        await storage.save(
          Fixtures.buildPersistedClientState(
            clientId: 'client-1',
            credential: 'a1b2c3d4e5f6',
          ),
        );
        await client.forgetCredential();
        await client.connect(Uri.parse('ws://127.0.0.1:58231/'));
        transport.queueResponse(_rawFixture('connection/hello-ack.json'));
        transport.queueResponse(
          _rawFixture('capabilities/capabilities-bridge.json'),
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
          _rawFixture('capabilities/capabilities-bridge.json'),
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
        _rawFixture('capabilities/capabilities-bridge.json'),
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
              'bridgeVersion': '0.3.2',
              'clientIdentityKind': 'unpaired',
            },
            'bridgeInstanceId': 'bridge-1',
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
          _rawFixture('capabilities/capabilities-bridge.json'),
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
        _rawFixture('capabilities/capabilities-bridge.json'),
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
          await client.connect(Uri.parse('ws://127.0.0.1:58231/'));
          transport.queueResponse(
            _rawFixture('connection/hello-ack-paired.json'),
          );
          transport.queueResponse(
            _rawFixture('capabilities/capabilities-bridge.json'),
          );
          await client.hello();

          transport.queueResponse(_rawSessionInvalidated(entry.value));
          await pumpEventQueue();

          final PersistedClientState stored = await storage.load();
          expect(stored.clientId, 'client-1');
          expect(stored.credential, isNull);
          expect(stored.recoveryState, PairingRecoveryState.none);
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
        await client.connect(Uri.parse('ws://127.0.0.1:58231/'));
        transport.queueResponse(
          _rawFixture('connection/hello-ack-paired.json'),
        );
        transport.queueResponse(
          _rawFixture('capabilities/capabilities-bridge.json'),
        );
        await client.hello();

        transport.queueResponse(_rawSessionInvalidated('revoked'));
        await pumpEventQueue();

        final PersistedClientState stored = await storage.load();
        expect(stored.clientId, 'client-1');
        expect(stored.credential, isNull);
        expect(stored.recoveryState, PairingRecoveryState.none);
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
        final DovahLinkClient trackingClient = DovahLinkClient(
          transport: trackingTransport,
          storage: trackingStorage,
        );
        addTearDown(trackingClient.disconnect);

        await trackingClient.connect(Uri.parse('ws://127.0.0.1:58231/'));
        trackingTransport.queueResponse(
          _rawFixture('connection/hello-ack-paired.json'),
        );
        trackingTransport.queueResponse(
          _rawFixture('capabilities/capabilities-bridge.json'),
        );
        await trackingClient.hello();

        trackingTransport.queueResponse(_rawSessionInvalidated('revoked'));
        trackingTransport.failMessagesWithBoth(
          const SocketException('closed by bridge'),
        );
        await pumpEventQueue();

        expect(trackingStorage.saveCount, 1);
        expect((await trackingStorage.load()).credential, isNull);
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
        )..saveError = StateError('storage unavailable');
        final FakeDovahLinkTransport failingTransport =
            FakeDovahLinkTransport();
        final DovahLinkClient failingClient = DovahLinkClient(
          transport: failingTransport,
          storage: failingStorage,
        );
        addTearDown(failingClient.disconnect);

        await failingClient.connect(Uri.parse('ws://127.0.0.1:58231/'));
        failingTransport.queueResponse(
          _rawFixture('connection/hello-ack-paired.json'),
        );
        failingTransport.queueResponse(
          _rawFixture('capabilities/capabilities-bridge.json'),
        );
        await failingClient.hello();

        final List<Object> uncaughtErrors = <Object>[];
        final Completer<void> done = Completer<void>();
        runZonedGuarded(() async {
          failingTransport.queueResponse(_rawSessionInvalidated('revoked'));
          await pumpEventQueue();
          done.complete();
        }, (Object error, StackTrace stackTrace) => uncaughtErrors.add(error));
        await done.future;

        expect(uncaughtErrors, isEmpty);
        expect(failingStorage.saveCount, 1);
        expect((await failingStorage.load()).credential, 'credential-1');
      },
    );

    test(
      'Behavior session_invalidated handling fails a pending operation with a connection exception '
      'while it awaits a reply',
      () async {
        await client.connect(Uri.parse('ws://127.0.0.1:58231/'));
        transport.queueResponse(_rawFixture('connection/hello-ack.json'));
        transport.queueResponse(
          _rawFixture('capabilities/capabilities-bridge.json'),
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
        _rawFixture('capabilities/capabilities-bridge.json'),
      );
      await client.hello();

      // Both delivered on the same still-active subscription before either is processed,
      // simulating the bridge's own follow-up socket close racing this SDK's own
      // subscription-cancellation cleanup for session_invalidated.
      transport.queueResponse(_rawSessionInvalidated('blocked'));
      transport.failMessagesWith(const SocketException('closed by bridge'));
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
        _rawFixture('capabilities/capabilities-bridge.json'),
      );
      final HelloResult result = await client.hello();

      expect(transport.connectCalls, hasLength(2));
      expect(result.trustState, DovahLinkTrustState.unpaired);
      expect(client.connectionState, DovahLinkConnectionState.connected);
      expect(client.invalidationReason, isNull);
      // The stable clientId survives explicit recovery -- only the rejected credential was
      // discarded, per `ai/context/sdk/persistence.md`.
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
    test('Behavior retry-safe reconnect retransmits an orphaned operation and resolves its caller, '
        'via automatic reconnect', () async {
      await client.connect(Uri.parse('ws://127.0.0.1:58231/'));
      transport.queueResponse(_rawFixture('connection/hello-ack.json'));
      transport.queueResponse(
        _rawFixture('capabilities/capabilities-bridge.json'),
      );
      await client.hello();

      final Future<PairingChallengeStatus> pending = client.requestPairing();
      await pumpEventQueue();
      // Queued ahead of the drop so bounded automatic reconnect's own connect()+hello()+retry
      // finds them ready the moment it retries -- nothing in this test drives reconnect by hand.
      transport.queueResponse(_rawFixture('connection/hello-ack.json'));
      transport.queueResponse(
        _rawFixture('capabilities/capabilities-bridge.json'),
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

    test('Behavior retry-safe reconnect fails without retransmission when trust state changes, via '
        'automatic reconnect', () async {
      await client.connect(Uri.parse('ws://127.0.0.1:58231/'));
      transport.queueResponse(_rawFixture('connection/hello-ack.json'));
      transport.queueResponse(
        _rawFixture('capabilities/capabilities-bridge.json'),
      );
      await client.hello();

      final Future<PairingChallengeStatus> pending = client.requestPairing();
      await pumpEventQueue();
      // The reconnected session comes back already trusted -- pairing_request requires
      // unpaired, so the orphaned request must fail instead of being retried into a session it
      // was never classified for. Queued ahead of the drop so automatic reconnect's own
      // connect()+hello() finds it ready the moment it retries.
      transport.queueResponse(
        jsonEncode(<String, dynamic>{
          'messageType': 'hello_ack',
          'messageId': 'message-hello-ack-2',
          'sessionId': 'session-2',
          'correlationId': 'irrelevant',
          'payload': <String, dynamic>{
            'bridgeVersion': '0.2.0',
            'clientIdentityKind': 'paired',
          },
          'bridgeInstanceId': 'bridge-1',
          'playContextId': null,
          'clientId': 'client-1',
        }),
      );
      transport.queueResponse(
        _rawFixture('capabilities/capabilities-bridge.json'),
      );
      transport.failMessagesWith(const SocketException('dropped'));

      await expectLater(pending, throwsA(isA<DovahLinkConnectionException>()));
      expect(client.connectionState, DovahLinkConnectionState.connected);
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
          _rawFixture('capabilities/capabilities-bridge.json'),
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
          _rawFixture('capabilities/capabilities-bridge.json'),
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
          _rawFixture('capabilities/capabilities-bridge.json'),
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
          _rawFixture('capabilities/capabilities-bridge.json'),
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
          _rawFixture('capabilities/capabilities-bridge.json'),
        );
        await client.hello();

        final Future<String> pending = client.confirmPairingCode(
          code: '123456',
        );
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
    test('Behavior automatic reconnect passes through reauthenticating before resolving to '
        'connected, never exposing connected before hello succeeds', () async {
      final List<DovahLinkConnectionState> observed =
          <DovahLinkConnectionState>[];
      final StreamSubscription<DovahLinkConnectionState> subscription = client
          .connectionStateChanges
          .listen(observed.add);
      addTearDown(subscription.cancel);

      await client.connect(Uri.parse('ws://127.0.0.1:58231/'));
      transport.queueResponse(_rawFixture('connection/hello-ack.json'));
      transport.queueResponse(
        _rawFixture('capabilities/capabilities-bridge.json'),
      );
      await client.hello();

      // Queued ahead of the drop so bounded automatic reconnect's own connect()+hello() finds
      // them ready the moment its first (zero-delay) attempt runs -- nothing in this test drives
      // reconnect by hand.
      transport.queueResponse(_rawFixture('connection/hello-ack.json'));
      transport.queueResponse(
        _rawFixture('capabilities/capabilities-bridge.json'),
      );
      transport.failMessagesWith(const SocketException('dropped'));

      // A fixed pump count, not a "not yet connected" condition -- connectionState is already
      // `connected` before the drop, so a condition guarding on that would exit before the drop
      // is ever actually processed. Includes a real wait past kReconnectAttemptDelays[1] in case
      // the immediate first attempt does not land within pumped microtasks alone.
      await pumpEventQueue();
      await Future<void>.delayed(const Duration(milliseconds: 1500));
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
      await _connectAndHello(transport, client);

      //  Answers the first (zero-delay) automatic attempt's hello with a retryable rejection --
      // built inline, mirroring protocol/fixtures/errors/error-rate-limited.json, since that
      // canonical fixture's own correlationId is null (an unsolicited push shape) and this case
      // needs one correlated to the hello it rejects, the same way
      // errors/error-unauthenticated-invalid-token.json already is.
      transport.queueResponse(
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
          'bridgeInstanceId': null,
          'playContextId': null,
          'clientId': null,
        }),
      );
      // Answers the second automatic attempt (after kReconnectAttemptDelays[1]'s real 1-second
      // delay) with success.
      transport.queueResponse(_rawFixture('connection/hello-ack.json'));
      transport.queueResponse(
        _rawFixture('capabilities/capabilities-bridge.json'),
      );
      transport.failMessagesWith(const SocketException('dropped'));

      // Lets the immediate first attempt run and fail, then waits out the real delay before the
      // second attempt -- no override point exists on DovahLinkClient's public constructor to
      // shorten kReconnectAttemptDelays for this test. A fixed pump count, not a "not yet
      // connected" condition -- connectionState is already `connected` from _connectAndHello
      // before the drop, so a condition guarding on that would exit before the drop is ever
      // actually processed.
      await pumpEventQueue();
      await Future<void>.delayed(const Duration(milliseconds: 1500));
      for (int i = 0; i < 20; i++) {
        await pumpEventQueue();
      }

      expect(client.connectionState, DovahLinkConnectionState.connected);
      final List<String> helloSends = transport.sent
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
      'Behavior automatic reconnect discards a credential the bridge rejects as blocked while '
      'recovering, preserving clientId and ending the cycle without retrying',
      () async {
        await storage.save(
          Fixtures.buildPersistedClientState(
            clientId: 'client-1',
            credential: 'stale-cred',
          ),
        );
        await client.connect(Uri.parse('ws://127.0.0.1:58231/'));
        transport.queueResponse(
          _rawFixture('connection/hello-ack-paired.json'),
        );
        transport.queueResponse(
          _rawFixture('capabilities/capabilities-bridge.json'),
        );
        await client.hello();

        // Answers the automatic reconnect's own hello -- the bridge decides, while this device
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
            'bridgeInstanceId': null,
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
        expect(stored.credential, isNull);
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
          _rawFixture('capabilities/capabilities-bridge.json'),
        );
        await client.hello();

        // confirmPairingCode (not retry-safe) rather than requestPairing here: the point of this
        // test is what happens to a stale reply arriving late for an old, dead generation, not
        // retry behavior -- a retry-safe first request would itself get auto-retried by the
        // second hello() below, which is exactly the mechanism the sibling group above already
        // covers and would confuse this test's own generation-isolation assertion.
        final Future<String> firstRequest = client.confirmPairingCode(
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
          _rawFixture('capabilities/capabilities-bridge.json'),
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
            'bridgeInstanceId': 'bridge-1',
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
          transport: WebSocketTransport(),
          storage: storage,
        );

        await expectLater(
          realTransportClient.hello(),
          throwsA(isA<DovahLinkConnectionException>()),
        );
      },
    );

    test(
      'Behavior request timeout handling fails and disconnects after its timeout class duration',
      () async {
        final DovahLinkClient timeoutClient =
            DovahLinkClient.withTimeoutDurations(
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

    test('Behavior request timeout handling retransmits retry-safe acknowledgeTrustedCredential '
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
        _rawFixture('capabilities/capabilities-bridge.json'),
      );
      await client.hello();

      final Future<void> pending = client.acknowledgeTrustedCredential(
        'a1b2c3d4e5f6',
      );
      final Future<void> pendingCompletes = expectLater(pending, completes);
      await pumpEventQueue();
      // Queued ahead of the drop so bounded automatic reconnect's own connect()+hello()+retry
      // finds them ready the moment it retries -- nothing in this test drives reconnect by hand.
      transport.queueResponse(_rawFixture('connection/hello-ack.json'));
      transport.queueResponse(
        _rawFixture('capabilities/capabilities-bridge.json'),
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
          _rawFixture('capabilities/capabilities-bridge.json'),
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
            'bridgeInstanceId': 'bridge-1',
            'playContextId': null,
            'clientId': null,
          }),
        );
        await pendingFails;

        // Confirmed not orphaned: a fresh connect/hello does not retransmit it.
        await client.connect(Uri.parse('ws://127.0.0.1:58231/'));
        transport.queueResponse(_rawFixture('connection/hello-ack.json'));
        transport.queueResponse(
          _rawFixture('capabilities/capabilities-bridge.json'),
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
          _rawFixture('capabilities/capabilities-bridge.json'),
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
      // Proves, through a real DovahLinkClient composed of real ConnectionTeardownCoordinator
      // and LifecycleOperationQueue instances (not mocked away, unlike every Service's own
      // unit test), that a stream's onError and onDone both firing for one dead connection --
      // exactly what a real dropped socket delivers -- runs real teardown/close exactly once,
      // per `ai/context/sdk/testing.md`'s "Session/request refactor regression requirements".
      await client.connect(Uri.parse('ws://127.0.0.1:58231/'));
      transport.queueResponse(_rawFixture('connection/hello-ack.json'));
      transport.queueResponse(
        _rawFixture('capabilities/capabilities-bridge.json'),
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
}
