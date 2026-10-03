import 'package:equatable/equatable.dart';

/// Records a device-name override after local persistence succeeds.
class DeviceNameSavedAction extends Equatable {
  /// The persisted, trimmed display name.
  final String displayName;

  /// Creates a successful device-name save action.
  /// @param displayName The persisted, trimmed display name.
  const DeviceNameSavedAction({required this.displayName});

  /// See [Equatable.props].
  @override
  List<Object?> get props => [displayName];
}
