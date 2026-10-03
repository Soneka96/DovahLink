import 'package:equatable/equatable.dart';

import 'package:dovahlink_client/shared/constants/enums.dart';

/// Requests persistence of a device-name override.
class DeviceNameSaveRequestedAction extends Equatable {
  /// The unmodified text entered in Settings.
  final String displayName;

  /// Creates a device-name save request.
  /// @param displayName The proposed override to normalize and persist.
  const DeviceNameSaveRequestedAction({required this.displayName});

  /// See [Equatable.props].
  @override
  List<Object?> get props => [displayName];
}

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

/// Records a local validation or persistence failure.
class DeviceNameSaveFailedAction extends Equatable {
  /// The user-safe local save failure.
  final String message;

  /// Creates a failed device-name save action.
  /// @param message The user-safe failure description.
  const DeviceNameSaveFailedAction({required this.message});

  /// See [Equatable.props].
  @override
  List<Object?> get props => [message];
}

/// Records the active Host's rename acknowledgement after the local name was saved.
class DeviceNameSaveFinishedAction extends Equatable {
  /// The Host's typed rename status, or `notAttempted` when no trusted Host was active.
  final DeviceNameRenameStatus remoteRenameStatus;

  /// The exact Host whose response is represented, or `null` when no request was sent.
  final String? remoteHostId;

  /// Creates a completed device-name save action.
  /// @param remoteRenameStatus The active Host's rename response status.
  /// @param remoteHostId The active Host ID, or `null` if no Host was eligible.
  const DeviceNameSaveFinishedAction({
    required this.remoteRenameStatus,
    this.remoteHostId,
  });

  /// See [Equatable.props].
  @override
  List<Object?> get props => [remoteRenameStatus, remoteHostId];
}
