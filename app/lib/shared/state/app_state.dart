/// The root immutable state held by the DovahLink Redux store.
class AppState {
  /// Creates an empty initial application state.
  const AppState();

  /// Returns the initial state for a new client session.
  factory AppState.initial() => const AppState();
}
