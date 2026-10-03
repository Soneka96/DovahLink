import 'package:flutter_test/flutter_test.dart';
import 'package:fpdart/fpdart.dart';
import 'package:mocktail/mocktail.dart';

import 'package:dovahlink_client/features/device_identity/domain/repositories/device_identity_repository.dart';
import 'package:dovahlink_client/features/device_identity/domain/usecases/params/set_device_name.params.dart';
import 'package:dovahlink_client/features/device_identity/domain/usecases/set_device_name.usecase.dart';
import 'package:dovahlink_client/shared/constants/constants.dart';
import 'package:dovahlink_client/shared/failures/failures.dart';

/// Mocks the local persistence contract for [SetDeviceNameUseCase].
class MockSetDeviceNameRepository extends Mock
    implements IDeviceIdentityRepository {}

/// Exercises normalization, Host constraints, persistence, and failure forwarding.
void main() {
  late MockSetDeviceNameRepository repository;
  late SetDeviceNameUseCase useCase;

  setUp(() {
    repository = MockSetDeviceNameRepository();
    useCase = SetDeviceNameUseCase(repository);
  });

  group('Method call behaves correctly', () {
    test('Method call trims and persists the proposed display name', () async {
      when(
        () => repository.saveDisplayNameOverride('Living Room PC'),
      ).thenAnswer((_) async => const Right(unit));

      expect(
        await useCase(
          const SetDeviceNameParams(displayName: '  Living Room PC  '),
        ),
        const Right<Failure, Unit>(unit),
      );
      verify(
        () => repository.saveDisplayNameOverride('Living Room PC'),
      ).called(1);
      verifyNoMoreInteractions(repository);
    });

    test(
      'Method call stores defaultDeviceName when the input is empty',
      () async {
        when(
          () => repository.saveDisplayNameOverride(defaultDeviceName),
        ).thenAnswer((_) async => const Right(unit));

        expect(
          await useCase(const SetDeviceNameParams(displayName: '  ')),
          const Right<Failure, Unit>(unit),
        );
        verify(
          () => repository.saveDisplayNameOverride(defaultDeviceName),
        ).called(1);
        verifyNoMoreInteractions(repository);
      },
    );

    test(
      'Method call accepts a name exactly at the UTF-8 byte limit',
      () async {
        final String name = List<String>.filled(
          maxDeviceNameLengthBytes ~/ 2,
          'é',
        ).join();
        when(
          () => repository.saveDisplayNameOverride(name),
        ).thenAnswer((_) async => const Right(unit));

        expect(
          await useCase(SetDeviceNameParams(displayName: name)),
          const Right<Failure, Unit>(unit),
        );
        verify(() => repository.saveDisplayNameOverride(name)).called(1);
        verifyNoMoreInteractions(repository);
      },
    );

    test(
      'Method call rejects a name over the UTF-8 byte limit before saving',
      () async {
        final String name = List<String>.filled(
          maxDeviceNameLengthBytes ~/ 2 + 1,
          'é',
        ).join();

        expect(
          await useCase(SetDeviceNameParams(displayName: name)),
          const Left<Failure, Unit>(
            ValidationFailure(
              'Use a device name of 64 UTF-8 bytes or less without control characters.',
            ),
          ),
        );
        verifyNever(() => repository.saveDisplayNameOverride(any()));
      },
    );

    test('Method call rejects a control character before saving', () async {
      expect(
        await useCase(const SetDeviceNameParams(displayName: 'Desk\nTop')),
        const Left<Failure, Unit>(
          ValidationFailure(
            'Use a device name of 64 UTF-8 bytes or less without control characters.',
          ),
        ),
      );
      verifyNever(() => repository.saveDisplayNameOverride(any()));
    });

    test(
      'Method call rejects DEL and C1 control characters before saving',
      () async {
        for (final String control in const ['\u007F', '\u0085']) {
          expect(
            await useCase(
              SetDeviceNameParams(displayName: 'Desk${control}Top'),
            ),
            const Left<Failure, Unit>(
              ValidationFailure(
                'Use a device name of 64 UTF-8 bytes or less without control characters.',
              ),
            ),
          );
        }
        verifyNever(() => repository.saveDisplayNameOverride(any()));
      },
    );

    test('Method call preserves a preference-write failure', () async {
      const DatabaseFailure failure = DatabaseFailure('unavailable');
      when(
        () => repository.saveDisplayNameOverride('Desktop'),
      ).thenAnswer((_) async => const Left(failure));

      expect(
        await useCase(const SetDeviceNameParams(displayName: 'Desktop')),
        const Left<Failure, Unit>(failure),
      );
      verify(() => repository.saveDisplayNameOverride('Desktop')).called(1);
      verifyNoMoreInteractions(repository);
    });
  });
}
