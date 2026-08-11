import 'package:dovahlink_client/features/connection/domain/entities/character_snapshot.entity.dart';
import 'package:dovahlink_client/features/connection/domain/repositories/Iconnection.repository.dart';
import 'package:dovahlink_client/features/connection/domain/usecases/subscribe_to_character.usecase.dart';
import 'package:dovahlink_client/shared/failures/failures.dart';
import 'package:dovahlink_client/shared/usecase/no_params.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:fpdart/fpdart.dart';
import 'package:mocktail/mocktail.dart';

import '../../fixtures/connection.fixture.dart';

class MockIConnectionRepository extends Mock implements IConnectionRepository {}

void main() {
  late MockIConnectionRepository mockRepository;
  late SubscribeToCharacterUseCase useCase;

  setUp(() {
    mockRepository = MockIConnectionRepository();
    useCase = SubscribeToCharacterUseCase(mockRepository);
  });

  group('Usecase SubscribeToCharacterUseCase returns the correct value', () {
    test('returns Right when repository succeeds', () async {
      when(
        mockRepository.subscribeToCharacter,
      ).thenAnswer((_) async => Right(testSnapshot));

      final Either<Failure, CharacterSnapshotEntity> result = await useCase(
        NoParams(),
      );

      expect(result, Right(testSnapshot));
      verify(mockRepository.subscribeToCharacter).called(1);
      verifyNoMoreInteractions(mockRepository);
    });

    test('returns Left when repository fails', () async {
      const NetworkFailure failure = NetworkFailure('failed');
      when(
        mockRepository.subscribeToCharacter,
      ).thenAnswer((_) async => const Left(failure));

      final Either<Failure, CharacterSnapshotEntity> result = await useCase(
        NoParams(),
      );

      expect(result, const Left(failure));
      verify(mockRepository.subscribeToCharacter).called(1);
      verifyNoMoreInteractions(mockRepository);
    });
  });
}
