import 'package:dovahlink_client_sdk/src/dovahlink_storage_exception.dart';
import 'package:dovahlink_client_sdk/src/persistence/persisted_client_state.dart';
import 'package:dovahlink_client_sdk/src/protocol/json_map.dart';
import 'package:dovahlink_client_sdk/src/shared/enums.dart';

/// Decodes and validates the SDK-owned persisted client-state format.
class PersistedClientStateDecoder {
  /// Decodes [json] or throws [DovahLinkStorageException] for an unsupported or malformed state.
  static PersistedClientState decode(JsonMap json) {
    final Object? formatVersion = json['formatVersion'];
    if (formatVersion != PersistedClientState.currentFormatVersion) {
      throw DovahLinkStorageException(
        'Unsupported persisted client state format version: $formatVersion.',
      );
    }

    final Object? recoveryStateRaw = json['recoveryState'];
    final PairingRecoveryState recoveryState = switch (recoveryStateRaw) {
      'none' => PairingRecoveryState.none,
      'confirming' => PairingRecoveryState.confirming,
      _ => throw DovahLinkStorageException(
        'Unrecognized persisted recoveryState: $recoveryStateRaw.',
      ),
    };

    final Object? clientId = json['clientId'];
    if (clientId != null && clientId is! String) {
      throw const DovahLinkStorageException(
        'Persisted clientId is not a string.',
      );
    }
    final Object? credential = json['credential'];
    if (credential != null && credential is! String) {
      throw const DovahLinkStorageException(
        'Persisted credential is not a string.',
      );
    }

    return PersistedClientState(
      clientId: clientId as String?,
      credential: credential as String?,
      recoveryState: recoveryState,
    );
  }
}
