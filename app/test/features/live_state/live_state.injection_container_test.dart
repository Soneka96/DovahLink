import 'package:flutter_test/flutter_test.dart';

import 'package:dovahlink_client/features/live_state/live_state.injection_container.dart';
import 'package:dovahlink_client/features/live_state/presentation/state/live_state.middleware.dart';
import 'package:dovahlink_client/injection_container.dart';

/// Exercises live-state feature dependency registration.
void main() {
  setUp(() async {
    await sl.reset();
  });

  tearDown(() async {
    await sl.reset();
  });

  group('initLiveStateDependencies behaves correctly', () {
    test('initLiveStateDependencies registers one middleware singleton', () {
      initLiveStateDependencies();

      expect(sl<ILiveStateMiddleware>(), isA<LiveStateMiddleware>());
      expect(
        identical(sl<ILiveStateMiddleware>(), sl<ILiveStateMiddleware>()),
        isTrue,
      );
    });
  });
}
