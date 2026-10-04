import 'dart:convert';
import 'dart:io';

import 'package:test/test.dart';

import 'package:dovahlink_client_sdk/src/protocol/json_map.dart';
import 'package:dovahlink_client_sdk/src/protocol/protocol_format_exception.dart';
import 'package:dovahlink_client_sdk/src/shared/enums.dart';
import 'package:dovahlink_client_sdk/src/state/player_location_state.dart';

/// Reads one canonical player-location fixture's state-area data object.
/// @param fileName The fixture file name.
/// @return The complete `data` object.
JsonMap _readPlayerLocationFixtureData(String fileName) {
  final String text = File(
    '../../../protocol/fixtures/state/$fileName',
  ).readAsStringSync();
  final JsonMap envelope = jsonDecode(text) as JsonMap;
  final JsonMap payload = envelope['payload'] as JsonMap;
  return payload['data'] as JsonMap;
}

/// Builds a representative available player-location value.
/// @param cellId The runtime cell FormID.
/// @param cellKind The interior or exterior classification.
/// @param cellName The localized cell display name.
/// @param locationId The selected location FormID.
/// @param locationName The localized selected location name.
/// @param worldspaceId The current worldspace FormID.
/// @param worldspaceName The localized worldspace name.
/// @return A fresh state-area value map.
JsonMap _buildPlayerLocationJson({
  Object? cellId = 123456,
  Object? cellKind = 'exterior',
  Object? cellName = 'WhiterunWorld',
  Object? locationId = 98765,
  Object? locationName = 'Whiterun',
  Object? worldspaceId = 1,
  Object? worldspaceName = 'Skyrim',
}) => <String, dynamic>{
  'cellId': cellId,
  'cellKind': cellKind,
  'cellName': cellName,
  'locationId': locationId,
  'locationName': locationName,
  'worldspaceId': worldspaceId,
  'worldspaceName': worldspaceName,
};

/// Tests the complete typed player-location decoder and explicit unavailability.
void main() {
  group('Factory fromJson behaves correctly', () {
    test('Factory fromJson decodes every distinct location fact', () {
      final PlayerLocationState state = PlayerLocationState.fromJson(
        _buildPlayerLocationJson(),
      );

      expect(state.cellId, 123456);
      expect(state.cellKind, PlayerLocationCellKind.exterior);
      expect(state.cellName, 'WhiterunWorld');
      expect(state.locationId, 98765);
      expect(state.locationName, 'Whiterun');
      expect(state.worldspaceId, 1);
      expect(state.worldspaceName, 'Skyrim');
    });

    test(
      'Factory fromJson preserves legitimate missing names and identifiers',
      () {
        final PlayerLocationState state = PlayerLocationState.fromJson(
          _buildPlayerLocationJson(
            cellKind: 'interior',
            cellName: null,
            locationId: null,
            locationName: null,
            worldspaceId: null,
            worldspaceName: null,
          ),
        );

        expect(state.cellId, 123456);
        expect(state.cellKind, PlayerLocationCellKind.interior);
        expect(state.cellName, isNull);
        expect(state.locationId, isNull);
        expect(state.locationName, isNull);
        expect(state.worldspaceId, isNull);
        expect(state.worldspaceName, isNull);
      },
    );

    test('Factory fromJson preserves localized UTF-8 names', () {
      final PlayerLocationState state = PlayerLocationState.fromJson(
        _buildPlayerLocationJson(
          cellName: 'Monastère du lac',
          locationName: 'Crête de l’ours',
          worldspaceName: 'Solitude',
        ),
      );

      expect(state.cellName, 'Monastère du lac');
      expect(state.locationName, 'Crête de l’ours');
      expect(state.worldspaceName, 'Solitude');
    });

    test('Factory fromJson accepts the exact UTF-8 name bound', () {
      final String name = List<String>.filled(52, 'x').join();
      final PlayerLocationState state = PlayerLocationState.fromJson(
        _buildPlayerLocationJson(cellName: name),
      );

      expect(state.cellName, name);
    });

    test(
      'Factory fromJson rejects missing fields, wrong types, and unknown cell kinds',
      () {
        final JsonMap complete = _buildPlayerLocationJson();
        final JsonMap missingCellId = Map<String, dynamic>.of(complete)
          ..remove('cellId');
        final JsonMap wrongName = _buildPlayerLocationJson(cellName: 4);
        final JsonMap unknownCellKind = _buildPlayerLocationJson(
          cellKind: 'underground',
        );
        final JsonMap zeroCellId = _buildPlayerLocationJson(cellId: 0);
        final JsonMap oversizedCellId = _buildPlayerLocationJson(
          cellId: 0x100000000,
        );
        final JsonMap missingLocationId = _buildPlayerLocationJson(
          locationId: null,
          locationName: 'Whiterun',
        );
        final JsonMap missingWorldspaceId = _buildPlayerLocationJson(
          worldspaceId: null,
          worldspaceName: 'Skyrim',
        );
        final JsonMap negativeLocationId = _buildPlayerLocationJson(
          locationId: -1,
        );
        final JsonMap zeroWorldspaceId = _buildPlayerLocationJson(
          worldspaceId: 0,
        );
        final JsonMap oversizedUtf8Name = _buildPlayerLocationJson(
          cellName: List<String>.filled(27, 'é').join(),
        );

        for (final JsonMap malformed in <JsonMap>[
          missingCellId,
          wrongName,
          unknownCellKind,
          zeroCellId,
          oversizedCellId,
          missingLocationId,
          missingWorldspaceId,
          negativeLocationId,
          zeroWorldspaceId,
          oversizedUtf8Name,
        ]) {
          expect(
            () => PlayerLocationState.fromJson(malformed),
            throwsA(isA<ProtocolFormatException>()),
          );
        }
      },
    );
  });

  group('Function decodePlayerLocationState behaves correctly', () {
    test(
      'Function decodePlayerLocationState decodes the available shared fixture',
      () {
        final PlayerLocationState? state = decodePlayerLocationState(
          _readPlayerLocationFixtureData('state-snapshot-player-location.json'),
        );

        expect(state?.locationName, 'Whiterun');
        expect(state?.worldspaceName, 'Skyrim');
        expect(state?.cellKind, PlayerLocationCellKind.exterior);
      },
    );

    test(
      'Function decodePlayerLocationState maps the unavailable fixture to null',
      () {
        expect(
          decodePlayerLocationState(
            _readPlayerLocationFixtureData(
              'state-snapshot-player-location-unavailable.json',
            ),
          ),
          isNull,
        );
      },
    );

    test(
      'Function decodePlayerLocationState rejects malformed state envelopes',
      () {
        for (final JsonMap malformed in <JsonMap>[
          <String, dynamic>{},
          <String, dynamic>{'value': 'Whiterun'},
          <String, dynamic>{
            'value': <String, dynamic>{'cellId': 1},
          },
        ]) {
          expect(
            () => decodePlayerLocationState(malformed),
            throwsA(isA<ProtocolFormatException>()),
          );
        }
      },
    );
  });
}
