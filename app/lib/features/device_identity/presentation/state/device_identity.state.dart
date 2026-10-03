import 'package:equatable/equatable.dart';
import 'package:meta/meta.dart';

import 'package:dovahlink_client/shared/constants/constants.dart';
import 'package:dovahlink_client/shared/constants/enums.dart';

/// Immutable application state for the resolved device name and its load status.
@immutable
class DeviceIdentityState extends Equatable {
  /// The resolved display name, or `null` if its saved override could not be read.
  final String? displayName;

  /// A user-safe load error, or `null` when the name loaded successfully.
  final String? loadFailure;

  /// Whether a local save and its active-Host rename attempt are still running.
  final bool isSaving;

  /// A local validation or persistence failure, or `null` after a successful local save.
  final String? saveFailure;

  /// The last Host rename outcome, or `null` before a save has completed.
  final DeviceNameRenameStatus? remoteRenameStatus;

  /// The Host whose rename outcome [remoteRenameStatus] describes, or `null` if none was attempted.
  final String? remoteHostId;

  /// Creates device-identity state.
  /// @param displayName The resolved name, or `null` when it could not be loaded.
  /// @param loadFailure The user-safe load failure, or `null` when loading succeeded.
  /// @param isSaving Whether a local save and remote acknowledgement are pending.
  /// @param saveFailure A local save error, or `null` after success.
  /// @param remoteRenameStatus The active-Host rename outcome, if a save completed.
  /// @param remoteHostId The Host associated with that rename outcome.
  const DeviceIdentityState({
    required this.displayName,
    this.loadFailure,
    this.isSaving = false,
    this.saveFailure,
    this.remoteRenameStatus,
    this.remoteHostId,
  });

  /// Returns the initial value for a new app state.
  factory DeviceIdentityState.initial() =>
      const DeviceIdentityState(displayName: defaultDeviceName);

  /// See [Equatable.props].
  @override
  List<Object?> get props => [
    displayName,
    loadFailure,
    isSaving,
    saveFailure,
    remoteRenameStatus,
    remoteHostId,
  ];
}
