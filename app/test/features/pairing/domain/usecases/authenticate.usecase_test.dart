import 'package:flutter_test/flutter_test.dart';
import 'package:fpdart/fpdart.dart';
import 'package:mocktail/mocktail.dart';

import 'package:dovahlink_client/features/pairing/domain/entities/pairing_handshake.entity.dart';
import 'package:dovahlink_client/features/pairing/domain/repositories/pairing_repository.dart';
import 'package:dovahlink_client/features/pairing/domain/usecases/authenticate.usecase.dart';
import 'package:dovahlink_client/features/pairing/domain/usecases/params/authenticate.params.dart';
import 'package:dovahlink_client/shared/failures/failures.dart';
import '../../../../fixtures/fixtures.dart';

/// Mocks the pairing repository for [AuthenticateUseCase] tests.
class MockIPairingRepository extends Mock implements IPairingRepository {}

/// Exercises [AuthenticateUseCase] success and failure forwarding.
void main() {
  late MockIPairingRepository mockRepository;
  late AuthenticateUseCase useCase;
  final Uri hostUri = Uri.parse('ws://192.168.1.20:4000/');
  late AuthenticateParams params;

  setUp(() {
    mockRepository = MockIPairingRepository();
    useCase = AuthenticateUseCase(mockRepository);
    params = Fixtures.buildAuthenticateParams(hostUri: hostUri);
  });

  group('Usecase AuthenticateUseCase returns the correct value', () {
    test(
      'Usecase AuthenticateUseCase returns Right with the given hostUri when repository succeeds',
      () async {
        final PairingHandshake handshake = Fixtures.buildPairingHandshake();
        when(
          () => mockRepository.authenticate(hostUri: hostUri),
        ).thenAnswer((_) async => Right(handshake));

        final Either<Failure, PairingHandshake> result = await useCase(params);

        expect(result, Right(handshake));
        verify(() => mockRepository.authenticate(hostUri: hostUri)).called(1);
        verifyNoMoreInteractions(mockRepository);
      },
    );

    test(
      'Usecase AuthenticateUseCase returns Left when repository fails',
      () async {
        const NetworkFailure failure = NetworkFailure('failed');
        when(
          () => mockRepository.authenticate(hostUri: hostUri),
        ).thenAnswer((_) async => const Left(failure));

        final Either<Failure, PairingHandshake> result = await useCase(params);

        expect(result, const Left(failure));
        verify(() => mockRepository.authenticate(hostUri: hostUri)).called(1);
        verifyNoMoreInteractions(mockRepository);
      },
    );
  });
}
