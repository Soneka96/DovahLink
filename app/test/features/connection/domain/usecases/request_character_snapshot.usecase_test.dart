import 'package:flutter_test/flutter_test.dart';
import 'package:fpdart/fpdart.dart';
import 'package:mocktail/mocktail.dart';

import 'package:dovahlink_client/features/connection/domain/entities/character_snapshot.entity.dart';
import 'package:dovahlink_client/features/connection/domain/repositories/Iconnection.repository.dart';
import 'package:dovahlink_client/features/connection/domain/usecases/params/request_character_snapshot.params.dart';
import 'package:dovahlink_client/features/connection/domain/usecases/request_character_snapshot.usecase.dart';
import 'package:dovahlink_client/shared/failures/failures.dart';

import '../../fixtures/connection.fixture.dart';

/// Mocks the connection repository for [RequestCharacterSnapshotUseCase] tests.
class MockIConnectionRepository extends Mock implements IConnectionRepository {}

/// Exercises [RequestCharacterSnapshotUseCase] forwarding behavior.
void main() {
  late MockIConnectionRepository mockRepository;
  late RequestCharacterSnapshotUseCase useCase;

  setUp(() {
    mockRepository = MockIConnectionRepository();
    useCase = RequestCharacterSnapshotUseCase(mockRepository);
  });

  group(
    'Usecase RequestCharacterSnapshotUseCase returns the correct value',
    () {
      test('returns Right and forwards knownRevision when it is set', () async {
        when(
          () => mockRepository.requestCharacterSnapshot(knownRevision: 3),
        ).thenAnswer((_) async => Right(buildCharacterSnapshotEntity()));

        final Either<Failure, CharacterSnapshotEntity> result = await useCase(
          const RequestCharacterSnapshotParams(knownRevision: 3),
        );

        expect(result, Right(buildCharacterSnapshotEntity()));
        verify(
          () => mockRepository.requestCharacterSnapshot(knownRevision: 3),
        ).called(1);
        verifyNoMoreInteractions(mockRepository);
      });

      test(
        'returns Right and forwards null knownRevision when it is absent',
        () async {
          when(
            () => mockRepository.requestCharacterSnapshot(),
          ).thenAnswer((_) async => Right(buildCharacterSnapshotEntity()));

          final Either<Failure, CharacterSnapshotEntity> result = await useCase(
            const RequestCharacterSnapshotParams(),
          );

          expect(result, Right(buildCharacterSnapshotEntity()));
          verify(() => mockRepository.requestCharacterSnapshot()).called(1);
          verifyNoMoreInteractions(mockRepository);
        },
      );

      test('returns Left when repository fails', () async {
        const NetworkFailure failure = NetworkFailure('failed');
        when(
          () => mockRepository.requestCharacterSnapshot(knownRevision: 3),
        ).thenAnswer((_) async => const Left(failure));

        final Either<Failure, CharacterSnapshotEntity> result = await useCase(
          const RequestCharacterSnapshotParams(knownRevision: 3),
        );

        expect(result, const Left(failure));
        verify(
          () => mockRepository.requestCharacterSnapshot(knownRevision: 3),
        ).called(1);
        verifyNoMoreInteractions(mockRepository);
      });
    },
  );
}
