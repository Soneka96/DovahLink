import 'package:test/test.dart';

import 'package:dovahlink_client_sdk/src/dovahlink_host.dart';
import 'package:dovahlink_client_sdk/src/dovahlink_storage_exception.dart';
import 'package:dovahlink_client_sdk/src/persistence/persisted_client_state.dart';
import 'package:dovahlink_client_sdk/src/persistence/persisted_client_state_decoder.dart';
import 'package:dovahlink_client_sdk/src/shared/enums.dart';

/// Runs persisted-client-state decoder behavior tests.
void main() {
  group('Method decode behaves correctly', () {
    test('Method decode creates empty v2 state', () {
      final PersistedClientState state =
          PersistedClientStateDecoder.decode(<String, dynamic>{
            'formatVersion': 2,
            'clientId': null,
            'credential': null,
            'recoveryState': 'none',
            'knownHost': null,
          });

      expect(state, const PersistedClientState());
    });

    test(
      'Method decode migrates valid v1 values without a fabricated Host',
      () {
        final PersistedClientState state =
            PersistedClientStateDecoder.decode(<String, dynamic>{
              'formatVersion': 1,
              'clientId': 'client-1',
              'credential': 'legacy-credential',
              'recoveryState': 'confirming',
            });

        expect(state.clientId, 'client-1');
        expect(state.credential, 'legacy-credential');
        expect(state.recoveryState, PairingRecoveryState.confirming);
        expect(state.knownHost, isNull);
      },
    );

    test('Method decode ignores Host-looking fields in legacy v1 data', () {
      final PersistedClientState state = PersistedClientStateDecoder.decode(
        <String, dynamic>{
          'formatVersion': 1,
          'clientId': 'client-1',
          'credential': 'legacy-credential',
          'recoveryState': 'none',
          'knownHost': <String, dynamic>{'hostId': 'not-authenticated'},
        },
      );

      expect(state.knownHost, isNull);
    });

    test('Method decode creates full v2 state with a Known Host', () {
      final PersistedClientState state = PersistedClientStateDecoder.decode(
        <String, dynamic>{
          'formatVersion': 2,
          'clientId': 'client-1',
          'credential': 'credential-1',
          'recoveryState': 'confirming',
          'knownHost': <String, dynamic>{
            'hostId': '81869993-955c-4ba3-a7d0-d35ca86078ea',
            'hostName': 'GONCALO-DESKTOP',
            'endpoint': 'ws://127.0.0.1:58231/',
          },
        },
      );

      expect(
        state.knownHost,
        DovahLinkHost(
          hostId: '81869993-955c-4ba3-a7d0-d35ca86078ea',
          hostName: 'GONCALO-DESKTOP',
          endpoint: Uri.parse('ws://127.0.0.1:58231/'),
        ),
      );
    });

    test('Method decode returns each recognized recovery state', () {
      const Map<String, PairingRecoveryState> states =
          <String, PairingRecoveryState>{
            'none': PairingRecoveryState.none,
            'confirming': PairingRecoveryState.confirming,
          };

      for (final MapEntry<String, PairingRecoveryState> entry
          in states.entries) {
        final PersistedClientState state =
            PersistedClientStateDecoder.decode(<String, dynamic>{
              'formatVersion': PersistedClientState.currentFormatVersion,
              'clientId': 'client-1',
              'credential': 'credential-1',
              'recoveryState': entry.key,
            });

        expect(state.recoveryState, entry.value);
        expect(state.clientId, 'client-1');
        expect(state.credential, 'credential-1');
      }
    });

    test('Method decode rejects unsupported versions and recovery states', () {
      for (final String key in <String>['formatVersion', 'recoveryState']) {
        final Map<String, dynamic> json = <String, dynamic>{
          'formatVersion': PersistedClientState.currentFormatVersion,
          'clientId': null,
          'credential': null,
          'recoveryState': 'none',
        };
        json[key] = key == 'formatVersion' ? 999 : 'future_state';

        expect(
          () => PersistedClientStateDecoder.decode(json),
          throwsA(isA<DovahLinkStorageException>()),
          reason: '$key must be validated',
        );
      }
    });

    test('Method decode rejects non-string identity fields', () {
      for (final String key in <String>['clientId', 'credential']) {
        final Map<String, dynamic> json = <String, dynamic>{
          'formatVersion': PersistedClientState.currentFormatVersion,
          'clientId': null,
          'credential': null,
          'recoveryState': 'none',
        };
        json[key] = 7;

        expect(
          () => PersistedClientStateDecoder.decode(json),
          throwsA(isA<DovahLinkStorageException>()),
          reason: '$key must be a string when present',
        );
      }
    });

    test('Method decode rejects malformed Known Host values', () {
      final Map<String, dynamic> valid = <String, dynamic>{
        'hostId': '81869993-955c-4ba3-a7d0-d35ca86078ea',
        'hostName': 'GONCALO-DESKTOP',
        'endpoint': 'ws://127.0.0.1:58231/',
      };
      final List<Map<String, dynamic>> malformed = <Map<String, dynamic>>[
        <String, dynamic>{...valid, 'hostId': ''},
        <String, dynamic>{...valid, 'hostId': 'not-a-uuid'},
        <String, dynamic>{
          ...valid,
          'hostId': '00000000-0000-0000-0000-000000000000',
        },
        <String, dynamic>{...valid, 'hostId': 42},
        <String, dynamic>{...valid, 'hostName': ''},
        <String, dynamic>{...valid, 'hostName': 'DESKTOP\nPC'},
        <String, dynamic>{
          ...valid,
          'hostName': List<String>.filled(65, 'é').join(),
        },
        <String, dynamic>{...valid, 'hostName': 42},
        <String, dynamic>{...valid, 'endpoint': 'not a URI'},
        <String, dynamic>{...valid, 'endpoint': 'http://127.0.0.1:58231/'},
        <String, dynamic>{...valid, 'endpoint': 'ws:///missing-host'},
        <String, dynamic>{...valid, 'endpoint': 42},
        <String, dynamic>{'hostId': valid['hostId']},
        <String, dynamic>{
          'hostId': valid['hostId'],
          'hostName': valid['hostName'],
        },
        <String, dynamic>{
          'hostId': valid['hostId'],
          'endpoint': valid['endpoint'],
        },
      ];

      for (final Map<String, dynamic> knownHost in malformed) {
        expect(
          () => PersistedClientStateDecoder.decode(<String, dynamic>{
            'formatVersion': 2,
            'clientId': null,
            'credential': null,
            'recoveryState': 'none',
            'knownHost': knownHost,
          }),
          throwsA(isA<DovahLinkStorageException>()),
          reason: 'knownHost must fail closed: $knownHost',
        );
      }
    });

    test('Method decode rejects non-object Known Host values', () {
      for (final Object value in <Object>[
        'host',
        42,
        <Object>['host'],
      ]) {
        expect(
          () => PersistedClientStateDecoder.decode(<String, dynamic>{
            'formatVersion': 2,
            'clientId': null,
            'credential': null,
            'recoveryState': 'none',
            'knownHost': value,
          }),
          throwsA(isA<DovahLinkStorageException>()),
        );
      }
    });
  });
}
