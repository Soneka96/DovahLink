import 'package:flutter_test/flutter_test.dart';
import 'package:go_router/go_router.dart';
import 'package:mocktail/mocktail.dart';

import 'package:dovahlink_client/shared/navigation/navigator_service.dart';

/// A mocktail double for [GoRouter], so [NavigatorService] can be tested without a real router.
class MockGoRouter extends Mock implements GoRouter {}

void main() {
  late MockGoRouter router;
  late NavigatorService service;

  setUp(() {
    router = MockGoRouter();
    service = NavigatorService(router);
  });

  group('go', () {
    test('delegates to the router with the exact location', () {
      service.go('/pairing');

      verify(() => router.go('/pairing', extra: null)).called(1);
    });

    test('forwards extra when supplied', () {
      service.go('/pairing', extra: 'payload');

      verify(() => router.go('/pairing', extra: 'payload')).called(1);
    });
  });

  group('push', () {
    test('delegates to the router with the exact location', () async {
      when(
        () => router.push<String>('/pairing', extra: null),
      ).thenAnswer((_) async => null);

      await service.push<String>('/pairing');

      verify(() => router.push<String>('/pairing', extra: null)).called(1);
    });

    test('returns the value the router resolves with', () async {
      when(
        () => router.push<String>('/pairing', extra: null),
      ).thenAnswer((_) async => 'result');

      final String? result = await service.push<String>('/pairing');

      expect(result, 'result');
    });

    test('forwards extra when supplied', () async {
      when(
        () => router.push<String>('/pairing', extra: 'payload'),
      ).thenAnswer((_) async => null);

      await service.push<String>('/pairing', extra: 'payload');

      verify(
        () => router.push<String>('/pairing', extra: 'payload'),
      ).called(1);
    });
  });

  group('replace', () {
    test('delegates to the router with the exact location', () async {
      when(
        () => router.replace<String>('/pairing', extra: null),
      ).thenAnswer((_) async => null);

      await service.replace<String>('/pairing');

      verify(() => router.replace<String>('/pairing', extra: null)).called(1);
    });

    test('returns the value the router resolves with', () async {
      when(
        () => router.replace<String>('/pairing', extra: null),
      ).thenAnswer((_) async => 'result');

      final String? result = await service.replace<String>('/pairing');

      expect(result, 'result');
    });

    test('forwards extra when supplied', () async {
      when(
        () => router.replace<String>('/pairing', extra: 'payload'),
      ).thenAnswer((_) async => null);

      await service.replace<String>('/pairing', extra: 'payload');

      verify(
        () => router.replace<String>('/pairing', extra: 'payload'),
      ).called(1);
    });
  });

  group('pop', () {
    test('delegates with no result by default', () {
      service.pop();

      verify(() => router.pop<Object?>(null)).called(1);
    });

    test('delegates an explicit type with no result', () {
      service.pop<String>();

      verify(() => router.pop<String>(null)).called(1);
    });

    test('delegates with a supplied result', () {
      service.pop<String>('done');

      verify(() => router.pop<String>('done')).called(1);
    });
  });
}
