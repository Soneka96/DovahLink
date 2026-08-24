import 'package:flutter_test/flutter_test.dart';
import 'package:mocktail/mocktail.dart';

import 'package:dovahlink_client/features/pairing/domain/repositories/Ipairing.repository.dart';
import 'package:dovahlink_client/features/pairing/domain/usecases/observe_session_invalidation.usecase.dart';
import 'package:dovahlink_client/shared/failures/failures.dart';
import 'package:dovahlink_client/shared/usecase/no_params.dart';

/// Mocks the pairing repository for [ObserveSessionInvalidationUseCase] tests.
class MockIPairingRepository extends Mock implements IPairingRepository {}

/// Exercises [ObserveSessionInvalidationUseCase]'s stream pass-through.
void main() {
  late MockIPairingRepository mockRepository;
  late ObserveSessionInvalidationUseCase useCase;

  setUp(() {
    mockRepository = MockIPairingRepository();
    useCase = ObserveSessionInvalidationUseCase(mockRepository);
  });

  group('Method call behaves correctly', () {
    test('Method call emits every event the repository stream emits', () async {
      const SessionInvalidatedFailure first = SessionInvalidatedFailure(
        'disconnected by the bridge',
      );
      const SessionInvalidatedFailure second = SessionInvalidatedFailure(
        'disconnected by the bridge again',
      );
      when(() => mockRepository.sessionInvalidated).thenAnswer(
        (_) => Stream<SessionInvalidatedFailure>.fromIterable([first, second]),
      );

      await expectLater(
        useCase(NoParams()),
        emitsInOrder(<Object>[first, second, emitsDone]),
      );
      verify(() => mockRepository.sessionInvalidated).called(1);
      verifyNoMoreInteractions(mockRepository);
    });
  });
}
