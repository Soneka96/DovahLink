import 'package:flutter_test/flutter_test.dart';
import 'package:fpdart/fpdart.dart';
import 'package:mocktail/mocktail.dart';

import 'package:dovahlink_client/features/device_identity/data/datasources/device_identity_local.datasource.dart';
import 'package:dovahlink_client/features/device_identity/data/repositories/device_identity.repository.dart';
import 'package:dovahlink_client/shared/failures/failures.dart';

/// Mocks the local preference boundary for repository tests.
class MockDeviceIdentityLocalDataSource extends Mock
    implements IDeviceIdentityLocalDataSource {}

/// Exercises [DeviceIdentityRepository] delegation.
void main() {
  late MockDeviceIdentityLocalDataSource dataSource;
  late DeviceIdentityRepository repository;

  setUp(() {
    dataSource = MockDeviceIdentityLocalDataSource();
    repository = DeviceIdentityRepository(dataSource);
  });

  group('Method loadDisplayName behaves correctly', () {
    test('Method loadDisplayName returns the resolved name', () async {
      when(
        () => dataSource.loadDisplayName(),
      ).thenAnswer((_) async => const Right('TEST-DESKTOP'));

      expect(await repository.loadDisplayName(), const Right('TEST-DESKTOP'));
      verify(() => dataSource.loadDisplayName()).called(1);
      verifyNoMoreInteractions(dataSource);
    });

    test('Method loadDisplayName preserves a persistence failure', () async {
      const DatabaseFailure failure = DatabaseFailure('unavailable');
      when(
        () => dataSource.loadDisplayName(),
      ).thenAnswer((_) async => const Left(failure));

      expect(await repository.loadDisplayName(), const Left(failure));
      verify(() => dataSource.loadDisplayName()).called(1);
      verifyNoMoreInteractions(dataSource);
    });
  });

  group('Method saveDisplayNameOverride behaves correctly', () {
    test('Method saveDisplayNameOverride forwards the name', () async {
      when(
        () => dataSource.saveDisplayNameOverride('Saved Device'),
      ).thenAnswer((_) async => const Right(unit));

      expect(
        await repository.saveDisplayNameOverride('Saved Device'),
        const Right(unit),
      );
      verify(
        () => dataSource.saveDisplayNameOverride('Saved Device'),
      ).called(1);
      verifyNoMoreInteractions(dataSource);
    });

    test(
      'Method saveDisplayNameOverride preserves a persistence failure',
      () async {
        const DatabaseFailure failure = DatabaseFailure('unavailable');
        when(
          () => dataSource.saveDisplayNameOverride('Saved Device'),
        ).thenAnswer((_) async => const Left(failure));

        expect(
          await repository.saveDisplayNameOverride('Saved Device'),
          const Left(failure),
        );
        verify(
          () => dataSource.saveDisplayNameOverride('Saved Device'),
        ).called(1);
        verifyNoMoreInteractions(dataSource);
      },
    );
  });
}
