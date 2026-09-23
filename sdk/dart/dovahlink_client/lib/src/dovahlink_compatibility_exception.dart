import 'package:dovahlink_client_sdk/src/shared/enums.dart';

/// Thrown when a valid Host release version falls outside the range this SDK supports.
class DovahLinkCompatibilityException implements Exception {
  /// Creates a compatibility failure with the Host version and its position relative to the
  /// supported range.
  /// @param hostVersion The Host release version that could not be used.
  /// @param supportedHostVersionRange The SDK-declared Host release range.
  /// @param failure Whether the Host is older or newer than the supported range.
  const DovahLinkCompatibilityException({
    required this.hostVersion,
    required this.supportedHostVersionRange,
    required this.failure,
  });

  /// The Host release version that could not be used.
  final String hostVersion;

  /// The SDK-declared Host release range, such as `0.4.x`.
  final String supportedHostVersionRange;

  /// Whether the Host is older or newer than the supported range.
  final HostVersionCompatibilityFailure failure;

  /// Implements [Object.toString].
  @override
  String toString() =>
      'DovahLinkCompatibilityException($failure, hostVersion: $hostVersion, '
      'supportedHostVersionRange: $supportedHostVersionRange)';
}
