import 'package:test/test.dart';

import 'package:dovahlink_client_sdk/src/protocol/capability.dart';
import 'package:dovahlink_client_sdk/src/protocol/protocol_format_exception.dart';

/// Runs [Capability.fromJson] and [Capability.toJson] behavior tests.
void main() {
  group('Method fromJson behaves correctly', () {
    test('Method fromJson decodes id and version', () {
      final Capability capability = Capability.fromJson(<String, dynamic>{
        'id': 'state.inventory',
        'version': 1,
      });

      expect(capability.id, 'state.inventory');
      expect(capability.version, 1);
    });

    test('Method fromJson throws ProtocolFormatException when id is missing', () {
      expect(
        () => Capability.fromJson(<String, dynamic>{'version': 1}),
        throwsA(isA<ProtocolFormatException>()),
      );
    });

    test('Method fromJson throws ProtocolFormatException when version is missing', () {
      expect(
        () => Capability.fromJson(<String, dynamic>{'id': 'state.inventory'}),
        throwsA(isA<ProtocolFormatException>()),
      );
    });

    test('Method fromJson throws ProtocolFormatException when id is not a string', () {
      expect(
        () => Capability.fromJson(<String, dynamic>{'id': 1, 'version': 1}),
        throwsA(isA<ProtocolFormatException>()),
      );
    });

    test('Method fromJson throws ProtocolFormatException when version is not an int', () {
      expect(
        () => Capability.fromJson(<String, dynamic>{'id': 'state.inventory', 'version': 'one'}),
        throwsA(isA<ProtocolFormatException>()),
      );
    });

    test('Method fromJson round-trips through toJson', () {
      final Capability capability = Capability.fromJson(<String, dynamic>{
        'id': 'state.inventory',
        'version': 1,
      });

      expect(Capability.fromJson(capability.toJson()).toJson(), capability.toJson());
    });
  });

  group('Method toJson behaves correctly', () {
    test('Method toJson encodes id and version', () {
      const Capability capability = Capability(id: 'state.inventory', version: 1);

      expect(capability.toJson(), <String, dynamic>{'id': 'state.inventory', 'version': 1});
    });
  });
}
