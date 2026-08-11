/// Base class for use cases that return a stream of values.
///
/// [T] is the value type emitted by the stream. [Params] is the input
/// parameter object.
abstract class UseCaseStream<T, Params> {
  /// Starts the stream with [params].
  Stream<T> call(Params params);
}
