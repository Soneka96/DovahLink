import 'package:redux/redux.dart';

import 'package:dovahlink_client/shared/state/app_reducer.dart';
import 'package:dovahlink_client/shared/state/app_state.dart';

/// Builds the application's Redux store.
class CreateStore {
  /// Creates a store factory with no hidden dependencies.
  const CreateStore();

  /// Returns a new distinct Redux store wired with [middleware], starting from [initialState]
  /// when given or [AppState.initial] otherwise.
  Store<AppState> call({
    List<Middleware<AppState>> middleware = const [],
    AppState? initialState,
  }) {
    return Store<AppState>(
      appReducer,
      initialState: initialState ?? AppState.initial(),
      middleware: middleware,
      distinct: true,
    );
  }
}
