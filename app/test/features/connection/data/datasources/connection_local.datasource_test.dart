import 'package:flutter_test/flutter_test.dart';
import 'package:shared_preferences/shared_preferences.dart';

import 'package:dovahlink_client/features/connection/data/datasources/connection_local.datasource.dart';

/// Matches an RFC 4122 version-4 UUID string.
final RegExp _uuidV4Pattern = RegExp(
  r'^[0-9a-f]{8}-[0-9a-f]{4}-4[0-9a-f]{3}-[89ab][0-9a-f]{3}-[0-9a-f]{12}$',
);

/// Exercises [ConnectionLocalDataSourceImpl.resolveClientId].
void main() {
  group('ConnectionLocalDataSourceImpl.resolveClientId', () {
    test(
      'generates and persists a fresh client ID when none exists yet',
      () async {
        SharedPreferences.setMockInitialValues(<String, Object>{});
        final SharedPreferences preferences =
            await SharedPreferences.getInstance();
        final ConnectionLocalDataSourceImpl dataSource =
            ConnectionLocalDataSourceImpl(preferences);

        final String clientId = await dataSource.resolveClientId();

        expect(clientId, isA<String>());
        expect(_uuidV4Pattern.hasMatch(clientId), isTrue);
        expect(
          preferences.getString(
            ConnectionLocalDataSourceImpl.preferencesKey,
          ),
          clientId,
        );
      },
    );

    test(
      'generates and persists a fresh client ID when the persisted value is an empty string',
      () async {
        SharedPreferences.setMockInitialValues(<String, Object>{
          ConnectionLocalDataSourceImpl.preferencesKey: '',
        });
        final SharedPreferences preferences =
            await SharedPreferences.getInstance();
        final ConnectionLocalDataSourceImpl dataSource =
            ConnectionLocalDataSourceImpl(preferences);

        final String clientId = await dataSource.resolveClientId();

        expect(clientId, isA<String>());
        expect(_uuidV4Pattern.hasMatch(clientId), isTrue);
        expect(
          preferences.getString(
            ConnectionLocalDataSourceImpl.preferencesKey,
          ),
          clientId,
        );
      },
    );

    test(
      'returns the already-persisted client ID without generating a new one',
      () async {
        SharedPreferences.setMockInitialValues(<String, Object>{
          ConnectionLocalDataSourceImpl.preferencesKey:
              'existing-client-id',
        });
        final SharedPreferences preferences =
            await SharedPreferences.getInstance();
        final ConnectionLocalDataSourceImpl dataSource =
            ConnectionLocalDataSourceImpl(preferences);

        final String clientId = await dataSource.resolveClientId();

        expect(clientId, isA<String>());
        expect(clientId, 'existing-client-id');
      },
    );

    test('returns the same client ID across repeated calls', () async {
      SharedPreferences.setMockInitialValues(<String, Object>{});
      final SharedPreferences preferences =
          await SharedPreferences.getInstance();
      final ConnectionLocalDataSourceImpl dataSource =
          ConnectionLocalDataSourceImpl(preferences);

      final String first = await dataSource.resolveClientId();
      final String second = await dataSource.resolveClientId();

      expect(second, isA<String>());
      expect(second, first);
    });

    test(
      'regenerates a different client ID after the persisted value is cleared',
      () async {
        SharedPreferences.setMockInitialValues(<String, Object>{});
        final SharedPreferences preferences =
            await SharedPreferences.getInstance();
        final ConnectionLocalDataSourceImpl dataSource =
            ConnectionLocalDataSourceImpl(preferences);
        final String beforeClear = await dataSource.resolveClientId();

        await preferences.clear();
        final String afterClear = await dataSource.resolveClientId();

        expect(afterClear, isA<String>());
        expect(_uuidV4Pattern.hasMatch(afterClear), isTrue);
        expect(afterClear == beforeClear, isFalse);
      },
    );
  });
}
