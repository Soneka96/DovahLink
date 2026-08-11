import 'package:redux/redux.dart';

import 'package:dovahlink_client/shared/state/app_reducer.dart';
import 'package:dovahlink_client/shared/state/app_state.dart';

/// Builds the application's Redux store.
class CreateStore {
  /// Creates a store factory with no hidden dependencies.
  const CreateStore();

  /// Returns a new distinct Redux store with [AppState.initial].
  Store<AppState> call() {
    return Store<AppState>(
      appReducer,
      initialState: AppState.initial(),
      distinct: true,
    );
  }
}
