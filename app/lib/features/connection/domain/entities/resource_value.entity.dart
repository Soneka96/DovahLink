/// A current and maximum value for one character resource pool.
class ResourceValueEntity {
  /// Creates a resource value in game units.
  const ResourceValueEntity({required this.current, required this.maximum});

  /// The currently available amount.
  final double current;

  /// The maximum available amount.
  final double maximum;
}
