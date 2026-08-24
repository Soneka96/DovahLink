import 'dart:convert';
import 'dart:io';

import 'package:test/test.dart';

import 'package:dovahlink_client_sdk/src/protocol/json_map.dart';
import 'package:dovahlink_client_sdk/src/protocol/protocol_format_exception.dart';
import 'package:dovahlink_client_sdk/src/protocol/rename_outcome_payload.dart';
import 'package:dovahlink_client_sdk/src/shared/enums.dart';

/// Reads one canonical protocol fixture's payload object, relative to `protocol/fixtures/`.
JsonMap _readPayload(String relativePath) {
  final File file = File('../../../protocol/fixtures/$relativePath');
  final JsonMap fixture = jsonDecode(file.readAsStringSync()) as JsonMap;
  return fixture['payload'] as JsonMap;
}

/// Runs [RenameOutcomePayload.fromJson] behavior tests.
void main() {
  group('Method fromJson behaves correctly', () {
    test('Method fromJson matches the canonical rename-outcome-renamed fixture', () {
      final RenameOutcomePayload payload = RenameOutcomePayload.fromJson(
        _readPayload('rename/rename-outcome-renamed.json'),
      );

      expect(payload.outcome, RenameOutcome.renamed);
      expect(payload.displayName, 'New Name');
    });

    test('Method fromJson matches the canonical rename-outcome-invalid-display-name fixture', () {
      final RenameOutcomePayload payload = RenameOutcomePayload.fromJson(
        _readPayload('rename/rename-outcome-invalid-display-name.json'),
      );

      expect(payload.outcome, RenameOutcome.invalidDisplayName);
      expect(payload.displayName, isNull);
    });

    test('Method fromJson matches the canonical rename-outcome-not-trusted fixture', () {
      final RenameOutcomePayload payload = RenameOutcomePayload.fromJson(
        _readPayload('rename/rename-outcome-not-trusted.json'),
      );

      expect(payload.outcome, RenameOutcome.notTrusted);
      expect(payload.displayName, isNull);
    });

    test('Method fromJson throws ProtocolFormatException when outcome is unrecognized', () {
      expect(
        () => RenameOutcomePayload.fromJson(<String, dynamic>{
          'outcome': 'not_a_real_outcome',
          'displayName': null,
        }),
        throwsA(isA<ProtocolFormatException>()),
      );
    });

    test('Method fromJson throws ProtocolFormatException when outcome is missing', () {
      expect(
        () => RenameOutcomePayload.fromJson(<String, dynamic>{'displayName': null}),
        throwsA(isA<ProtocolFormatException>()),
      );
    });

    test('Method fromJson throws ProtocolFormatException when displayName key is absent', () {
      expect(
        () => RenameOutcomePayload.fromJson(<String, dynamic>{'outcome': 'not_trusted'}),
        throwsA(isA<ProtocolFormatException>()),
      );
    });

    test('Method fromJson throws ProtocolFormatException when outcome is not a string', () {
      expect(
        () => RenameOutcomePayload.fromJson(<String, dynamic>{'outcome': 1, 'displayName': null}),
        throwsA(isA<ProtocolFormatException>()),
      );
    });

    test('Method fromJson throws ProtocolFormatException when displayName is not a string', () {
      expect(
        () => RenameOutcomePayload.fromJson(<String, dynamic>{
          'outcome': 'not_trusted',
          'displayName': 1,
        }),
        throwsA(isA<ProtocolFormatException>()),
      );
    });
  });
}
