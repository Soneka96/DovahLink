import 'dart:convert';
import 'dart:io';
import 'dart:typed_data';

import 'package:test/test.dart';

import 'package:dovahlink_client_sdk/src/dovahlink_client_exception.dart';
import 'package:dovahlink_client_sdk/src/persistence/persisted_client_state.dart';
import 'package:dovahlink_client_sdk/src/persistence/windows/dpapi.dart';
import 'package:dovahlink_client_sdk/src/persistence/windows/dpapi_client_storage.dart';

/// Builds an isolated state-file path so this test never touches a real per-user SDK state file,
/// mirroring this project's existing isolated-trust-store-path test convention.
String _isolatedStateFilePath() =>
    '${Directory.systemTemp.path}${Platform.pathSeparator}'
    'dovahlink-sdk-state-${DateTime.now().microsecondsSinceEpoch}.dat';

void main() {
  late String filePath;
  late DpapiClientStorage storage;

  setUp(() {
    filePath = _isolatedStateFilePath();
    storage = DpapiClientStorage(filePath: filePath);
  });

  tearDown(() {
    final File file = File(filePath);
    if (file.existsSync()) {
      file.deleteSync();
    }
    final File tempFile = File('$filePath.tmp');
    if (tempFile.existsSync()) {
      tempFile.deleteSync();
    }
  });

  group('load', () {
    test('returns the empty state when no file exists yet', () async {
      final PersistedClientState state = await storage.load();

      expect(state, const PersistedClientState());
    });

    test('round-trips a full state through save', () async {
      const PersistedClientState saved = PersistedClientState(
        clientId: 'client-1',
        credential: 'a1b2c3d4e5f6',
        recoveryState: PairingRecoveryState.confirming,
      );

      await storage.save(saved);
      final PersistedClientState loaded = await storage.load();

      expect(loaded, saved);
    });

    test('round-trips the empty state through save', () async {
      await storage.save(const PersistedClientState());
      final PersistedClientState loaded = await storage.load();

      expect(loaded, const PersistedClientState());
    });

    test(
      'throws DovahLinkStorageException for undecryptable (non-DPAPI) bytes',
      () async {
        await File(filePath).writeAsBytes(Uint8List.fromList(<int>[1, 2, 3, 4]));

        await expectLater(
          storage.load(),
          throwsA(isA<DovahLinkStorageException>()),
        );
      },
    );

    test(
      'throws DovahLinkStorageException for decryptable but non-JSON content',
      () async {
        final Uint8List encrypted = Dpapi.protect(
          Uint8List.fromList(utf8.encode('not json')),
        );
        await File(filePath).writeAsBytes(encrypted);

        await expectLater(
          storage.load(),
          throwsA(isA<DovahLinkStorageException>()),
        );
      },
    );

    test(
      'throws DovahLinkStorageException for an unsupported format version',
      () async {
        final Uint8List encrypted = Dpapi.protect(
          Uint8List.fromList(
            utf8.encode(
              jsonEncode(<String, dynamic>{
                'formatVersion': 999,
                'clientId': 'client-1',
                'credential': null,
                'recoveryState': 'none',
              }),
            ),
          ),
        );
        await File(filePath).writeAsBytes(encrypted);

        await expectLater(
          storage.load(),
          throwsA(isA<DovahLinkStorageException>()),
        );
      },
    );

    test(
      'throws DovahLinkStorageException for an unrecognized recoveryState',
      () async {
        final Uint8List encrypted = Dpapi.protect(
          Uint8List.fromList(
            utf8.encode(
              jsonEncode(<String, dynamic>{
                'formatVersion': PersistedClientState.currentFormatVersion,
                'clientId': null,
                'credential': null,
                'recoveryState': 'not-a-real-state',
              }),
            ),
          ),
        );
        await File(filePath).writeAsBytes(encrypted);

        await expectLater(
          storage.load(),
          throwsA(isA<DovahLinkStorageException>()),
        );
      },
    );

    test(
      'throws DovahLinkStorageException when clientId has the wrong type',
      () async {
        final Uint8List encrypted = Dpapi.protect(
          Uint8List.fromList(
            utf8.encode(
              jsonEncode(<String, dynamic>{
                'formatVersion': PersistedClientState.currentFormatVersion,
                'clientId': 42,
                'credential': null,
                'recoveryState': 'none',
              }),
            ),
          ),
        );
        await File(filePath).writeAsBytes(encrypted);

        await expectLater(
          storage.load(),
          throwsA(isA<DovahLinkStorageException>()),
        );
      },
    );

    test(
      'throws DovahLinkStorageException when credential has the wrong type',
      () async {
        final Uint8List encrypted = Dpapi.protect(
          Uint8List.fromList(
            utf8.encode(
              jsonEncode(<String, dynamic>{
                'formatVersion': PersistedClientState.currentFormatVersion,
                'clientId': null,
                'credential': 42,
                'recoveryState': 'none',
              }),
            ),
          ),
        );
        await File(filePath).writeAsBytes(encrypted);

        await expectLater(
          storage.load(),
          throwsA(isA<DovahLinkStorageException>()),
        );
      },
    );

    test(
      'throws DovahLinkStorageException when the decrypted JSON is not an object',
      () async {
        final Uint8List encrypted = Dpapi.protect(
          Uint8List.fromList(utf8.encode(jsonEncode(<int>[1, 2, 3]))),
        );
        await File(filePath).writeAsBytes(encrypted);

        await expectLater(
          storage.load(),
          throwsA(isA<DovahLinkStorageException>()),
        );
      },
    );

    test(
      'throws DovahLinkStorageException for a zero-byte state file',
      () async {
        await File(filePath).writeAsBytes(Uint8List(0));

        await expectLater(
          storage.load(),
          throwsA(isA<DovahLinkStorageException>()),
        );
      },
    );

    test(
      'a stale leftover .tmp file does not affect reading the real target file',
      () async {
        await storage.save(const PersistedClientState(clientId: 'client-1'));
        await File(
          '$filePath.tmp',
        ).writeAsBytes(Uint8List.fromList(<int>[9, 9, 9]));

        final PersistedClientState loaded = await storage.load();

        expect(loaded.clientId, 'client-1');
      },
    );

    test('the raw on-disk bytes never contain the plaintext credential', () async {
      const String credential = 'super-secret-credential-value';
      await storage.save(const PersistedClientState(credential: credential));

      final Uint8List raw = await File(filePath).readAsBytes();
      final String rawAsLatin1 = latin1.decode(raw, allowInvalid: true);

      expect(rawAsLatin1.contains(credential), isFalse);
    });
  });

  group('save', () {
    test('overwrites a previously saved state', () async {
      await storage.save(const PersistedClientState(clientId: 'client-1'));
      await storage.save(const PersistedClientState(clientId: 'client-2'));

      final PersistedClientState loaded = await storage.load();

      expect(loaded.clientId, 'client-2');
    });

    test('does not leave a temporary file behind after completing', () async {
      await storage.save(const PersistedClientState(clientId: 'client-1'));

      expect(File('$filePath.tmp').existsSync(), isFalse);
    });

    test(
      'cleans up the temporary file when the atomic rename fails',
      () async {
        // A held-open handle on the not-yet-existing target path makes the OS reject the rename
        // that would replace it, deterministically simulating a locked/in-use target.
        await File(filePath).create();
        final RandomAccessFile lock = File(
          filePath,
        ).openSync(mode: FileMode.write);

        try {
          await expectLater(
            storage.save(const PersistedClientState(clientId: 'client-1')),
            throwsA(isA<PathAccessException>()),
          );
        } finally {
          lock.closeSync();
        }

        expect(File('$filePath.tmp').existsSync(), isFalse);
      },
    );
  });

  group('clear', () {
    test('deletes the persisted state so load returns the empty state', () async {
      await storage.save(const PersistedClientState(clientId: 'client-1'));

      await storage.clear();

      final PersistedClientState loaded = await storage.load();
      expect(loaded, const PersistedClientState());
    });

    test('is idempotent: calling it when nothing exists does not throw', () async {
      await expectLater(storage.clear(), completes);
    });
  });
}
