import 'dart:async';
import 'dart:convert';
import 'dart:io';

import 'package:flutter_test/flutter_test.dart';

import 'package:dovahlink_client/dovahlink_client.dart';
import 'package:dovahlink_client/src/dovahlink_client/protocol/json_map.dart';

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

  /// Whether [close] was called.
  bool closeCalled = false;

  /// Makes the next [connect] call throw [error] instead of succeeding.
  Object? failConnectWith;

  /// Makes every [send] call throw [error] instead of succeeding.
  Object? failSendWith;

  /// Queues one raw JSON response for the next unconsumed [messages] access.
  void queueResponse(String rawJson) => _queuedResponses.add(rawJson);

  @override
  Future<void> connect(Uri uri) async {
    final Object? failure = failConnectWith;
    if (failure != null) {
      throw failure;
    }
    connectedUri = uri;
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
  }
}

/// Reads one canonical protocol fixture as raw JSON text, relative to `protocol/fixtures/`.
String _rawFixture(String relativePath) =>
    File('../protocol/fixtures/$relativePath').readAsStringSync();

/// Builds a minimal `capabilities` envelope. No canonical fixture exists for this message type yet
/// (out of the pairing epic this shared fixture set was built for); this pre-SDK client only needs
/// to consume and discard it, so a hand-built stand-in is sufficient.
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
  late DovahLinkClient client;

  setUp(() {
    transport = FakeDovahLinkTransport();
    client = DovahLinkClient(transport: transport);
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
          client.hello(clientId: 'client-1'),
          throwsA(isA<DovahLinkConnectionException>()),
        );
      },
    );
  });

  group('hello', () {
    test(
      'an unpaired hello sets sessionId and trustState from the real fixtures',
      () async {
        transport.queueResponse(_rawFixture('connection/hello-ack.json'));
        transport.queueResponse(_rawCapabilities());

        final HelloResult result = await client.hello(clientId: 'client-1');

        expect(result.bridgeVersion, '0.2.0');
        expect(result.trustState, DovahLinkTrustState.unpaired);
        expect(client.trustState, DovahLinkTrustState.unpaired);
        expect(client.sessionId, 'session-1');

        final JsonMap sentPayload =
            (jsonDecode(transport.sent.single) as JsonMap)['payload']
                as JsonMap;
        expect(sentPayload['auth'], <String, dynamic>{'method': 'unpaired'});
      },
    );

    test(
      'a trusted_device_credential hello sends the credential and reports trusted',
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

        final HelloResult result = await client.hello(
          clientId: 'client-1',
          credential: 'a1b2c3d4e5f6',
        );

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
      'a rejected hello throws DovahLinkProtocolException and leaves state unset',
      () async {
        transport.queueResponse(
          _rawFixture('errors/error-unauthenticated-invalid-token.json'),
        );

        await expectLater(
          client.hello(clientId: 'client-1', credential: 'deadbeef'),
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
          client.hello(clientId: 'client-1'),
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
      'a malformed JSON response throws DovahLinkConnectionException',
      () async {
        transport.queueResponse('not valid json');

        await expectLater(
          client.hello(clientId: 'client-1'),
          throwsA(isA<DovahLinkConnectionException>()),
        );
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
    };
    for (final MapEntry<PairingAvailability, String> entry
        in stateFixtures.entries) {
      test('reports ${entry.key} from the real fixture', () async {
        transport.queueResponse(_rawFixture(entry.value));

        final PairingAvailability availability = await client.requestPairing();

        expect(availability, entry.key);
      });
    }

    test(
      'an unrecognized state throws DovahLinkProtocolException(malformed_message)',
      () async {
        transport.queueResponse(
          jsonEncode(<String, dynamic>{
            'messageType': 'pairing_status',
            'messageId': 'message-1',
            'sessionId': null,
            'correlationId': null,
            'payload': <String, dynamic>{'state': 'not-a-real-state'},
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
      await client.hello(clientId: 'client-1');

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
  });

  group('confirmPairingCode', () {
    test('returns the issued credential on credential_issued', () async {
      transport.queueResponse(
        _rawFixture('pairing/pairing-outcome-credential-issued.json'),
      );

      final String credential = await client.confirmPairingCode(
        code: '123456',
        displayName: 'My PC',
      );

      expect(credential, 'a1b2c3d4e5f6');
    });

    const Map<String, String> failureFixtures = <String, String>{
      'expired': 'pairing/pairing-outcome-expired.json',
      'invalid': 'pairing/pairing-outcome-invalid.json',
      'rate_limited': 'pairing/pairing-outcome-rate-limited.json',
    };
    for (final MapEntry<String, String> entry in failureFixtures.entries) {
      test(
        'throws DovahLinkPairingException(${entry.key}) on that outcome',
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
        },
      );
    }
  });

  group('acknowledgeTrustedCredential', () {
    test('sets trustState to trusted on a trusted outcome', () async {
      transport.queueResponse(
        _rawFixture('pairing/pairing-outcome-trusted.json'),
      );

      await client.acknowledgeTrustedCredential('a1b2c3d4e5f6');

      expect(client.trustState, DovahLinkTrustState.trusted);
    });

    test('sets trustState to trusted on an already_trusted outcome', () async {
      transport.queueResponse(
        _rawFixture('pairing/pairing-outcome-already-trusted.json'),
      );

      await client.acknowledgeTrustedCredential('a1b2c3d4e5f6');

      expect(client.trustState, DovahLinkTrustState.trusted);
    });

    test(
      'throws DovahLinkPairingException on pending_not_found without changing trustState',
      () async {
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
      },
    );
  });

  group('disconnect', () {
    test('closes the transport and resets session state', () async {
      transport.queueResponse(_rawFixture('connection/hello-ack.json'));
      transport.queueResponse(_rawCapabilities());
      await client.hello(clientId: 'client-1');

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
  });

  group('protocol violations', () {
    test(
      'an unexpected message type throws DovahLinkProtocolException',
      () async {
        transport.queueResponse(
          _rawFixture('pairing/pairing-status-available.json'),
        );

        await expectLater(
          client.hello(clientId: 'client-1'),
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
