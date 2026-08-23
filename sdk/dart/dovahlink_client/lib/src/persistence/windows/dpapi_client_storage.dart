import 'dart:convert';
import 'dart:io';
import 'dart:typed_data';

import 'package:dovahlink_client_sdk/src/dovahlink_storage_exception.dart';
import 'package:dovahlink_client_sdk/src/persistence/client_storage.dart';
import 'package:dovahlink_client_sdk/src/persistence/persisted_client_state.dart';
import 'package:dovahlink_client_sdk/src/persistence/persisted_client_state_decoder.dart';
import 'package:dovahlink_client_sdk/src/persistence/windows/dpapi.dart';

/// A [ClientStorage] implementation for Windows, encrypting persisted state at rest with DPAPI
/// ([Dpapi]) in its default per-user scope -- the OS itself ties the encrypted material to the
/// logged-in Windows user, matching `ai/context/protocol/security.md`'s "approved per-user
/// secure-storage mechanism for the platform" and mirroring the Bridge's own DPAPI decision there.
/// Fails closed: a missing file is a valid empty store, but a file that exists and cannot be
/// decrypted or parsed always throws [DovahLinkStorageException] rather than silently returning an
/// empty or partial state.
class DpapiClientStorage implements ClientStorage {
  /// The absolute path of the encrypted state file.
  final String _filePath;

  /// Creates a Windows DPAPI-backed storage adapter. [filePath] defaults to a fixed location
  /// under the current user's local application-data directory.
  DpapiClientStorage({String? filePath})
    : _filePath = filePath ?? _defaultFilePath();

  /// See [ClientStorage.load].
  @override
  Future<PersistedClientState> load() async {
    final File file = File(_filePath);
    if (!file.existsSync()) {
      return const PersistedClientState();
    }

    final Uint8List encrypted = await file.readAsBytes();
    final Uint8List decrypted = Dpapi.unprotect(encrypted);

    final Object? decoded;
    try {
      decoded = jsonDecode(utf8.decode(decrypted));
    } on FormatException catch (error) {
      throw DovahLinkStorageException(
        'Persisted client state is not valid JSON: $error',
      );
    }
    if (decoded is! Map<String, dynamic>) {
      throw const DovahLinkStorageException(
        'Persisted client state is not a JSON object.',
      );
    }
    return PersistedClientStateDecoder.decode(decoded);
  }

  /// See [ClientStorage.save]. Writes to a temporary file and atomically renames it over the
  /// target, so a crash mid-write cannot leave a partially written state file.
  @override
  Future<void> save(PersistedClientState state) async {
    final Map<String, dynamic> json = <String, dynamic>{
      'formatVersion': PersistedClientState.currentFormatVersion,
      'clientId': state.clientId,
      'credential': state.credential,
      'recoveryState': state.recoveryState.name,
    };
    final Uint8List plaintext = Uint8List.fromList(
      utf8.encode(jsonEncode(json)),
    );
    final Uint8List encrypted = Dpapi.protect(plaintext);

    final File file = File(_filePath);
    file.parent.createSync(recursive: true);
    final File tempFile = File('$_filePath.tmp');
    await tempFile.writeAsBytes(encrypted, flush: true);
    try {
      await tempFile.rename(_filePath);
    } on Object {
      // Never leave the temporary file behind on a failed rename (e.g. the target is locked by
      // another process); a future save() or load() must not have to reason about stale .tmp
      // litter left by a prior failed attempt.
      if (tempFile.existsSync()) {
        tempFile.deleteSync();
      }
      rethrow;
    }
  }

  /// See [ClientStorage.clear].
  @override
  Future<void> clear() async {
    final File file = File(_filePath);
    if (file.existsSync()) {
      file.deleteSync();
    }
  }

  /// Resolves the default per-user state file path under `%LOCALAPPDATA%\DovahLink\`.
  static String _defaultFilePath() {
    final String? localAppData = Platform.environment['LOCALAPPDATA'];
    if (localAppData == null || localAppData.isEmpty) {
      throw StateError(
        'LOCALAPPDATA is not set; DpapiClientStorage requires a Windows per-user profile.',
      );
    }
    return '$localAppData\\DovahLink\\sdk_client_state.dat';
  }
}
