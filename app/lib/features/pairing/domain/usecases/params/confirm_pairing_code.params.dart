import 'package:equatable/equatable.dart';
import 'package:meta/meta.dart';

/// Parameters for submitting a pairing code.
@immutable
class ConfirmPairingCodeParams extends Equatable {
  /// The six-digit code the user read from Skyrim.
  final String code;

  /// An optional, presentation-only label for the resulting trusted client.
  final String? displayName;

  /// Creates pairing-code confirmation parameters.
  const ConfirmPairingCodeParams({required this.code, this.displayName});

  /// See [Equatable.props].
  @override
  List<Object?> get props => [code, displayName];
}
