import 'package:equatable/equatable.dart';
import 'package:meta/meta.dart';

import 'package:dovahlink_client/shared/constants/constants.dart';

/// Immutable application state for the resolved device name and its load status.
@immutable
class DeviceIdentityState extends Equatable {
  /// The resolved display name, or `null` if its saved override could not be read.
  final String? displayName;

  /// A user-safe load error, or `null` when the name loaded successfully.
  final String? loadFailure;

  /// Creates device-identity state.
  /// @param displayName The resolved name, or `null` when it could not be loaded.
  /// @param loadFailure The user-safe load failure, or `null` when loading succeeded.
  const DeviceIdentityState({required this.displayName, this.loadFailure});

  /// Returns the initial value for a new app state.
  factory DeviceIdentityState.initial() =>
      const DeviceIdentityState(displayName: defaultDeviceName);

  /// See [Equatable.props].
  @override
  List<Object?> get props => [displayName, loadFailure];
}
