import 'package:dovahlink_client_sdk/src/protocol/host_identity_validator.dart';

/// A stable Host identifier used to request SDK-owned Host operations.
final class DovahLinkHostId {
  /// The validated, lowercase stable Host identifier.
  final String value;

  /// Creates a validated identifier.
  /// @param value The lowercase Host UUID.
  const DovahLinkHostId._(this.value);

  /// Creates an identifier from a valid DovahLink Host UUID.
  /// @param value The Host UUID to validate and normalize.
  /// @throws [ArgumentError] if [value] is not a valid Host UUID.
  factory DovahLinkHostId(String value) {
    if (!isValidHostId(value)) {
      throw ArgumentError.value(value, 'value', 'must be a valid Host UUID');
    }
    return DovahLinkHostId._(value.toLowerCase());
  }

  /// Compares Host identifiers by value.
  @override
  bool operator ==(Object other) =>
      other is DovahLinkHostId &&
      other.value.toLowerCase() == value.toLowerCase();

  /// Combines the normalized Host identifier.
  @override
  int get hashCode => value.toLowerCase().hashCode;

  /// Returns the represented Host identifier.
  @override
  String toString() => value;
}
