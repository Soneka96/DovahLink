import 'dart:ffi';
import 'dart:typed_data';

import 'package:ffi/ffi.dart';
import 'package:win32/win32.dart';

import '../../dovahlink_client_exception.dart';

/// The documented, stable Win32 DPAPI flag suppressing any UI prompt. This SDK never wants DPAPI
/// to show a dialog, since (un)protection happens outside any user-interactive context; win32
/// 5.15.0 does not expose a named Dart constant for it, so the flag's well-known Win32 value is
/// recorded here directly. `CRYPTPROTECT_LOCAL_MACHINE` is deliberately never set: that flag would
/// scope encryption to the machine instead of the current Windows user.
const int _cryptProtectUiForbidden = 0x1;

/// A human-readable label attached to the encrypted blob by DPAPI; never contains secret material.
const String _dataDescription = 'DovahLink SDK client state';

/// Thin, directly testable wrapper over Windows DPAPI (`CryptProtectData`/`CryptUnprotectData`) in
/// its default per-user scope. [DpapiClientStorage] is this SDK's only production caller; tests
/// also call these directly to construct deliberately corrupt or foreign-format encrypted fixtures
/// that [DpapiClientStorage]'s own private encoding would not otherwise let a test produce.
class Dpapi {
  /// Prevents instantiation; every member is static.
  const Dpapi._();

  /// Encrypts [plaintext] with DPAPI in the default per-user scope.
  /// @throws DovahLinkStorageException if DPAPI reports failure.
  static Uint8List protect(Uint8List plaintext) {
    final Pointer<CRYPT_INTEGER_BLOB> dataIn = calloc<CRYPT_INTEGER_BLOB>();
    final Pointer<CRYPT_INTEGER_BLOB> dataOut = calloc<CRYPT_INTEGER_BLOB>();
    final Pointer<Uint8> plaintextPtr = calloc<Uint8>(plaintext.length);
    final Pointer<Utf16> description = _dataDescription.toNativeUtf16(
      allocator: calloc,
    );
    try {
      plaintextPtr.asTypedList(plaintext.length).setAll(0, plaintext);
      dataIn.ref
        ..cbData = plaintext.length
        ..pbData = plaintextPtr;

      final int result = CryptProtectData(
        dataIn,
        description,
        nullptr,
        nullptr,
        nullptr,
        _cryptProtectUiForbidden,
        dataOut,
      );
      if (result == 0) {
        throw DovahLinkStorageException(
          'CryptProtectData failed (GetLastError=${GetLastError()}).',
        );
      }
      return _copyAndFreeBlob(dataOut);
    } finally {
      calloc.free(dataIn);
      calloc.free(plaintextPtr);
      calloc.free(description);
      calloc.free(dataOut);
    }
  }

  /// Decrypts [encrypted] with DPAPI.
  /// @throws DovahLinkStorageException if DPAPI reports failure -- undecryptable, tampered, or
  ///     produced under a different Windows user's DPAPI master key.
  static Uint8List unprotect(Uint8List encrypted) {
    final Pointer<CRYPT_INTEGER_BLOB> dataIn = calloc<CRYPT_INTEGER_BLOB>();
    final Pointer<CRYPT_INTEGER_BLOB> dataOut = calloc<CRYPT_INTEGER_BLOB>();
    final Pointer<Uint8> encryptedPtr = calloc<Uint8>(encrypted.length);
    try {
      encryptedPtr.asTypedList(encrypted.length).setAll(0, encrypted);
      dataIn.ref
        ..cbData = encrypted.length
        ..pbData = encryptedPtr;

      final int result = CryptUnprotectData(
        dataIn,
        nullptr,
        nullptr,
        nullptr,
        nullptr,
        _cryptProtectUiForbidden,
        dataOut,
      );
      if (result == 0) {
        throw DovahLinkStorageException(
          'CryptUnprotectData failed (GetLastError=${GetLastError()}); the persisted state is '
          'undecryptable or corrupt.',
        );
      }
      return _copyAndFreeBlob(dataOut);
    } finally {
      calloc.free(dataIn);
      calloc.free(encryptedPtr);
      calloc.free(dataOut);
    }
  }

  /// Copies a DPAPI output [CRYPT_INTEGER_BLOB]'s bytes into a Dart-owned [Uint8List] and frees
  /// the blob's DPAPI-allocated buffer via `LocalFree`, per the documented
  /// `CryptProtectData`/`CryptUnprotectData` output-ownership contract.
  static Uint8List _copyAndFreeBlob(Pointer<CRYPT_INTEGER_BLOB> blob) {
    final int length = blob.ref.cbData;
    final Pointer<Uint8> data = blob.ref.pbData;
    final Uint8List copy = Uint8List.fromList(data.asTypedList(length));
    LocalFree(data);
    return copy;
  }
}
