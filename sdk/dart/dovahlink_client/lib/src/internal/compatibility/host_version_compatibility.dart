import 'package:dovahlink_client_sdk/src/dovahlink_compatibility_exception.dart';
import 'package:dovahlink_client_sdk/src/dovahlink_protocol_exception.dart';
import 'package:dovahlink_client_sdk/src/shared/enums.dart';

/// The explicit Host release range supported by this SDK version.
const String supportedHostVersionRange = '0.5.x';

/// The oldest Host major version this SDK accepts.
const int _minimumHostMajor = 0;

/// The oldest Host minor version this SDK accepts.
const int _minimumHostMinor = 5;

/// The Host major version accepted by this SDK release.
const int _acceptedHostMajor = 0;

/// The highest Host minor version accepted by this SDK release.
const int _acceptedHostMinor = 5;

/// Matches a complete numeric Host release version without prerelease metadata.
final RegExp _releaseVersionPattern = RegExp(
  r'^(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)$',
);

/// Rejects malformed or unsupported Host release versions before session admission.
/// @param hostVersion The Host's `hello_ack.hostVersion` release string.
void validateHostVersionCompatibility(String hostVersion) {
  final Match? match = _releaseVersionPattern.firstMatch(hostVersion);
  if (match == null) {
    throw const DovahLinkProtocolException(
      code: ProtocolErrorCode.malformedMessage,
      message: 'The host reported an invalid hostVersion release string.',
      retryable: false,
    );
  }

  final int? hostMajor = int.tryParse(match[1]!);
  final int? hostMinor = int.tryParse(match[2]!);
  if (hostMajor == null || hostMinor == null) {
    throw const DovahLinkProtocolException(
      code: ProtocolErrorCode.malformedMessage,
      message: 'The host reported an invalid hostVersion release string.',
      retryable: false,
    );
  }

  if (hostMajor < 1) {
    if (hostMajor == _acceptedHostMajor && hostMinor == _acceptedHostMinor) {
      return;
    }
    throw DovahLinkCompatibilityException(
      hostVersion: hostVersion,
      supportedHostVersionRange: supportedHostVersionRange,
      failure:
          hostMajor < _minimumHostMajor ||
              (hostMajor == _minimumHostMajor && hostMinor < _minimumHostMinor)
          ? HostVersionCompatibilityFailure.hostTooOld
          : HostVersionCompatibilityFailure.hostTooNew,
    );
  }

  if (hostMajor < _minimumHostMajor ||
      (hostMajor == _minimumHostMajor && hostMinor < _minimumHostMinor)) {
    throw DovahLinkCompatibilityException(
      hostVersion: hostVersion,
      supportedHostVersionRange: supportedHostVersionRange,
      failure: HostVersionCompatibilityFailure.hostTooOld,
    );
  }
  if (hostMajor > _acceptedHostMajor ||
      (hostMajor == _acceptedHostMajor && hostMinor > _acceptedHostMinor)) {
    throw DovahLinkCompatibilityException(
      hostVersion: hostVersion,
      supportedHostVersionRange: supportedHostVersionRange,
      failure: HostVersionCompatibilityFailure.hostTooNew,
    );
  }
}
