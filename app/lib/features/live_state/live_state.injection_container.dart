import 'package:dovahlink_client/features/live_state/presentation/state/live_state.middleware.dart';
import 'package:dovahlink_client/injection_container.dart';

/// Registers app-lifetime live-state middleware.
void initLiveStateDependencies() {
  sl.registerLazySingleton<ILiveStateMiddleware>(LiveStateMiddleware.new);
}
