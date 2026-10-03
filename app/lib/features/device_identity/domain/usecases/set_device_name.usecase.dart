import 'dart:convert';

import 'package:fpdart/fpdart.dart';

import 'package:dovahlink_client/features/device_identity/domain/repositories/device_identity_repository.dart';
import 'package:dovahlink_client/features/device_identity/domain/usecases/params/set_device_name.params.dart';
import 'package:dovahlink_client/shared/constants/constants.dart';
import 'package:dovahlink_client/shared/failures/failures.dart';
import 'package:dovahlink_client/shared/usecase/usecase.dart';

/// Matches the control-character range rejected by Host display-name validation.
final RegExp _controlCharacterPattern = RegExp(r'[\x00-\x1F\x7F-\x9F]');

/// Validates and persists a user-selected device-name override.
abstract interface class ISetDeviceNameUseCase {
  /// Trims, validates, and persists the proposed display name.
  /// @param params The user's proposed display name.
  /// @return The persisted, trimmed name, or a validation or persistence [Failure].
  Future<Either<Failure, String>> call(SetDeviceNameParams params);
}

/// Trims, validates, and persists the user-selected device-name override.
class SetDeviceNameUseCase
    extends UseCase<Either<Failure, String>, SetDeviceNameParams>
    implements ISetDeviceNameUseCase {
  /// The repository that owns local override persistence.
  final IDeviceIdentityRepository _repository;

  /// Creates a use case backed by [_repository].
  /// @param repository The owner of local override persistence.
  SetDeviceNameUseCase(IDeviceIdentityRepository repository)
    : _repository = repository;

  /// Implements [UseCase.call]. Empty input stores [defaultDeviceName].
  /// @param params The user's proposed display name.
  /// @return `Right` when saved, or a validation or persistence [Failure].
  @override
  Future<Either<Failure, String>> call(SetDeviceNameParams params) async {
    final String trimmed = params.displayName.trim();
    final String displayName = trimmed.isEmpty ? defaultDeviceName : trimmed;
    if (utf8.encode(displayName).length > maxDeviceNameLengthBytes ||
        _controlCharacterPattern.hasMatch(displayName)) {
      return const Left(
        ValidationFailure(
          'Use a device name of 64 UTF-8 bytes or less without control characters.',
        ),
      );
    }
    final Either<Failure, Unit> saved = await _repository
        .saveDisplayNameOverride(displayName);
    return saved.map((Unit _) => displayName);
  }
}
