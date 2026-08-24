import 'dart:convert';
import 'dart:io';

import 'package:test/test.dart';

import 'package:dovahlink_client_sdk/src/protocol/json_map.dart';
import 'package:dovahlink_client_sdk/src/protocol/protocol_format_exception.dart';
import 'package:dovahlink_client_sdk/src/protocol/state_event_payload.dart';

/// Reads one canonical protocol fixture's payload object, relative to `protocol/fixtures/`.
JsonMap _readPayload(String relativePath) {
  final File file = File('../../../protocol/fixtures/$relativePath');
  final JsonMap fixture = jsonDecode(file.readAsStringSync()) as JsonMap;
  return fixture['payload'] as JsonMap;
}

/// Runs [StateEventPayload.fromJson] behavior tests.
void main() {
  group('Method fromJson behaves correctly', () {
    test('Method fromJson matches the canonical state-event fixture', () {
      final StateEventPayload payload = StateEventPayload.fromJson(
        _readPayload('state/state-event.json'),
      );

      expect(payload.stateArea, 'example_area');
      expect(payload.baseRevision, 1);
      expect(payload.revision, 2);
      expect(payload.occurredAt, '2026-08-11T12:00:02Z');
      expect(payload.data, <String, dynamic>{'value': 13});
    });

    test('Method fromJson decodes the canonical state-event-duplicate fixture at the same '
        'revision as state-event', () {
      final StateEventPayload payload = StateEventPayload.fromJson(
        _readPayload('state/state-event-duplicate.json'),
      );

      expect(payload.baseRevision, 1);
      expect(payload.revision, 2);
    });

    test('Method fromJson decodes the canonical state-event-revision-gap fixture', () {
      final StateEventPayload payload = StateEventPayload.fromJson(
        _readPayload('state/state-event-revision-gap.json'),
      );

      expect(payload.baseRevision, 5);
      expect(payload.revision, 6);
    });

    test('Method fromJson decodes the canonical state-event-stale fixture', () {
      final StateEventPayload payload = StateEventPayload.fromJson(
        _readPayload('state/state-event-stale.json'),
      );

      expect(payload.baseRevision, 0);
      expect(payload.revision, 1);
    });

    test('Method fromJson throws ProtocolFormatException when baseRevision is missing', () {
      expect(
        () => StateEventPayload.fromJson(<String, dynamic>{
          'stateArea': 'example_area',
          'revision': 2,
          'occurredAt': '2026-08-11T12:00:00Z',
          'data': <String, dynamic>{},
        }),
        throwsA(isA<ProtocolFormatException>()),
      );
    });

    test('Method fromJson throws ProtocolFormatException when stateArea is missing', () {
      expect(
        () => StateEventPayload.fromJson(<String, dynamic>{
          'baseRevision': 1,
          'revision': 2,
          'occurredAt': '2026-08-11T12:00:00Z',
          'data': <String, dynamic>{},
        }),
        throwsA(isA<ProtocolFormatException>()),
      );
    });

    test('Method fromJson throws ProtocolFormatException when data is missing', () {
      expect(
        () => StateEventPayload.fromJson(<String, dynamic>{
          'stateArea': 'example_area',
          'baseRevision': 1,
          'revision': 2,
          'occurredAt': '2026-08-11T12:00:00Z',
        }),
        throwsA(isA<ProtocolFormatException>()),
      );
    });

    test('Method fromJson throws ProtocolFormatException when baseRevision is not an int', () {
      expect(
        () => StateEventPayload.fromJson(<String, dynamic>{
          'stateArea': 'example_area',
          'baseRevision': 'one',
          'revision': 2,
          'occurredAt': '2026-08-11T12:00:00Z',
          'data': <String, dynamic>{},
        }),
        throwsA(isA<ProtocolFormatException>()),
      );
    });

    test('Method fromJson throws ProtocolFormatException when revision is missing', () {
      expect(
        () => StateEventPayload.fromJson(<String, dynamic>{
          'stateArea': 'example_area',
          'baseRevision': 1,
          'occurredAt': '2026-08-11T12:00:00Z',
          'data': <String, dynamic>{},
        }),
        throwsA(isA<ProtocolFormatException>()),
      );
    });

    test('Method fromJson throws ProtocolFormatException when occurredAt is missing', () {
      expect(
        () => StateEventPayload.fromJson(<String, dynamic>{
          'stateArea': 'example_area',
          'baseRevision': 1,
          'revision': 2,
          'data': <String, dynamic>{},
        }),
        throwsA(isA<ProtocolFormatException>()),
      );
    });

    test('Method fromJson throws ProtocolFormatException when stateArea is not a string', () {
      expect(
        () => StateEventPayload.fromJson(<String, dynamic>{
          'stateArea': 1,
          'baseRevision': 1,
          'revision': 2,
          'occurredAt': '2026-08-11T12:00:00Z',
          'data': <String, dynamic>{},
        }),
        throwsA(isA<ProtocolFormatException>()),
      );
    });

    test('Method fromJson throws ProtocolFormatException when revision is not an int', () {
      expect(
        () => StateEventPayload.fromJson(<String, dynamic>{
          'stateArea': 'example_area',
          'baseRevision': 1,
          'revision': 'two',
          'occurredAt': '2026-08-11T12:00:00Z',
          'data': <String, dynamic>{},
        }),
        throwsA(isA<ProtocolFormatException>()),
      );
    });

    test('Method fromJson throws ProtocolFormatException when occurredAt is not a string', () {
      expect(
        () => StateEventPayload.fromJson(<String, dynamic>{
          'stateArea': 'example_area',
          'baseRevision': 1,
          'revision': 2,
          'occurredAt': 1,
          'data': <String, dynamic>{},
        }),
        throwsA(isA<ProtocolFormatException>()),
      );
    });

    test('Method fromJson throws ProtocolFormatException when data is not an object', () {
      expect(
        () => StateEventPayload.fromJson(<String, dynamic>{
          'stateArea': 'example_area',
          'baseRevision': 1,
          'revision': 2,
          'occurredAt': '2026-08-11T12:00:00Z',
          'data': 'not an object',
        }),
        throwsA(isA<ProtocolFormatException>()),
      );
    });
  });
}
