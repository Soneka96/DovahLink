import 'package:equatable/equatable.dart';

import 'package:dovahlink_client/features/connection/domain/entities/resource_value.entity.dart';

/// Read-only character values displayed by the companion client.
class CharacterStateEntity extends Equatable {
  /// Creates a character state contract.
  const CharacterStateEntity({
    required this.level,
    required this.health,
    required this.magicka,
    required this.stamina,
  });

  /// The player's level, or `null` when unavailable.
  final int? level;

  /// The player's health pool, or `null` when unavailable.
  final ResourceValueEntity? health;

  /// The player's magicka pool, or `null` when unavailable.
  final ResourceValueEntity? magicka;

  /// The player's stamina pool, or `null` when unavailable.
  final ResourceValueEntity? stamina;

  /// See [Equatable.props].
  @override
  List<Object?> get props => [level, health, magicka, stamina];
}
