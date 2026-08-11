import 'package:dovahlink_client/shared/usecase/no_params.dart';

/// Base class for all asynchronous use cases.
///
/// [T] is the return type. [Params] is the input parameter object.
/// Use [NoParams] when the use case requires no input.
abstract class UseCase<T, Params> {
  /// Executes the use case with [params].
  Future<T> call(Params params);
}
