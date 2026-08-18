import 'package:go_router/go_router.dart';

import 'app_routes.dart';

/// The only approved way to navigate. Middleware calls this, never [GoRouter] or
/// `context.go`/`context.push` directly, and always with an [AppRoutes] constant, never an
/// inline path string.
class NavigatorService {
  /// Creates a navigator service wrapping [router].
  NavigatorService(this._router);

  /// The wrapped router.
  final GoRouter _router;

  /// Replaces the current location with [location].
  void go(String location, {Object? extra}) =>
      _router.go(location, extra: extra);

  /// Pushes [location] onto the navigation stack.
  Future<T?> push<T extends Object?>(String location, {Object? extra}) =>
      _router.push<T>(location, extra: extra);

  /// Replaces the current entry with [location] without changing stack depth.
  Future<T?> replace<T extends Object?>(String location, {Object? extra}) =>
      _router.replace<T>(location, extra: extra);

  /// Pops the current location, optionally returning [result] to the caller that pushed it.
  void pop<T extends Object?>([T? result]) => _router.pop<T>(result);
}
