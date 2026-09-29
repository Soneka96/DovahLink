import 'package:test/test.dart';

import 'package:dovahlink_client_sdk/src/dovahlink_storage_exception.dart';
import 'package:dovahlink_client_sdk/src/persistence/persisted_client_state.dart';
import 'package:dovahlink_client_sdk/src/persistence/persisted_client_state_decoder.dart';
import 'package:dovahlink_client_sdk/src/shared/enums.dart';

/// Runs persisted-client-state decoder behavior tests.
void main() {
  const String hostA = '81869993-955c-4ba3-a7d0-d35ca86078ea';
  const String hostB = '81f6cc90-3a88-40c7-8351-104d4a36c971';

  /// Builds a serialized relationship fixture for [id].
  /// @param id The Host UUID represented by the record.
  /// @param credential The optional Host-scoped credential.
  /// @return A version-3 Known Host JSON object.
  Map<String, dynamic> host({String id = hostA, String? credential}) =>
      <String, dynamic>{
        'hostName': id,
        'endpoint': 'ws://127.0.0.1:58231/',
        'credential': credential,
      };

  /// Builds a version-3 root state object with scenario overrides.
  /// @param knownHosts The serialized Host collection, or an empty object by default.
  /// @param recovery The pending recovery record, or `null` when none is stored.
  /// @return A valid root object before scenario-specific validation.
  Map<String, dynamic> currentJson({
    Object? knownHosts = const <String, dynamic>{},
    Object? recovery,
  }) => <String, dynamic>{
    'formatVersion': PersistedClientState.currentFormatVersion,
    'clientId': 'client-1',
    'knownHosts': knownHosts,
    'pendingPairingRecovery': recovery,
  };

  group('Method decode behaves correctly', () {
    test('Method decode creates the empty v3 state', () {
      expect(
        PersistedClientStateDecoder.decode(currentJson()),
        PersistedClientState(clientId: 'client-1'),
      );
    });

    test('Method decode invalidates unreleased v1 singleton credentials', () {
      final PersistedClientState state =
          PersistedClientStateDecoder.decode(<String, dynamic>{
            'formatVersion': 1,
            'clientId': 'client-1',
            'credential': 'legacy-credential',
            'recoveryState': 'confirming',
          });

      expect(state.clientId, 'client-1');
      expect(state.knownHosts, isEmpty);
      expect(state.pendingPairingRecovery, isNull);
    });

    test('Method decode invalidates unreleased v2 singleton credentials', () {
      final PersistedClientState state = PersistedClientStateDecoder.decode(
        <String, dynamic>{
          'formatVersion': 2,
          'clientId': 'client-1',
          'credential': 'legacy-credential',
          'recoveryState': 'confirming',
          'knownHost': <String, dynamic>{
            'hostId': hostA,
            'hostName': 'OLD-HOST',
            'endpoint': 'ws://127.0.0.1:58231/',
          },
        },
      );

      expect(state.clientId, 'client-1');
      expect(state.knownHosts, isEmpty);
      expect(state.pendingPairingRecovery, isNull);
    });

    test(
      'Method decode keeps independent Host credentials and owned recovery',
      () {
        final PersistedClientState state = PersistedClientStateDecoder.decode(
          currentJson(
            knownHosts: <String, dynamic>{
              hostA: host(id: hostA, credential: 'credential-a'),
              hostB: host(id: hostB, credential: 'credential-b'),
            },
            recovery: <String, dynamic>{'hostId': hostB, 'state': 'confirming'},
          ),
        );

        expect(state.knownHosts[hostA]?.credential, 'credential-a');
        expect(state.knownHosts[hostB]?.credential, 'credential-b');
        expect(state.pendingPairingRecovery?.hostId, hostB);
        expect(
          state.pendingPairingRecovery?.state,
          PairingRecoveryState.confirming,
        );
      },
    );

    test('Method decode canonicalizes uppercase Host IDs in stored state', () {
      final PersistedClientState state = PersistedClientStateDecoder.decode(
        currentJson(
          knownHosts: <String, dynamic>{
            hostA.toUpperCase(): host(
              id: hostA.toUpperCase(),
              credential: 'credential-a',
            ),
          },
          recovery: <String, dynamic>{
            'hostId': hostA.toUpperCase(),
            'state': 'confirming',
          },
        ),
      );

      expect(state.knownHosts.keys, <String>[hostA]);
      expect(state.knownHosts[hostA]?.host.hostId, hostA);
      expect(state.pendingPairingRecovery?.hostId, hostA);
    });

    test('Method decode rejects an unknown format version', () {
      expect(
        () => PersistedClientStateDecoder.decode(<String, dynamic>{
          ...currentJson(),
          'formatVersion': 999,
        }),
        throwsA(isA<DovahLinkStorageException>()),
      );
    });

    test('Method decode rejects malformed clientId values', () {
      expect(
        () => PersistedClientStateDecoder.decode(<String, dynamic>{
          ...currentJson(),
          'clientId': 7,
        }),
        throwsA(isA<DovahLinkStorageException>()),
      );
    });

    test(
      'Method decode rejects malformed Known Hosts containers and duplicate IDs',
      () {
        for (final Object? value in <Object?>[
          null,
          'hosts',
          7,
          <Object>['host'],
        ]) {
          expect(
            () => PersistedClientStateDecoder.decode(
              currentJson(knownHosts: value),
            ),
            throwsA(isA<DovahLinkStorageException>()),
            reason: '$value is not a valid Known Hosts object',
          );
        }

        expect(
          () => PersistedClientStateDecoder.decode(
            currentJson(
              knownHosts: <String, dynamic>{
                hostA: host(),
                hostA.toUpperCase(): host(id: hostA.toUpperCase()),
              },
            ),
          ),
          throwsA(isA<DovahLinkStorageException>()),
        );
      },
    );

    test('Method decode rejects malformed Known Host records', () {
      final List<Map<String, dynamic>> malformed = <Map<String, dynamic>>[
        <String, dynamic>{...host(), 'hostName': ''},
        <String, dynamic>{...host(), 'hostName': 'DESKTOP\nPC'},
        <String, dynamic>{...host(), 'endpoint': 'http://127.0.0.1:58231/'},
        <String, dynamic>{...host(), 'endpoint': 'not-a-uri'},
        <String, dynamic>{
          ...host(),
          'endpoint': 'ws://user:secret@127.0.0.1:58231/',
        },
        <String, dynamic>{
          ...host(),
          'endpoint': 'ws://127.0.0.1:58231/#fragment',
        },
        <String, dynamic>{...host(), 'endpoint': 'ws:///missing-host'},
        <String, dynamic>{...host(), 'endpoint': 'ws://127.0.0.1:0/'},
        <String, dynamic>{...host(), 'endpoint': 'ws://127.0.0.1:65536/'},
        <String, dynamic>{...host(), 'credential': 42},
      ];

      for (final Map<String, dynamic> record in malformed) {
        expect(
          () => PersistedClientStateDecoder.decode(
            currentJson(knownHosts: <String, dynamic>{hostA: record}),
          ),
          throwsA(isA<DovahLinkStorageException>()),
          reason: '$record must fail closed',
        );
      }
    });

    test(
      'Method decode rejects invalid Host map keys and non-object records',
      () {
        for (final Object value in <Object>[
          <String, dynamic>{'not-a-uuid': host(id: 'not-a-uuid')},
          <String, dynamic>{hostA: 'not an object'},
        ]) {
          expect(
            () => PersistedClientStateDecoder.decode(
              currentJson(knownHosts: value),
            ),
            throwsA(isA<DovahLinkStorageException>()),
          );
        }
      },
    );

    test('Method decode rejects malformed or ownerless recovery records', () {
      for (final Object recovery in <Object>[
        'confirming',
        <String, dynamic>{'hostId': 'not-a-uuid', 'state': 'confirming'},
        <String, dynamic>{'hostId': hostB, 'state': 'confirming'},
        <String, dynamic>{'hostId': hostA, 'state': 'future'},
      ]) {
        expect(
          () => PersistedClientStateDecoder.decode(
            currentJson(
              knownHosts: <String, dynamic>{hostA: host()},
              recovery: recovery,
            ),
          ),
          throwsA(isA<DovahLinkStorageException>()),
        );
      }
    });
  });
}
