import 'dart:async';

/// Serializes asynchronous lifecycle operations while preserving each operation's result/error.
class LifecycleOperationQueue {
  /// The completion of the latest queued operation.
  Future<void> _tail = Future<void>.value();

  /// Runs [operation] after all earlier operations finish.
  /// @return The operation's own result, or its error.
  Future<T> run<T>(Future<T> Function() operation) {
    final Future<void> previous = _tail;
    final Completer<void> completion = Completer<void>();
    _tail = completion.future;
    return () async {
      try {
        await previous;
        return await operation();
      } finally {
        completion.complete();
      }
    }();
  }
}
