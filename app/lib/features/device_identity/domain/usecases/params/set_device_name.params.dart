import 'package:equatable/equatable.dart';

/// Parameters for setting the companion display-name override.
class SetDeviceNameParams extends Equatable {
  /// The text entered by the user.
  final String displayName;

  /// Creates parameters for a device-name save.
  /// @param displayName The text entered by the user.
  const SetDeviceNameParams({required this.displayName});

  /// See [Equatable.props].
  @override
  List<Object?> get props => [displayName];
}
