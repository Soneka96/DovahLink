import 'package:flutter_test/flutter_test.dart';
import 'package:mocktail/mocktail.dart';

import 'package:dovahlink_client/features/pairing/domain/repositories/Ipairing.repository.dart';
import 'package:dovahlink_client/features/pairing/domain/usecases/observe_connection_status.usecase.dart';
import 'package:dovahlink_client/shared/constants/enums.dart';
import 'package:dovahlink_client/shared/usecase/no_params.dart';

/// Mocks the pairing repository for [ObserveConnectionStatusUseCase] tests.
class MockIPairingRepository extends Mock implements IPairingRepository {}

/// Exercises [ObserveConnectionStatusUseCase]'s stream pass-through.
void main() {
  late MockIPairingRepository mockRepository;
  late ObserveConnectionStatusUseCase useCase;

  setUp(() {
    mockRepository = MockIPairingRepository();
    useCase = ObserveConnectionStatusUseCase(mockRepository);
  });

  group('Method call behaves correctly', () {
    test('Method call emits every event the repository stream emits', () async {
      when(() => mockRepository.connectionStatus).thenAnswer(
        (_) => Stream<PairingConnectionStatus>.fromIterable([
          PairingConnectionStatus.lost,
          PairingConnectionStatus.restored,
        ]),
      );

      await expectLater(
        useCase(NoParams()),
        emitsInOrder(<Object>[
          PairingConnectionStatus.lost,
          PairingConnectionStatus.restored,
          emitsDone,
        ]),
      );
      verify(() => mockRepository.connectionStatus).called(1);
      verifyNoMoreInteractions(mockRepository);
    });
  });
}
