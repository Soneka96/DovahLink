import 'dart:async';

import 'package:fpdart/fpdart.dart';
import 'package:redux/redux.dart';

import 'package:dovahlink_client/features/device_identity/domain/usecases/params/set_device_name.params.dart';
import 'package:dovahlink_client/features/device_identity/domain/usecases/set_device_name.usecase.dart';
import 'package:dovahlink_client/features/device_identity/presentation/state/device_identity.actions.dart';
import 'package:dovahlink_client/injection_container.dart';
import 'package:dovahlink_client/shared/constants/enums.dart';
import 'package:dovahlink_client/shared/failures/failures.dart';
import 'package:dovahlink_client/shared/state/app_state.dart';

import 'package:dovahlink_client_sdk/dovahlink_client.dart'
    show
        DovahLinkClient,
        DovahLinkConnectionState,
        IDovahLinkCurrentHost,
        DovahLinkTrustState,
        RenameOutcome;

/// Handles device-name actions and coordinates local persistence with one active Host rename.
abstract interface class IDeviceIdentityMiddleware {
  /// Routes an app action to the matching identity handler.
  /// @param store The application store receiving the action.
  /// @param action The Redux action to route.
  /// @param next The next middleware or reducer in the chain.
  void call(Store<AppState> store, dynamic action, NextDispatcher next);
}

/// Coordinates local device-name persistence and an optional active-Host rename.
class DeviceIdentityMiddleware extends MiddlewareClass<AppState>
    implements IDeviceIdentityMiddleware {
  /// Prevents overlapping saves while the Settings action is in progress.
  bool _isSaving = false;

  /// Creates the device-identity middleware.
  DeviceIdentityMiddleware();

  /// Implements [IDeviceIdentityMiddleware.call].
  @override
  void call(Store<AppState> store, dynamic action, NextDispatcher next) {
    next(action);

    switch (action) {
      case final DeviceNameSaveRequestedAction saveRequested:
        _deviceNameSaveRequested(store, saveRequested);
      default:
        break;
    }
  }

  /// Starts local persistence and optional Host rename for a Settings save.
  /// @param store The app store receiving save result actions.
  /// @param action The proposed device name.
  void _deviceNameSaveRequested(
    Store<AppState> store,
    DeviceNameSaveRequestedAction action,
  ) {
    if (_isSaving) {
      return;
    }
    _isSaving = true;
    unawaited(_saveDeviceName(store, action));
  }

  /// Persists the local name before requesting an active Host acknowledgement.
  /// @param store The app store receiving local and remote result actions.
  /// @param action The original name save request.
  Future<void> _saveDeviceName(
    Store<AppState> store,
    DeviceNameSaveRequestedAction action,
  ) async {
    try {
      final Either<Failure, String> saveResult;
      try {
        saveResult = await sl<ISetDeviceNameUseCase>()(
          SetDeviceNameParams(displayName: action.displayName),
        );
      } on Object {
        store.dispatch(
          const DeviceNameSaveFailedAction(
            message: 'The device name could not be saved.',
          ),
        );
        return;
      }

      final String? savedName = saveResult.fold<String?>(
        (Failure failure) {
          store.dispatch(DeviceNameSaveFailedAction(message: failure.message));
          return null;
        },
        (String name) {
          store.dispatch(DeviceNameSavedAction(displayName: name));
          return name;
        },
      );
      if (savedName == null) {
        return;
      }

      DeviceNameRenameStatus remoteRenameStatus =
          DeviceNameRenameStatus.unconfirmed;
      String? remoteHostId;
      try {
        final DovahLinkClient client = sl<DovahLinkClient>();
        final IDovahLinkCurrentHost currentHost = client.currentHost;
        final DovahLinkConnectionState connectionState =
            client.connections.state;
        final DovahLinkTrustState? trustState = currentHost.trustState;
        remoteHostId = currentHost.host?.hostId;
        if (connectionState != DovahLinkConnectionState.connected ||
            trustState != DovahLinkTrustState.trusted ||
            remoteHostId == null) {
          remoteRenameStatus = DeviceNameRenameStatus.notAttempted;
          remoteHostId = null;
        } else {
          final RenameOutcome outcome = await client.connections.renameDevice(
            savedName,
          );
          remoteRenameStatus = switch (outcome) {
            RenameOutcome.renamed => DeviceNameRenameStatus.renamed,
            RenameOutcome.invalidDisplayName =>
              DeviceNameRenameStatus.invalidDisplayName,
            RenameOutcome.notTrusted => DeviceNameRenameStatus.notTrusted,
          };
        }
      } on Object {
        // The local name is already durable, but this Host did not confirm the rename.
      }
      store.dispatch(
        DeviceNameSaveFinishedAction(
          remoteRenameStatus: remoteRenameStatus,
          remoteHostId: remoteHostId,
        ),
      );
    } finally {
      _isSaving = false;
    }
  }
}
