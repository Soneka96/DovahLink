import 'dart:convert';
import 'dart:io';

import 'package:test/test.dart';

import 'package:dovahlink_client_sdk/src/protocol/json_map.dart';
import 'package:dovahlink_client_sdk/src/protocol/protocol_format_exception.dart';
import 'package:dovahlink_client_sdk/src/protocol/state_snapshot_payload.dart';

/// Reads one canonical protocol fixture's payload object, relative to `protocol/fixtures/`.
JsonMap _readPayload(String relativePath) {
  final File file = File('../../../protocol/fixtures/$relativePath');
  final JsonMap fixture = jsonDecode(file.readAsStringSync()) as JsonMap;
  return fixture['payload'] as JsonMap;
}

/// Runs [StateSnapshotPayload.fromJson] behavior tests.
void main() {
  group('Method fromJson behaves correctly', () {
    test('Method fromJson matches the canonical state-snapshot fixture', () {
      final StateSnapshotPayload payload = StateSnapshotPayload.fromJson(
        _readPayload('state/state-snapshot.json'),
      );

      expect(payload.stateArea, 'example_area');
      expect(payload.revision, 1);
      expect(payload.occurredAt, '2026-08-11T12:00:00Z');
      expect(payload.data, <String, dynamic>{'value': 12});
    });

    test(
      'Method fromJson composes with timestamp validation for fractional UTC values',
      () {
        final StateSnapshotPayload payload = StateSnapshotPayload.fromJson(
          <String, dynamic>{
            'stateArea': 'example_area',
            'revision': 1,
            'occurredAt': '2026-08-11T12:00:00.1Z',
            'data': <String, dynamic>{'value': 12},
          },
        );

        expect(payload.occurredAt, '2026-08-11T12:00:00.1Z');
      },
    );

    test(
      'Method fromJson matches the canonical state-snapshot-unavailable fixture',
      () {
        final StateSnapshotPayload payload = StateSnapshotPayload.fromJson(
          _readPayload('state/state-snapshot-unavailable.json'),
        );

        expect(payload.data, <String, dynamic>{'value': null});
      },
    );

    test(
      'Method fromJson throws ProtocolFormatException when stateArea is missing',
      () {
        expect(
          () => StateSnapshotPayload.fromJson(<String, dynamic>{
            'revision': 1,
            'occurredAt': '2026-08-11T12:00:00Z',
            'data': <String, dynamic>{},
          }),
          throwsA(isA<ProtocolFormatException>()),
        );
      },
    );

    test(
      'Method fromJson throws ProtocolFormatException when revision is missing',
      () {
        expect(
          () => StateSnapshotPayload.fromJson(<String, dynamic>{
            'stateArea': 'example_area',
            'occurredAt': '2026-08-11T12:00:00Z',
            'data': <String, dynamic>{},
          }),
          throwsA(isA<ProtocolFormatException>()),
        );
      },
    );

    test(
      'Method fromJson throws ProtocolFormatException when occurredAt is missing',
      () {
        expect(
          () => StateSnapshotPayload.fromJson(<String, dynamic>{
            'stateArea': 'example_area',
            'revision': 1,
            'data': <String, dynamic>{},
          }),
          throwsA(isA<ProtocolFormatException>()),
        );
      },
    );

    test(
      'Method fromJson throws ProtocolFormatException when data is missing',
      () {
        expect(
          () => StateSnapshotPayload.fromJson(<String, dynamic>{
            'stateArea': 'example_area',
            'revision': 1,
            'occurredAt': '2026-08-11T12:00:00Z',
          }),
          throwsA(isA<ProtocolFormatException>()),
        );
      },
    );

    test(
      'Method fromJson throws ProtocolFormatException when revision is not an int',
      () {
        expect(
          () => StateSnapshotPayload.fromJson(<String, dynamic>{
            'stateArea': 'example_area',
            'revision': 'one',
            'occurredAt': '2026-08-11T12:00:00Z',
            'data': <String, dynamic>{},
          }),
          throwsA(isA<ProtocolFormatException>()),
        );
      },
    );

    test(
      'Method fromJson throws ProtocolFormatException when stateArea is not a string',
      () {
        expect(
          () => StateSnapshotPayload.fromJson(<String, dynamic>{
            'stateArea': 1,
            'revision': 1,
            'occurredAt': '2026-08-11T12:00:00Z',
            'data': <String, dynamic>{},
          }),
          throwsA(isA<ProtocolFormatException>()),
        );
      },
    );

    test(
      'Method fromJson throws ProtocolFormatException when occurredAt is not a string',
      () {
        expect(
          () => StateSnapshotPayload.fromJson(<String, dynamic>{
            'stateArea': 'example_area',
            'revision': 1,
            'occurredAt': 1,
            'data': <String, dynamic>{},
          }),
          throwsA(isA<ProtocolFormatException>()),
        );
      },
    );

    test(
      'Method fromJson throws ProtocolFormatException when occurredAt is malformed',
      () {
        expect(
          () => StateSnapshotPayload.fromJson(<String, dynamic>{
            'stateArea': 'example_area',
            'revision': 1,
            'occurredAt': 'not-a-timestamp',
            'data': <String, dynamic>{},
          }),
          throwsA(isA<ProtocolFormatException>()),
        );
      },
    );

    test(
      'Method fromJson throws ProtocolFormatException when data is not an object',
      () {
        expect(
          () => StateSnapshotPayload.fromJson(<String, dynamic>{
            'stateArea': 'example_area',
            'revision': 1,
            'occurredAt': '2026-08-11T12:00:00Z',
            'data': 'not an object',
          }),
          throwsA(isA<ProtocolFormatException>()),
        );
      },
    );
  });
}
