import 'package:fpdart/fpdart.dart';

import 'package:dovahlink_client/shared/failures/failures.dart';

/// Domain boundary for loading and saving the companion's display-name override.
abstract interface class IDeviceIdentityRepository {
  /// Returns the resolved saved or platform-fallback display name.
  /// @return The resolved display name, or a persistence [Failure].
  Future<Either<Failure, String>> loadDisplayName();

  /// Persists [displayName] as the user-selected override.
  /// @param displayName The validated non-empty display name to persist.
  /// @return `Right` when persisted, or a persistence [Failure].
  Future<Either<Failure, Unit>> saveDisplayNameOverride(String displayName);
}
