import 'resource_value.entity.dart';

/// Read-only character values displayed by the companion client.
abstract class CharacterStateEntity {
  /// Creates a character state contract.
  const CharacterStateEntity();

  /// The player's level, or `null` when unavailable.
  int? get level;

  /// The player's health pool, or `null` when unavailable.
  ResourceValueEntity? get health;

  /// The player's magicka pool, or `null` when unavailable.
  ResourceValueEntity? get magicka;

  /// The player's stamina pool, or `null` when unavailable.
  ResourceValueEntity? get stamina;
}
