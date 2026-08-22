import 'package:test/test.dart';

import 'package:dovahlink_client_sdk/src/dovahlink_storage_exception.dart';
import 'package:dovahlink_client_sdk/src/persistence/persisted_client_state.dart';
import 'package:dovahlink_client_sdk/src/persistence/persisted_client_state_decoder.dart';
import 'package:dovahlink_client_sdk/src/shared/enums.dart';

/// Runs persisted-client-state decoder behavior tests.
void main() {
  group('Method decode behaves correctly', () {
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
  });
}
