import 'dart:convert';
import 'dart:io';

import 'package:test/test.dart';

import 'package:dovahlink_client_sdk/dovahlink_client.dart';
import 'package:dovahlink_client_sdk/src/persistence/in_memory_client_storage.dart';
import 'package:dovahlink_client_sdk/src/protocol/json_map.dart';

/// A controllable [DovahLinkTransport] double: records every sent message and replays queued raw
/// JSON responses in order, one per [messages] access, matching this project's per-file
/// hand-written test-double convention rather than mocking a stream-shaped interface.
class FakeDovahLinkTransport implements DovahLinkTransport {
  /// Every raw text message sent, in order.
  final List<String> sent = <String>[];

  /// Responses queued for successive [messages] accesses.
  final List<String> _queuedResponses = <String>[];

  int _readIndex = 0;

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

  /// Queues one raw JSON response for the next unconsumed [messages] access.
  void queueResponse(String rawJson) => _queuedResponses.add(rawJson);

  @override
  Future<void> connect(Uri uri) async {
    final Object? failure = failConnectWith;
    if (failure != null) {
      throw failure;
    }
    connectedUri = uri;
    connectCalls.add(uri);
  }

  @override
  Future<void> send(String text) async {
    final Object? failure = failSendWith;
    if (failure != null) {
      throw failure;
    }
    sent.add(text);
  }

  @override
  Stream<String> get messages {
    final int index = _readIndex++;
    if (index >= _queuedResponses.length) {
      return Stream<String>.error(StateError('No more queued responses.'));
    }
    return Stream<String>.value(_queuedResponses[index]);
  }

  @override
  Future<void> close() async {
    closeCalled = true;
    final Object? failure = failCloseWith;
    if (failure != null) {
      throw failure;
    }
  }
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

        expect(result.bridgeVersion, '0.3.1');
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

    test(
      'session state is still reset when cleanup fails after the session was already established',
      () async {
        await client.connect(Uri.parse('ws://127.0.0.1:58231/'));
        // hello_ack decodes fine and sets sessionId/trustState, but the capabilities read right
        // after it fails -- proving the reset covers state set moments earlier in this same call,
        // not just the "never got that far" case above.
        transport.queueResponse(_rawFixture('connection/hello-ack.json'));
        transport.queueResponse('not valid json');
        transport.failCloseWith = const SocketException('socket already gone');

        await expectLater(
          client.hello(),
          throwsA(isA<DovahLinkConnectionException>()),
        );

        expect(transport.closeCalled, isTrue);
        expect(client.connectionState, DovahLinkConnectionState.disconnected);
        expect(client.trustState, isNull);
        expect(client.sessionId, isNull);
      },
    );

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
            'correlationId': null,
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
      transport.queueResponse(_rawFixture('errors/error-rate-limited.json'));

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
            'correlationId': null,
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

    test(
      'a transport failure mid-request resets connection state, not just hello\'s',
      () async {
        transport.failSendWith = const SocketException('reset');

        await expectLater(
          client.requestPairing(),
          throwsA(isA<DovahLinkConnectionException>()),
        );

        expect(client.connectionState, DovahLinkConnectionState.disconnected);
        expect(transport.closeCalled, isTrue);
      },
    );
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

    test(
      'an unrecognized outcome throws DovahLinkProtocolException(malformed_message)',
      () async {
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
      },
    );

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

    test(
      'an unrecognized outcome throws DovahLinkProtocolException(malformed_message)',
      () async {
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
      },
    );

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

    const Map<String, String> failureFixtures = <String, String>{
      'expired': 'pairing/pairing-outcome-expired.json',
      'invalid': 'pairing/pairing-outcome-invalid.json',
      'pacing_limited': 'pairing/pairing-outcome-pacing-limited.json',
      'hard_limit_reached': 'pairing/pairing-outcome-hard-limit-reached.json',
    };
    for (final MapEntry<String, String> entry in failureFixtures.entries) {
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
              'pending_not_found',
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
              'expired',
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
}
