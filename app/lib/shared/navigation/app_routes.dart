/// Every route path in the app lives here, regardless of which feature owns the destination
/// screen. `app_router.dart` is the only place these are turned into `GoRoute`s; no other file
/// writes a route path as an inline string.
abstract final class AppRoutes {
  /// The root/entry screen, currently the read-only connection status display.
  static const String home = '/';

  /// The pairing flow: requesting a code, entering it, and confirming trust.
  static const String pairing = '/pairing';
}
