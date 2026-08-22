import 'dart:async';
import 'dart:convert';
import 'dart:io';

import 'package:test/test.dart';

import 'package:dovahlink_client_sdk/dovahlink_client.dart';
import 'package:dovahlink_client_sdk/src/persistence/in_memory_client_storage.dart';
import 'package:dovahlink_client_sdk/src/protocol/json_map.dart';
import 'package:dovahlink_client_sdk/src/shared/enums.dart' show TimeoutClass;
import 'package:dovahlink_client_sdk/src/transport/websocket_transport.dart';

/// One [FakeDovahLinkTransport.queueResponse] entry awaiting release, in queue order. An
/// uncorrelated entry (already `correlationId: null`, or not even valid JSON) needs nothing and
/// releases as soon as it is at the front of the queue; a correlated entry additionally needs an
/// unconsumed sent `messageId` to rewrite its `correlationId` to before it can release.
class _PendingReply {
  _PendingReply.immediate(this._raw) : _decoded = null;
  _PendingReply.correlated(this._decoded) : _raw = null;

  final String? _raw;
  final JsonMap? _decoded;

  bool get needsCorrelation => _decoded != null;

  String resolve([String? messageId]) {
    final JsonMap? decoded = _decoded;
    if (decoded == null) {
      return _raw!;
    }
    decoded['correlationId'] = messageId;
    return jsonEncode(decoded);
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
  final List<_PendingReply> _pendingReplies = <_PendingReply>[];

  /// The current connection's inbound stream, or `null` before [connect]/after [close].
  StreamController<String>? _incoming;

  /// The URI passed to [connect], or `null` if not yet called.
  Uri? connectedUri;

  /// Every URI passed to [connect], in order -- unlike [connectedUri], proves how many times and
  /// with what arguments [connect] was called across a retry.
  final List<Uri> connectCalls = <Uri>[];

  /// Whether [close] was called.
  bool closeCalled = false;

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
    _pendingReplies.add(
      decoded == null || decoded['correlationId'] == null
          ? _PendingReply.immediate(rawJson)
          : _PendingReply.correlated(decoded),
    );
    // Only ever releases a leading run of uncorrelated entries (nothing needs a send for those);
    // a correlated entry always waits for its own [send] specifically -- see [_releaseUpTo].
    while (_pendingReplies.isNotEmpty &&
        !_pendingReplies.first.needsCorrelation) {
      _requireIncoming().add(_pendingReplies.removeAt(0).resolve());
    }
  }

  /// Delivers [rawJson] exactly as given, bypassing auto-correlation and queue ordering -- for a
  /// test that deliberately wants an unrewritten or mismatched `correlationId`.
  void queueRawResponse(String rawJson) => _requireIncoming().add(rawJson);

  /// Delivers [error] on the current connection's inbound stream, simulating a transport-level
  /// failure (e.g. a dropped socket) while a request may be pending.
  void failMessagesWith(Object error) => _requireIncoming().addError(error);

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

  @override
  Future<void> send(String text) async {
    final Object? failure = failSendWith;
    if (failure != null) {
      throw failure;
    }
    sent.add(text);
    _releaseUpTo((jsonDecode(text) as JsonMap)['messageId'] as String);
  }

  @override
  Stream<String> get messages => _requireIncoming().stream;

  @override
  Future<void> close() async {
    closeCalled = true;
    _incoming = null;
    final Object? failure = failCloseWith;
    if (failure != null) {
      throw failure;
    }
  }

  /// Releases every leading uncorrelated reply, then -- if the next one needs correlation --
  /// releases exactly that one using [messageId], the `messageId` [send] just used. A `send`
  /// whose own turn finds no correlated reply queued for it consumes nothing and leaves no trace:
  /// deliberately not tracked for a later, unrelated [queueResponse] to latch onto -- a request a
  /// test means to leave unanswered (a drop/timeout scenario) must never accidentally supply the
  /// correlationId for some other reply queued afterward.
  void _releaseUpTo(String messageId) {
    bool consumedThisSend = false;
    while (_pendingReplies.isNotEmpty) {
      final _PendingReply next = _pendingReplies.first;
      if (!next.needsCorrelation) {
        _pendingReplies.removeAt(0);
        _requireIncoming().add(next.resolve());
        continue;
      }
      if (consumedThisSend) {
        return;
      }
      _pendingReplies.removeAt(0);
      _requireIncoming().add(next.resolve(messageId));
      consumedThisSend = true;
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

/// Reads one canonical protocol fixture as raw JSON text, relative to `protocol/fixtures/`.
String _rawFixture(String relativePath) =>
    File('../../../protocol/fixtures/$relativePath').readAsStringSync();

/// Builds a minimal `capabilities` envelope. No canonical fixture exists for this message type yet
/// (out of the pairing epic this shared fixture set was built for); this client only needs to
/// consume and discard it, so a hand-built stand-in is sufficient.
String _rawCapabilities() => jsonEncode(<String, dynamic>{
  'messageType': 'capabilities',
  'messageId': 'message-capabilities-1',
  'sessionId': 'session-1',
  'correlationId': null,
  'payload': <String, dynamic>{'capabilities': <dynamic>[]},
  'bridgeInstanceId': 'bridge-1',
  'playContextId': null,
  'clientId': null,
});

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

void main() {
  late FakeDovahLinkTransport transport;
  late InMemoryClientStorage storage;
  late DovahLinkClient client;

  setUp(() {
    transport = FakeDovahLinkTransport();
    storage = InMemoryClientStorage();
    client = DovahLinkClient(transport: transport, storage: storage);
  });

  group('connect', () {
    test(
      'reaches connected state and forwards the URI to the transport',
      () async {
        final Uri uri = Uri.parse('ws://127.0.0.1:58231/');

        await client.connect(uri);

        expect(client.connectionState, DovahLinkConnectionState.connected);
        expect(transport.connectedUri, uri);
      },
    );

    test(
      'wraps a transport failure as DovahLinkConnectionException and stays disconnected',
      () async {
        transport.failConnectWith = const SocketException('refused');

        await expectLater(
          client.connect(Uri.parse('ws://127.0.0.1:58231/')),
          throwsA(isA<DovahLinkConnectionException>()),
        );
        expect(client.connectionState, DovahLinkConnectionState.disconnected);
      },
    );

    test(
      'wraps a transport send failure (e.g. sending before connecting) as DovahLinkConnectionException',
      () async {
        transport.failSendWith = StateError(
          'Not connected. Call connect() first.',
        );

        await expectLater(
          client.hello(),
          throwsA(isA<DovahLinkConnectionException>()),
        );
      },
    );
  });

  group('hello', () {
    test('generates and persists a clientId on first use', () async {
      transport.queueResponse(_rawFixture('connection/hello-ack.json'));
      transport.queueResponse(_rawCapabilities());

      await client.hello();

      expect(client.clientId, isNotNull);
      final PersistedClientState stored = await storage.load();
      expect(stored.clientId, client.clientId);
    });

    test('reuses the persisted clientId on a later call', () async {
      await storage.save(const PersistedClientState(clientId: 'client-1'));
      transport.queueResponse(_rawFixture('connection/hello-ack.json'));
      transport.queueResponse(_rawCapabilities());

      await client.hello();

      expect(client.clientId, 'client-1');
      final JsonMap sentEnvelope = jsonDecode(transport.sent.single) as JsonMap;
      final JsonMap sentPayload = sentEnvelope['payload'] as JsonMap;
      expect(sentPayload['clientId'], 'client-1');
    });

    test(
      'an unpaired hello (no stored credential) sets sessionId and trustState from the real fixtures',
      () async {
        transport.queueResponse(_rawFixture('connection/hello-ack.json'));
        transport.queueResponse(_rawCapabilities());

        final HelloResult result = await client.hello();

        expect(result.bridgeVersion, '0.3.2');
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
      'sends trusted_device_credential when a trusted credential is stored',
      () async {
        await storage.save(
          const PersistedClientState(
            clientId: 'client-1',
            credential: 'a1b2c3d4e5f6',
          ),
        );
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
        transport.queueResponse(_rawCapabilities());

        final HelloResult result = await client.hello();

        expect(result.trustState, DovahLinkTrustState.trusted);
        expect(client.trustState, DovahLinkTrustState.trusted);

        final JsonMap sentPayload =
            (jsonDecode(transport.sent.single) as JsonMap)['payload']
                as JsonMap;
        expect(sentPayload['auth'], <String, dynamic>{
          'method': 'trusted_device_credential',
          'token': 'a1b2c3d4e5f6',
        });
      },
    );

    test(
      'sends unpaired when a credential is stored but recovery is still CONFIRMING',
      () async {
        await storage.save(
          const PersistedClientState(
            clientId: 'client-1',
            credential: 'a1b2c3d4e5f6',
            recoveryState: PairingRecoveryState.confirming,
          ),
        );
        transport.queueResponse(_rawFixture('connection/hello-ack.json'));
        transport.queueResponse(_rawCapabilities());

        final HelloResult result = await client.hello();

        expect(result.trustState, DovahLinkTrustState.unpaired);
        final JsonMap sentPayload =
            (jsonDecode(transport.sent.single) as JsonMap)['payload']
                as JsonMap;
        expect(sentPayload['auth'], <String, dynamic>{'method': 'unpaired'});
      },
    );

    test(
      'a rejected hello throws DovahLinkProtocolException and leaves state unset',
      () async {
        await storage.save(
          const PersistedClientState(
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
                  'unauthenticated',
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
      'the original rejection still surfaces even when cleanup itself fails',
      () async {
        await storage.save(
          const PersistedClientState(
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
              'unauthenticated',
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

    test('a malformed message arriving after hello succeeds still resets session state, via the '
        'background receiver', () async {
      await client.connect(Uri.parse('ws://127.0.0.1:58231/'));
      // hello_ack resolves hello() as soon as it is correlated -- hello() does not wait for
      // capabilities. Queuing malformed JSON in its place proves the persistent receiver's own
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
      'a revoked-credential rejection throws DovahLinkProtocolException with code revoked',
      () async {
        await storage.save(
          const PersistedClientState(
            clientId: 'client-1',
            credential: 'deadbeef',
          ),
        );
        transport.queueResponse(_rawFixture('errors/error-revoked.json'));

        await expectLater(
          client.hello(),
          throwsA(
            isA<DovahLinkProtocolException>()
                .having(
                  (DovahLinkProtocolException e) => e.code,
                  'code',
                  'revoked',
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
        expect(transport.closeCalled, isTrue);
      },
    );

    test(
      'a freshly generated clientId is still persisted even when the hello_ack is rejected',
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
      'an unrecognized clientIdentityKind throws DovahLinkProtocolException(malformed_message)',
      () async {
        transport.queueResponse(
          jsonEncode(<String, dynamic>{
            'messageType': 'hello_ack',
            'messageId': 'message-hello-ack-1',
            'sessionId': 'session-1',
            // Auto-correlated by FakeDovahLinkTransport.queueResponse to the real hello messageId
            // -- a placeholder here, but must be non-null to be treated as a correlated reply
            // rather than an unsolicited push.
            'correlationId': 'irrelevant',
            'payload': <String, dynamic>{
              'bridgeVersion': '0.2.0',
              'clientIdentityKind': 'not-a-real-kind',
            },
            'bridgeInstanceId': 'bridge-1',
            'playContextId': null,
            'clientId': 'client-1',
          }),
        );

        await expectLater(
          client.hello(),
          throwsA(
            isA<DovahLinkProtocolException>().having(
              (DovahLinkProtocolException e) => e.code,
              'code',
              'malformed_message',
            ),
          ),
        );
        expect(transport.closeCalled, isTrue);
      },
    );

    test(
      'a malformed JSON response throws DovahLinkConnectionException',
      () async {
        transport.queueResponse('not valid json');

        await expectLater(
          client.hello(),
          throwsA(isA<DovahLinkConnectionException>()),
        );
        // A transport-level failure leaves the socket just as unusable as a protocol rejection;
        // the reset must cover both, not only DovahLinkProtocolException.
        expect(transport.closeCalled, isTrue);
      },
    );
  });

  group('authenticate', () {
    test('delegates to connect and hello when nothing is rejected', () async {
      transport.queueResponse(_rawFixture('connection/hello-ack.json'));
      transport.queueResponse(_rawCapabilities());

      final HelloResult result = await client.authenticate(
        Uri.parse('ws://127.0.0.1:58231/'),
      );

      expect(result.trustState, DovahLinkTrustState.unpaired);
      expect(result.recoveredFromRejectedCredential, isNull);
      expect(transport.connectedUri, Uri.parse('ws://127.0.0.1:58231/'));
    });

    test(
      'recovers from a revoked credential by forgetting it and retrying as unpaired',
      () async {
        await storage.save(
          const PersistedClientState(
            clientId: 'client-1',
            credential: 'deadbeef',
          ),
        );
        transport.queueResponse(_rawFixture('errors/error-revoked.json'));
        transport.queueResponse(_rawFixture('connection/hello-ack.json'));
        transport.queueResponse(_rawCapabilities());

        final HelloResult result = await client.authenticate(
          Uri.parse('ws://127.0.0.1:58231/'),
        );

        expect(
          result.recoveredFromRejectedCredential,
          CredentialRejectionReason.revoked,
        );
        final PersistedClientState stored = await storage.load();
        expect(stored.clientId, 'client-1');
        expect(stored.credential, isNull);
        expect(transport.connectCalls, [
          Uri.parse('ws://127.0.0.1:58231/'),
          Uri.parse('ws://127.0.0.1:58231/'),
        ]);
      },
    );

    test(
      'recovers from an unrecognized credential by forgetting it and retrying as unpaired',
      () async {
        await storage.save(
          const PersistedClientState(
            clientId: 'client-1',
            credential: 'deadbeef',
          ),
        );
        transport.queueResponse(
          _rawFixture('errors/error-unauthenticated-invalid-token.json'),
        );
        transport.queueResponse(_rawFixture('connection/hello-ack.json'));
        transport.queueResponse(_rawCapabilities());

        final HelloResult result = await client.authenticate(
          Uri.parse('ws://127.0.0.1:58231/'),
        );

        expect(
          result.recoveredFromRejectedCredential,
          CredentialRejectionReason.unrecognized,
        );
      },
    );

    test(
      'rethrows a non-recoverable protocol rejection without forgetting the credential',
      () async {
        await storage.save(
          const PersistedClientState(
            clientId: 'client-1',
            credential: 'deadbeef',
          ),
        );
        transport.queueResponse(
          _rawFixture('errors/error-malformed-message.json'),
        );

        await expectLater(
          client.authenticate(Uri.parse('ws://127.0.0.1:58231/')),
          throwsA(
            isA<DovahLinkProtocolException>().having(
              (DovahLinkProtocolException e) => e.code,
              'code',
              'malformed_message',
            ),
          ),
        );
        final PersistedClientState stored = await storage.load();
        expect(stored.credential, 'deadbeef');
      },
    );

    test("rethrows the retry's own rejection without retrying again", () async {
      await storage.save(
        const PersistedClientState(
          clientId: 'client-1',
          credential: 'deadbeef',
        ),
      );
      transport.queueResponse(_rawFixture('errors/error-revoked.json'));
      // error-rate-limited.json's own canonical correlationId is null (a rejection issued before
      // the request context is resolved); this retry's rejection needs a non-null one so
      // FakeDovahLinkTransport.queueResponse treats it as this specific retry's correlated reply
      // rather than an unsolicited push delivered before the retry is even sent.
      transport.queueResponse(
        jsonEncode(<String, dynamic>{
          'messageType': 'error',
          'messageId': 'message-error-rate-limited-1',
          'sessionId': 'session-1',
          'correlationId': 'irrelevant',
          'payload': <String, dynamic>{
            'code': 'rate_limited',
            'message': 'Inbound message rate exceeded 100 messages per second',
            'retryable': true,
            'details': null,
          },
          'bridgeInstanceId': 'bridge-1',
          'playContextId': null,
          'clientId': null,
        }),
      );

      await expectLater(
        client.authenticate(Uri.parse('ws://127.0.0.1:58231/')),
        throwsA(
          isA<DovahLinkProtocolException>().having(
            (DovahLinkProtocolException e) => e.code,
            'code',
            'rate_limited',
          ),
        ),
      );
    });

    test(
      'does not retry a second time when the retry is rejected with the same recoverable code',
      () async {
        await storage.save(
          const PersistedClientState(
            clientId: 'client-1',
            credential: 'deadbeef',
          ),
        );
        transport.queueResponse(_rawFixture('errors/error-revoked.json'));
        transport.queueResponse(_rawFixture('errors/error-revoked.json'));

        await expectLater(
          client.authenticate(Uri.parse('ws://127.0.0.1:58231/')),
          throwsA(
            isA<DovahLinkProtocolException>().having(
              (DovahLinkProtocolException e) => e.code,
              'code',
              'revoked',
            ),
          ),
        );
        // Exactly one retry: forgetCredential ran once and connect() was called exactly twice,
        // not looping on a second revoked rejection.
        final PersistedClientState stored = await storage.load();
        expect(stored.credential, isNull);
        expect(transport.connectCalls, hasLength(2));
      },
    );

    test(
      'propagates a transport failure on the initial connect without calling hello',
      () async {
        transport.failConnectWith = const SocketException('refused');

        await expectLater(
          client.authenticate(Uri.parse('ws://127.0.0.1:58231/')),
          throwsA(isA<DovahLinkConnectionException>()),
        );
        expect(transport.sent, isEmpty);
      },
    );

    test(
      'returns the cached result without reconnecting when already connected and trusted',
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
        transport.queueResponse(_rawCapabilities());
        final Uri uri = Uri.parse('ws://127.0.0.1:58231/');
        final HelloResult first = await client.authenticate(uri);
        expect(first.trustState, DovahLinkTrustState.trusted);

        final HelloResult second = await client.authenticate(uri);

        expect(second.bridgeVersion, first.bridgeVersion);
        expect(second.trustState, DovahLinkTrustState.trusted);
        expect(second.recoveredFromRejectedCredential, isNull);
        expect(transport.connectCalls, hasLength(1));
        expect(transport.sent, hasLength(1));
      },
    );

    test('reconnects when already connected but still unpaired', () async {
      transport.queueResponse(_rawFixture('connection/hello-ack.json'));
      transport.queueResponse(_rawCapabilities());
      final Uri uri = Uri.parse('ws://127.0.0.1:58231/');
      final HelloResult first = await client.authenticate(uri);
      expect(first.trustState, DovahLinkTrustState.unpaired);
      // Real transport parity: connect() throws if the previous connection was never closed, so a
      // caller reconnecting an unpaired session must disconnect first -- matching the pattern the
      // trusted-reconnect test below already uses.
      await client.disconnect();

      transport.queueResponse(_rawFixture('connection/hello-ack.json'));
      transport.queueResponse(_rawCapabilities());
      await client.authenticate(uri);

      expect(transport.connectCalls, hasLength(2));
    });

    test(
      'reconnects after disconnect even though the client was last trusted',
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
        transport.queueResponse(_rawCapabilities());
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
        transport.queueResponse(_rawCapabilities());
        await client.authenticate(uri);

        expect(transport.connectCalls, hasLength(2));
      },
    );
  });

  group('requestPairing', () {
    const Map<PairingAvailability, String>
    stateFixtures = <PairingAvailability, String>{
      PairingAvailability.unavailable:
          'pairing/pairing-status-unavailable.json',
      PairingAvailability.available: 'pairing/pairing-status-available.json',
      PairingAvailability.inProgress: 'pairing/pairing-status-in-progress.json',
      PairingAvailability.otherDevicePairing:
          'pairing/pairing-status-other-device.json',
    };
    for (final MapEntry<PairingAvailability, String> entry
        in stateFixtures.entries) {
      test('reports ${entry.key} from the real fixture', () async {
        transport.queueResponse(_rawFixture(entry.value));

        final PairingChallengeStatus status = await client.requestPairing();

        expect(status.availability, entry.key);
      });
    }

    test('reports expiresInSeconds for an available fixture', () async {
      transport.queueResponse(
        _rawFixture('pairing/pairing-status-available.json'),
      );

      final PairingChallengeStatus status = await client.requestPairing();

      expect(status.expiresInSeconds, 300);
    });

    test('reports expiresInSeconds for an in_progress fixture', () async {
      transport.queueResponse(
        _rawFixture('pairing/pairing-status-in-progress.json'),
      );

      final PairingChallengeStatus status = await client.requestPairing();

      expect(status.expiresInSeconds, 187);
    });

    test(
      'reports expiresInSeconds as null for an unavailable fixture',
      () async {
        transport.queueResponse(
          _rawFixture('pairing/pairing-status-unavailable.json'),
        );

        final PairingChallengeStatus status = await client.requestPairing();

        expect(status.expiresInSeconds, isNull);
      },
    );

    test(
      'reports expiresInSeconds as null for an other_device_pairing fixture',
      () async {
        transport.queueResponse(
          _rawFixture('pairing/pairing-status-other-device.json'),
        );

        final PairingChallengeStatus status = await client.requestPairing();

        expect(status.expiresInSeconds, isNull);
      },
    );

    test(
      'an unrecognized state throws DovahLinkProtocolException(malformed_message)',
      () async {
        transport.queueResponse(
          jsonEncode(<String, dynamic>{
            'messageType': 'pairing_status',
            'messageId': 'message-1',
            'sessionId': null,
            // Auto-correlated by FakeDovahLinkTransport.queueResponse to the real
            // pairing_request messageId -- a placeholder here, but must be non-null to be
            // treated as a correlated reply rather than an unsolicited push.
            'correlationId': 'irrelevant',
            'payload': <String, dynamic>{
              'state': 'not-a-real-state',
              'expiresInSeconds': null,
            },
            'bridgeInstanceId': 'bridge-1',
            'playContextId': null,
            'clientId': null,
          }),
        );

        await expectLater(
          client.requestPairing(),
          throwsA(
            isA<DovahLinkProtocolException>().having(
              (DovahLinkProtocolException e) => e.code,
              'code',
              'malformed_message',
            ),
          ),
        );
      },
    );

    test('propagates the sessionId a prior hello established', () async {
      transport.queueResponse(_rawFixture('connection/hello-ack.json'));
      transport.queueResponse(_rawCapabilities());
      await client.hello();

      transport.queueResponse(
        _rawFixture('pairing/pairing-status-available.json'),
      );
      await client.requestPairing();

      final JsonMap sentRequest = jsonDecode(transport.sent.last) as JsonMap;
      expect(sentRequest['sessionId'], 'session-1');
    });

    test(
      'sends an empty payload, matching the pairing_request fixture shape',
      () async {
        transport.queueResponse(
          _rawFixture('pairing/pairing-status-available.json'),
        );

        await client.requestPairing();

        final JsonMap sent = jsonDecode(transport.sent.single) as JsonMap;
        expect(sent['messageType'], 'pairing_request');
        expect(sent['payload'], <String, dynamic>{});
      },
    );

    test('a transport failure mid-request resets connection state, not just hello\'s, for a '
        'non-retry-safe operation', () async {
      // pairing_confirm is not retrySafe (unlike pairing_request): a send failure must fail it
      // immediately rather than parking it to retry after a reconnect -- see the "retry-safe
      // operations across reconnect" group below for the retrySafe counterpart of this case.
      transport.failSendWith = const SocketException('reset');

      await expectLater(
        client.confirmPairingCode(code: '123456'),
        throwsA(isA<DovahLinkConnectionException>()),
      );

      expect(client.connectionState, DovahLinkConnectionState.disconnected);
      expect(transport.closeCalled, isTrue);
    });
  });

  group('requestPairingRenotify', () {
    test('reports renotified from the real fixture', () async {
      transport.queueResponse(
        _rawFixture('pairing/pairing-outcome-renotified.json'),
      );

      final PairingRenotifyResult result = await client
          .requestPairingRenotify();

      expect(result.status, PairingRenotifyStatus.renotified);
      expect(result.retryAfterSeconds, isNull);
    });

    test(
      'reports cooldown with retryAfterSeconds from the real fixture',
      () async {
        transport.queueResponse(
          _rawFixture('pairing/pairing-outcome-renotify-cooldown.json'),
        );

        final PairingRenotifyResult result = await client
            .requestPairingRenotify();

        expect(result.status, PairingRenotifyStatus.cooldown);
        expect(result.retryAfterSeconds, 3);
      },
    );

    test('reports alreadyIdle from the real fixture', () async {
      transport.queueResponse(
        _rawFixture('pairing/pairing-outcome-already-idle.json'),
      );

      final PairingRenotifyResult result = await client
          .requestPairingRenotify();

      expect(result.status, PairingRenotifyStatus.alreadyIdle);
      expect(result.retryAfterSeconds, isNull);
    });

    test('a recognized outcome invalid for this exchange throws '
        'DovahLinkProtocolException(malformed_message)', () async {
      transport.queueResponse(
        _rawFixture('pairing/pairing-outcome-trusted.json'),
      );

      await expectLater(
        client.requestPairingRenotify(),
        throwsA(
          isA<DovahLinkProtocolException>().having(
            (DovahLinkProtocolException e) => e.code,
            'code',
            'malformed_message',
          ),
        ),
      );
    });

    test('a genuinely unrecognized outcome throws '
        'DovahLinkProtocolException(malformed_message)', () async {
      transport.queueResponse(
        jsonEncode(<String, dynamic>{
          'messageType': 'pairing_outcome',
          'messageId': 'message-1',
          'sessionId': null,
          'correlationId': 'irrelevant',
          'payload': <String, dynamic>{
            'outcome': 'not-a-real-outcome',
            'credential': null,
            'shortId': null,
            'displayName': null,
            'retryAfterSeconds': null,
          },
          'bridgeInstanceId': 'bridge-1',
          'playContextId': null,
          'clientId': null,
        }),
      );

      await expectLater(
        client.requestPairingRenotify(),
        throwsA(
          isA<DovahLinkProtocolException>().having(
            (DovahLinkProtocolException e) => e.code,
            'code',
            'malformed_message',
          ),
        ),
      );
    });

    test(
      'sends an empty payload, matching the pairing_renotify fixture shape',
      () async {
        transport.queueResponse(
          _rawFixture('pairing/pairing-outcome-renotified.json'),
        );

        await client.requestPairingRenotify();

        final JsonMap sent = jsonDecode(transport.sent.single) as JsonMap;
        expect(sent['messageType'], 'pairing_renotify');
        expect(sent['payload'], <String, dynamic>{});
      },
    );

    test('propagates the sessionId a prior hello established', () async {
      transport.queueResponse(_rawFixture('connection/hello-ack.json'));
      transport.queueResponse(_rawCapabilities());
      await client.hello();

      transport.queueResponse(
        _rawFixture('pairing/pairing-outcome-renotified.json'),
      );
      await client.requestPairingRenotify();

      final JsonMap sentRequest = jsonDecode(transport.sent.last) as JsonMap;
      expect(sentRequest['sessionId'], 'session-1');
    });
  });

  group('cancelPairing', () {
    test('reports cancelled from the real fixture', () async {
      transport.queueResponse(
        _rawFixture('pairing/pairing-outcome-cancelled.json'),
      );

      final PairingCancelOutcome outcome = await client.cancelPairing();

      expect(outcome.status, PairingCancelStatus.cancelled);
    });

    test('reports alreadyIdle from the real fixture', () async {
      transport.queueResponse(
        _rawFixture('pairing/pairing-outcome-already-idle.json'),
      );

      final PairingCancelOutcome outcome = await client.cancelPairing();

      expect(outcome.status, PairingCancelStatus.alreadyIdle);
    });

    test('a recognized outcome invalid for this exchange throws '
        'DovahLinkProtocolException(malformed_message)', () async {
      transport.queueResponse(
        _rawFixture('pairing/pairing-outcome-trusted.json'),
      );

      await expectLater(
        client.cancelPairing(),
        throwsA(
          isA<DovahLinkProtocolException>().having(
            (DovahLinkProtocolException e) => e.code,
            'code',
            'malformed_message',
          ),
        ),
      );
    });

    test(
      'sends an empty payload, matching the pairing_cancel fixture shape',
      () async {
        transport.queueResponse(
          _rawFixture('pairing/pairing-outcome-cancelled.json'),
        );

        await client.cancelPairing();

        final JsonMap sent = jsonDecode(transport.sent.single) as JsonMap;
        expect(sent['messageType'], 'pairing_cancel');
        expect(sent['payload'], <String, dynamic>{});
      },
    );

    test('propagates the sessionId a prior hello established', () async {
      transport.queueResponse(_rawFixture('connection/hello-ack.json'));
      transport.queueResponse(_rawCapabilities());
      await client.hello();

      transport.queueResponse(
        _rawFixture('pairing/pairing-outcome-cancelled.json'),
      );
      await client.cancelPairing();

      final JsonMap sentRequest = jsonDecode(transport.sent.last) as JsonMap;
      expect(sentRequest['sessionId'], 'session-1');
    });
  });

  group('confirmPairingCode', () {
    test(
      'returns the issued credential and persists it with CONFIRMING recovery',
      () async {
        await storage.save(const PersistedClientState(clientId: 'client-1'));
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
      're-pairing overwrites an existing trusted credential and flips recovery back to CONFIRMING',
      () async {
        await storage.save(
          const PersistedClientState(
            clientId: 'client-1',
            credential: 'old-credential',
          ),
        );
        transport.queueResponse(
          _rawFixture('pairing/pairing-outcome-credential-issued.json'),
        );

        final String credential = await client.confirmPairingCode(
          code: '123456',
        );

        expect(credential, 'a1b2c3d4e5f6');
        final PersistedClientState stored = await storage.load();
        expect(stored.credential, 'a1b2c3d4e5f6');
        expect(stored.recoveryState, PairingRecoveryState.confirming);
      },
    );

    const Map<PairingOutcome, String> failureFixtures =
        <PairingOutcome, String>{
          PairingOutcome.expired: 'pairing/pairing-outcome-expired.json',
          PairingOutcome.invalid: 'pairing/pairing-outcome-invalid.json',
          PairingOutcome.pacingLimited:
              'pairing/pairing-outcome-pacing-limited.json',
          PairingOutcome.hardLimitReached:
              'pairing/pairing-outcome-hard-limit-reached.json',
        };
    for (final MapEntry<PairingOutcome, String> entry
        in failureFixtures.entries) {
      test(
        'throws DovahLinkPairingException(${entry.key}) on that outcome without persisting anything',
        () async {
          transport.queueResponse(_rawFixture(entry.value));

          await expectLater(
            client.confirmPairingCode(code: '000000'),
            throwsA(
              isA<DovahLinkPairingException>().having(
                (DovahLinkPairingException e) => e.outcome,
                'outcome',
                entry.key,
              ),
            ),
          );
          final PersistedClientState stored = await storage.load();
          expect(stored, const PersistedClientState());
        },
      );
    }

    test('a genuinely unrecognized outcome throws '
        'DovahLinkProtocolException(malformed_message)', () async {
      transport.queueResponse(
        jsonEncode(<String, dynamic>{
          'messageType': 'pairing_outcome',
          'messageId': 'message-1',
          'sessionId': null,
          'correlationId': 'irrelevant',
          'payload': <String, dynamic>{
            'outcome': 'not-a-real-outcome',
            'credential': null,
            'shortId': null,
            'displayName': null,
            'retryAfterSeconds': null,
          },
          'bridgeInstanceId': 'bridge-1',
          'playContextId': null,
          'clientId': null,
        }),
      );

      await expectLater(
        client.confirmPairingCode(code: '000000'),
        throwsA(
          isA<DovahLinkProtocolException>().having(
            (DovahLinkProtocolException e) => e.code,
            'code',
            'malformed_message',
          ),
        ),
      );
    });

    test(
      'leaves a pre-existing CONFIRMING credential untouched when the outcome is a failure',
      () async {
        await storage.save(
          const PersistedClientState(
            clientId: 'client-1',
            credential: 'already-confirming-credential',
            recoveryState: PairingRecoveryState.confirming,
          ),
        );
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

  group('acknowledgeTrustedCredential', () {
    test(
      'sets trustState to trusted and clears recovery to none on a trusted outcome',
      () async {
        await storage.save(
          const PersistedClientState(
            clientId: 'client-1',
            credential: 'a1b2c3d4e5f6',
            recoveryState: PairingRecoveryState.confirming,
          ),
        );
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

    test('sets trustState to trusted on an already_trusted outcome', () async {
      await storage.save(
        const PersistedClientState(
          credential: 'a1b2c3d4e5f6',
          recoveryState: PairingRecoveryState.confirming,
        ),
      );
      transport.queueResponse(
        _rawFixture('pairing/pairing-outcome-already-trusted.json'),
      );

      await client.acknowledgeTrustedCredential('a1b2c3d4e5f6');

      expect(client.trustState, DovahLinkTrustState.trusted);
      final PersistedClientState stored = await storage.load();
      expect(stored.recoveryState, PairingRecoveryState.none);
    });

    test(
      'throws DovahLinkPairingException on pending_not_found without changing trustState or storage',
      () async {
        await storage.save(
          const PersistedClientState(
            credential: 'a1b2c3d4e5f6',
            recoveryState: PairingRecoveryState.confirming,
          ),
        );
        transport.queueResponse(
          _rawFixture('pairing/pairing-outcome-pending-not-found.json'),
        );

        await expectLater(
          client.acknowledgeTrustedCredential('a1b2c3d4e5f6'),
          throwsA(
            isA<DovahLinkPairingException>().having(
              (DovahLinkPairingException e) => e.outcome,
              'outcome',
              PairingOutcome.pendingNotFound,
            ),
          ),
        );
        expect(client.trustState, isNull);
        final PersistedClientState stored = await storage.load();
        expect(stored.recoveryState, PairingRecoveryState.confirming);
      },
    );

    test(
      'throws DovahLinkPairingException on a non-pending_not_found failure outcome, also leaving CONFIRMING untouched',
      () async {
        await storage.save(
          const PersistedClientState(
            credential: 'a1b2c3d4e5f6',
            recoveryState: PairingRecoveryState.confirming,
          ),
        );
        transport.queueResponse(
          _rawFixture('pairing/pairing-outcome-expired.json'),
        );

        await expectLater(
          client.acknowledgeTrustedCredential('a1b2c3d4e5f6'),
          throwsA(isA<DovahLinkPairingException>()),
        );

        expect(client.trustState, isNull);
        final PersistedClientState stored = await storage.load();
        expect(stored.recoveryState, PairingRecoveryState.confirming);
      },
    );

    test(
      'persists the previously stored credential, not the method argument, on success',
      () async {
        await storage.save(
          const PersistedClientState(
            credential: 'stored-credential',
            recoveryState: PairingRecoveryState.confirming,
          ),
        );
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

  group('recoverPendingPairing', () {
    test('is a no-op returning unpaired when recovery is none', () async {
      await storage.save(const PersistedClientState(clientId: 'client-1'));

      final DovahLinkTrustState result = await client.recoverPendingPairing();

      expect(result, DovahLinkTrustState.unpaired);
      expect(transport.sent, isEmpty);
    });

    test(
      'is a no-op returning unpaired when CONFIRMING but no credential is stored',
      () async {
        await storage.save(
          const PersistedClientState(
            recoveryState: PairingRecoveryState.confirming,
          ),
        );

        final DovahLinkTrustState result = await client.recoverPendingPairing();

        expect(result, DovahLinkTrustState.unpaired);
        expect(transport.sent, isEmpty);
      },
    );

    test(
      'resumes a CONFIRMING credential to trusted on a trusted outcome',
      () async {
        await storage.save(
          const PersistedClientState(
            clientId: 'client-1',
            credential: 'a1b2c3d4e5f6',
            recoveryState: PairingRecoveryState.confirming,
          ),
        );
        transport.queueResponse(
          _rawFixture('pairing/pairing-outcome-trusted.json'),
        );

        final DovahLinkTrustState result = await client.recoverPendingPairing();

        expect(result, DovahLinkTrustState.trusted);
        final JsonMap sent = jsonDecode(transport.sent.single) as JsonMap;
        expect(sent['messageType'], 'pairing_ack');
        final PersistedClientState stored = await storage.load();
        expect(stored.recoveryState, PairingRecoveryState.none);
      },
    );

    test(
      'discards the credential and returns to unpaired on pending_not_found',
      () async {
        await storage.save(
          const PersistedClientState(
            clientId: 'client-1',
            credential: 'a1b2c3d4e5f6',
            recoveryState: PairingRecoveryState.confirming,
          ),
        );
        transport.queueResponse(
          _rawFixture('pairing/pairing-outcome-pending-not-found.json'),
        );

        final DovahLinkTrustState result = await client.recoverPendingPairing();

        expect(result, DovahLinkTrustState.unpaired);
        final PersistedClientState stored = await storage.load();
        expect(stored.clientId, 'client-1');
        expect(stored.credential, isNull);
        expect(stored.recoveryState, PairingRecoveryState.none);
      },
    );

    test(
      'leaves CONFIRMING untouched when the retry fails for another reason',
      () async {
        await storage.save(
          const PersistedClientState(
            clientId: 'client-1',
            credential: 'a1b2c3d4e5f6',
            recoveryState: PairingRecoveryState.confirming,
          ),
        );
        transport.queueResponse('not valid json');

        await expectLater(
          client.recoverPendingPairing(),
          throwsA(isA<DovahLinkConnectionException>()),
        );

        final PersistedClientState stored = await storage.load();
        expect(stored.credential, 'a1b2c3d4e5f6');
        expect(stored.recoveryState, PairingRecoveryState.confirming);
      },
    );

    test(
      'rethrows and leaves CONFIRMING untouched on a non-pending_not_found pairing failure',
      () async {
        await storage.save(
          const PersistedClientState(
            clientId: 'client-1',
            credential: 'a1b2c3d4e5f6',
            recoveryState: PairingRecoveryState.confirming,
          ),
        );
        transport.queueResponse(
          _rawFixture('pairing/pairing-outcome-expired.json'),
        );

        await expectLater(
          client.recoverPendingPairing(),
          throwsA(
            isA<DovahLinkPairingException>().having(
              (DovahLinkPairingException e) => e.outcome,
              'outcome',
              PairingOutcome.expired,
            ),
          ),
        );

        final PersistedClientState stored = await storage.load();
        expect(stored.credential, 'a1b2c3d4e5f6');
        expect(stored.recoveryState, PairingRecoveryState.confirming);
      },
    );
  });

  group('disconnect', () {
    test('closes the transport and resets session state', () async {
      transport.queueResponse(_rawFixture('connection/hello-ack.json'));
      transport.queueResponse(_rawCapabilities());
      await client.hello();

      await client.disconnect();

      expect(transport.closeCalled, isTrue);
      expect(client.connectionState, DovahLinkConnectionState.disconnected);
      expect(client.trustState, isNull);
      expect(client.sessionId, isNull);
    });

    test('is idempotent: calling it twice does not throw', () async {
      await client.disconnect();

      await expectLater(client.disconnect(), completes);
    });

    test(
      'resets in-memory session state and does not throw even when closing the transport fails',
      () async {
        transport.queueResponse(_rawFixture('connection/hello-ack.json'));
        transport.queueResponse(_rawCapabilities());
        await client.hello();
        transport.failCloseWith = const SocketException('socket already gone');

        await expectLater(client.disconnect(), completes);

        expect(transport.closeCalled, isTrue);
        expect(client.connectionState, DovahLinkConnectionState.disconnected);
        expect(client.trustState, isNull);
        expect(client.sessionId, isNull);
      },
    );

    test('leaves persisted state untouched', () async {
      await storage.save(
        const PersistedClientState(
          clientId: 'client-1',
          credential: 'a1b2c3d4e5f6',
        ),
      );

      await client.disconnect();

      final PersistedClientState stored = await storage.load();
      expect(stored.clientId, 'client-1');
      expect(stored.credential, 'a1b2c3d4e5f6');
    });
  });

  group('forgetCredential', () {
    test(
      'preserves clientId while clearing the credential and recovery state',
      () async {
        await storage.save(
          const PersistedClientState(
            clientId: 'client-1',
            credential: 'a1b2c3d4e5f6',
            recoveryState: PairingRecoveryState.confirming,
          ),
        );

        await client.forgetCredential();

        final PersistedClientState stored = await storage.load();
        expect(stored.clientId, 'client-1');
        expect(stored.credential, isNull);
        expect(stored.recoveryState, PairingRecoveryState.none);
      },
    );

    test('is safe to call with nothing persisted yet', () async {
      await expectLater(client.forgetCredential(), completes);

      final PersistedClientState stored = await storage.load();
      expect(stored.clientId, isNull);
      expect(stored.credential, isNull);
    });

    test('is a no-op when the credential is already clear', () async {
      await storage.save(const PersistedClientState(clientId: 'client-1'));

      await client.forgetCredential();

      final PersistedClientState stored = await storage.load();
      expect(stored.clientId, 'client-1');
      expect(stored.credential, isNull);
      expect(stored.recoveryState, PairingRecoveryState.none);
    });

    test('is idempotent: calling it twice does not throw', () async {
      await storage.save(
        const PersistedClientState(
          clientId: 'client-1',
          credential: 'a1b2c3d4e5f6',
        ),
      );

      await client.forgetCredential();

      await expectLater(client.forgetCredential(), completes);
    });

    test(
      'does not touch the transport or in-memory connection state',
      () async {
        await client.connect(Uri.parse('ws://127.0.0.1:58231/'));
        transport.queueResponse(_rawFixture('connection/hello-ack.json'));
        transport.queueResponse(_rawCapabilities());
        await client.hello();

        await client.forgetCredential();

        expect(transport.closeCalled, isFalse);
        expect(client.connectionState, DovahLinkConnectionState.connected);
      },
    );

    test(
      'a later hello presents unpaired instead of the forgotten credential',
      () async {
        await storage.save(
          const PersistedClientState(
            clientId: 'client-1',
            credential: 'a1b2c3d4e5f6',
          ),
        );
        await client.forgetCredential();
        transport.queueResponse(_rawFixture('connection/hello-ack.json'));
        transport.queueResponse(_rawCapabilities());

        await client.hello();

        final JsonMap sentPayload =
            (jsonDecode(transport.sent.single) as JsonMap)['payload']
                as JsonMap;
        expect(sentPayload['auth'], <String, dynamic>{'method': 'unpaired'});
      },
    );
  });

  group('protocol violations', () {
    test(
      'an unexpected message type throws DovahLinkProtocolException',
      () async {
        transport.queueResponse(
          _rawFixture('pairing/pairing-status-available.json'),
        );

        await expectLater(
          client.hello(),
          throwsA(
            isA<DovahLinkProtocolException>().having(
              (DovahLinkProtocolException e) => e.code,
              'code',
              'unexpected_message_type',
            ),
          ),
        );
      },
    );
  });

  group('inbound message router', () {
    test(
      'two sequential requests each receive their own correctly correlated reply',
      () async {
        transport.queueResponse(_rawFixture('connection/hello-ack.json'));
        transport.queueResponse(_rawCapabilities());
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

    test('an unsolicited message queued ahead of a pending request\'s reply is not consumed as '
        'that reply', () async {
      // capabilities is queued before hello-ack here, unlike every other test above, to prove
      // the router does not treat "whatever arrives first" as the pending operation's reply.
      transport.queueResponse(_rawCapabilities());
      transport.queueResponse(_rawFixture('connection/hello-ack.json'));

      final HelloResult result = await client.hello();

      expect(result.trustState, DovahLinkTrustState.unpaired);
    });

    test(
      'a non-null correlationId matching no pending operation fails closed',
      () async {
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
              'unexpected_correlation_id',
            ),
          ),
        );
        expect(client.connectionState, DovahLinkConnectionState.disconnected);
        expect(transport.closeCalled, isTrue);
      },
    );

    test(
      'malformed JSON received in the background does not escape as an unhandled error',
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

  group('session_invalidated', () {
    const Map<String, AdministrativeInvalidationReason> wireReasons =
        <String, AdministrativeInvalidationReason>{
          'revoked': AdministrativeInvalidationReason.revoked,
          'blocked': AdministrativeInvalidationReason.blocked,
          'trust_reset': AdministrativeInvalidationReason.trustReset,
          'factory_reset': AdministrativeInvalidationReason.factoryReset,
        };
    for (final MapEntry<String, AdministrativeInvalidationReason> entry
        in wireReasons.entries) {
      test('exposes ${entry.value} as the typed invalidationReason', () async {
        await client.connect(Uri.parse('ws://127.0.0.1:58231/'));
        transport.queueResponse(_rawFixture('connection/hello-ack.json'));
        transport.queueResponse(_rawCapabilities());
        await client.hello();

        transport.queueResponse(_rawSessionInvalidated(entry.key));
        await pumpEventQueue();

        expect(
          client.connectionState,
          DovahLinkConnectionState.administrativelyInvalidated,
        );
        expect(client.invalidationReason, entry.value);
      });
    }

    test(
      'fails a pending operation with a connection exception, not unexpected_message_type, '
      'when it arrives while the operation awaits a reply',
      () async {
        await client.connect(Uri.parse('ws://127.0.0.1:58231/'));
        transport.queueResponse(_rawFixture('connection/hello-ack.json'));
        transport.queueResponse(_rawCapabilities());
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
      'received with no authenticated session fails closed as a protocol violation, not '
      'accepted',
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

    test(
      'an unrecognized reason resets to disconnected rather than being accepted',
      () async {
        await client.connect(Uri.parse('ws://127.0.0.1:58231/'));
        transport.queueResponse(_rawFixture('connection/hello-ack.json'));
        transport.queueResponse(_rawCapabilities());
        await client.hello();

        transport.queueResponse(_rawSessionInvalidated('not-a-real-reason'));
        await pumpEventQueue();

        expect(client.connectionState, DovahLinkConnectionState.disconnected);
        expect(client.invalidationReason, isNull);
      },
    );

    test('a transport failure racing immediately behind it does not overwrite the typed reason '
        'with generic transport loss', () async {
      await client.connect(Uri.parse('ws://127.0.0.1:58231/'));
      transport.queueResponse(_rawFixture('connection/hello-ack.json'));
      transport.queueResponse(_rawCapabilities());
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

  group('retry-safe operations across reconnect', () {
    test('orphaned by ordinary transport loss, a retry-safe operation is retransmitted after '
        'reconnect and resolves the original caller', () async {
      await client.connect(Uri.parse('ws://127.0.0.1:58231/'));
      transport.queueResponse(_rawFixture('connection/hello-ack.json'));
      transport.queueResponse(_rawCapabilities());
      await client.hello();

      final Future<PairingChallengeStatus> pending = client.requestPairing();
      await pumpEventQueue();
      transport.failMessagesWith(const SocketException('dropped'));
      await pumpEventQueue();
      // Ordinary transport loss, not administrative invalidation -- the orphaned operation is
      // still alive, not failed.
      expect(client.connectionState, DovahLinkConnectionState.disconnected);

      await client.connect(Uri.parse('ws://127.0.0.1:58231/'));
      transport.queueResponse(_rawFixture('connection/hello-ack.json'));
      transport.queueResponse(_rawCapabilities());
      transport.queueResponse(
        _rawFixture('pairing/pairing-status-available.json'),
      );
      await client.hello();

      final PairingChallengeStatus status = await pending;
      expect(status.availability, PairingAvailability.available);
      // hello#1, pairing_request#1 (orphaned), hello#2, pairing_request#2 (the one retry).
      expect(transport.sent, hasLength(4));
    });

    test('fails without retransmission when the reconnected session no longer satisfies the '
        'required trust state', () async {
      await client.connect(Uri.parse('ws://127.0.0.1:58231/'));
      transport.queueResponse(_rawFixture('connection/hello-ack.json'));
      transport.queueResponse(_rawCapabilities());
      await client.hello();

      final Future<PairingChallengeStatus> pending = client.requestPairing();
      await pumpEventQueue();
      transport.failMessagesWith(const SocketException('dropped'));
      await pumpEventQueue();

      await client.connect(Uri.parse('ws://127.0.0.1:58231/'));
      // The new session comes back already trusted -- pairing_request requires unpaired, so
      // the orphaned request must fail instead of being retried into a session it was never
      // classified for.
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
      transport.queueResponse(_rawCapabilities());
      await client.hello();

      await expectLater(pending, throwsA(isA<DovahLinkConnectionException>()));
      // hello#1, pairing_request#1 (orphaned, already sent before the drop), hello#2 -- no
      // pairing_request#2: the orphaned request was never retransmitted.
      expect(transport.sent, hasLength(3));
    });

    test(
      'a retried operation that fails again is not orphaned a second time',
      () async {
        await client.connect(Uri.parse('ws://127.0.0.1:58231/'));
        transport.queueResponse(_rawFixture('connection/hello-ack.json'));
        transport.queueResponse(_rawCapabilities());
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
        transport.failMessagesWith(const SocketException('dropped'));
        await pumpEventQueue();

        await client.connect(Uri.parse('ws://127.0.0.1:58231/'));
        transport.queueResponse(_rawFixture('connection/hello-ack.json'));
        transport.queueResponse(_rawCapabilities());
        await client.hello();
        // The retry itself now also drops, with no reply ever queued for it.
        await pumpEventQueue();
        transport.failMessagesWith(const SocketException('dropped again'));

        await pendingFails;
        expect(client.connectionState, DovahLinkConnectionState.disconnected);

        // A third connect/hello round must not resurrect it for a second retry.
        await client.connect(Uri.parse('ws://127.0.0.1:58231/'));
        transport.queueResponse(_rawFixture('connection/hello-ack.json'));
        transport.queueResponse(_rawCapabilities());
        await client.hello();

        // hello#1, pairing_request#1, hello#2, pairing_request#2(retry), hello#3 -- no third
        // pairing_request.
        expect(transport.sent, hasLength(5));
      },
    );

    test(
      'a non-retry-safe operation does not survive transport loss -- it fails immediately',
      () async {
        await client.connect(Uri.parse('ws://127.0.0.1:58231/'));
        transport.queueResponse(_rawFixture('connection/hello-ack.json'));
        transport.queueResponse(_rawCapabilities());
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
      },
    );
  });

  group('stale receiver isolation', () {
    test('a transport failure from an already-superseded connection does not alter the new '
        'connection\'s state', () async {
      await client.connect(Uri.parse('ws://127.0.0.1:58231/'));
      transport.queueResponse(_rawFixture('connection/hello-ack.json'));
      transport.queueResponse(_rawCapabilities());
      await client.hello();
      final FakeDovahLinkTransport oldTransport = transport;

      // Ordinary transport loss tears the first connection down and generation-advances past
      // it, then a fresh connect() (on a fresh transport instance, exactly like a real
      // reconnect against a new socket) establishes a new one.
      oldTransport.failMessagesWith(const SocketException('dropped'));
      await pumpEventQueue();
      expect(client.connectionState, DovahLinkConnectionState.disconnected);

      transport = FakeDovahLinkTransport();
      client = DovahLinkClient(transport: transport, storage: storage);
      await client.connect(Uri.parse('ws://127.0.0.1:58231/'));
      transport.queueResponse(_rawFixture('connection/hello-ack.json'));
      transport.queueResponse(_rawCapabilities());
      await client.hello();
      expect(client.connectionState, DovahLinkConnectionState.connected);

      // A late failure from the old, already-superseded transport must not be observable on
      // the new client's state at all -- it isn't even the same client instance's transport
      // any more, exactly like a real reconnect's old socket/stream being fully discarded.
      oldTransport.failMessagesWith(
        const SocketException('late, from the old connection'),
      );
      await pumpEventQueue();

      expect(client.connectionState, DovahLinkConnectionState.connected);
      expect(client.trustState, DovahLinkTrustState.unpaired);
    });

    test('a late reply correlated to an old, already-superseded generation is not consumed by a '
        'new pending operation', () async {
      await client.connect(Uri.parse('ws://127.0.0.1:58231/'));
      transport.queueResponse(_rawFixture('connection/hello-ack.json'));
      transport.queueResponse(_rawCapabilities());
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

      await client.connect(Uri.parse('ws://127.0.0.1:58231/'));
      transport.queueResponse(_rawFixture('connection/hello-ack.json'));
      transport.queueResponse(_rawCapabilities());
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
    });
  });

  group('request policy: timeout, and generalizing beyond one call site', () {
    test('a request against a real, never-connected transport surfaces '
        'DovahLinkConnectionException, not a raw transport error', () async {
      final DovahLinkClient realTransportClient = DovahLinkClient(
        transport: WebSocketTransport(),
        storage: storage,
      );

      await expectLater(
        realTransportClient.hello(),
        throwsA(isA<DovahLinkConnectionException>()),
      );
    });

    test(
      'a request that exceeds its timeout class duration fails and disconnects',
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

    test(
      'acknowledgeTrustedCredential (also retrySafe, unlike requestPairing) survives ordinary '
      'transport loss and is retransmitted after reconnect',
      () async {
        await storage.save(
          const PersistedClientState(
            clientId: 'client-1',
            credential: 'a1b2c3d4e5f6',
            recoveryState: PairingRecoveryState.confirming,
          ),
        );
        await client.connect(Uri.parse('ws://127.0.0.1:58231/'));
        transport.queueResponse(_rawFixture('connection/hello-ack.json'));
        transport.queueResponse(_rawCapabilities());
        await client.hello();

        final Future<void> pending = client.acknowledgeTrustedCredential(
          'a1b2c3d4e5f6',
        );
        final Future<void> pendingCompletes = expectLater(pending, completes);
        await pumpEventQueue();
        transport.failMessagesWith(const SocketException('dropped'));
        await pumpEventQueue();

        await client.connect(Uri.parse('ws://127.0.0.1:58231/'));
        transport.queueResponse(_rawFixture('connection/hello-ack.json'));
        transport.queueResponse(_rawCapabilities());
        transport.queueResponse(
          _rawFixture('pairing/pairing-outcome-trusted.json'),
        );
        await client.hello();

        await pendingCompletes;
        expect(client.trustState, DovahLinkTrustState.trusted);
      },
    );

    test(
      'a protocol violation fails a pending retry-safe operation immediately, not orphaned '
      'for retry',
      () async {
        await client.connect(Uri.parse('ws://127.0.0.1:58231/'));
        transport.queueResponse(_rawFixture('connection/hello-ack.json'));
        transport.queueResponse(_rawCapabilities());
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
        transport.queueResponse(_rawCapabilities());
        await client.hello();
        expect(transport.sent, hasLength(3));
      },
    );

    test(
      'disconnect() also fails an already-orphaned operation, not just a currently pending one',
      () async {
        await client.connect(Uri.parse('ws://127.0.0.1:58231/'));
        transport.queueResponse(_rawFixture('connection/hello-ack.json'));
        transport.queueResponse(_rawCapabilities());
        await client.hello();

        final Future<PairingChallengeStatus> pending = client.requestPairing();
        final Future<void> pendingFails = expectLater(
          pending,
          throwsA(isA<DovahLinkConnectionException>()),
        );
        await pumpEventQueue();
        transport.failMessagesWith(const SocketException('dropped'));
        await pumpEventQueue();
        expect(client.connectionState, DovahLinkConnectionState.disconnected);

        // Deliberate disconnect before any reconnect ever gets a chance to retry it -- must not
        // leave the orphaned operation hanging forever.
        await client.disconnect();

        await pendingFails;
      },
    );
  });
}
