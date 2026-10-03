import 'package:flutter_test/flutter_test.dart';
import 'package:fpdart/fpdart.dart';
import 'package:mocktail/mocktail.dart';

import 'package:dovahlink_client/features/device_identity/domain/repositories/device_identity_repository.dart';
import 'package:dovahlink_client/features/device_identity/domain/usecases/load_device_name.usecase.dart';
import 'package:dovahlink_client/shared/failures/failures.dart';
import 'package:dovahlink_client/shared/usecase/no_params.dart';

/// Mocks the domain boundary for device-name loading.
class MockDeviceIdentityRepository extends Mock
    implements IDeviceIdentityRepository {}

/// Exercises [LoadDeviceNameUseCase] forwarding.
void main() {
  late MockDeviceIdentityRepository repository;
  late LoadDeviceNameUseCase useCase;

  setUp(() {
    repository = MockDeviceIdentityRepository();
    useCase = LoadDeviceNameUseCase(repository);
  });

  group('Method call behaves correctly', () {
    test('Method call returns the resolved name', () async {
      when(
        () => repository.loadDisplayName(),
      ).thenAnswer((_) async => const Right('TEST-DESKTOP'));

      expect(
        await useCase(NoParams()),
        const Right<Failure, String>('TEST-DESKTOP'),
      );
      verify(() => repository.loadDisplayName()).called(1);
      verifyNoMoreInteractions(repository);
    });

    test('Method call preserves a local-load failure', () async {
      const DatabaseFailure failure = DatabaseFailure('unavailable');
      when(
        () => repository.loadDisplayName(),
      ).thenAnswer((_) async => const Left(failure));

      expect(await useCase(NoParams()), const Left<Failure, String>(failure));
      verify(() => repository.loadDisplayName()).called(1);
      verifyNoMoreInteractions(repository);
    });
  });
}
