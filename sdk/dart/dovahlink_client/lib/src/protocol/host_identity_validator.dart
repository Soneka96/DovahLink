import 'dart:convert';

import 'package:dovahlink_client_sdk/src/shared/constants.dart';

/// Matches the hyphenated UUID shape used for Host installation identities.
final RegExp _hostIdPattern = RegExp(
  r'^[0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12}$',
);

/// Reports whether [hostId] uses the supported non-empty UUID shape.
/// @param hostId The Host installation identity to validate.
/// @return `true` when [hostId] is a supported, non-zero UUID.
bool isValidHostId(String hostId) =>
    _hostIdPattern.hasMatch(hostId) &&
    hostId.toLowerCase() != '00000000-0000-0000-0000-000000000000';

/// Reports whether [hostName] satisfies the Host-name protocol constraints.
/// @param hostName The current OS computer name to validate.
/// @return `true` when the name is non-empty, well-formed, control-free, and within its UTF-8 limit.
bool isValidHostName(String hostName) {
  if (hostName.trim().isEmpty) {
    return false;
  }
  for (int index = 0; index < hostName.length; index++) {
    final int codeUnit = hostName.codeUnitAt(index);
    if (codeUnit >= 0xd800 && codeUnit <= 0xdbff) {
      if (index + 1 >= hostName.length) {
        return false;
      }
      final int nextCodeUnit = hostName.codeUnitAt(index + 1);
      if (nextCodeUnit < 0xdc00 || nextCodeUnit > 0xdfff) {
        return false;
      }
      index++;
    } else if (codeUnit >= 0xdc00 && codeUnit <= 0xdfff) {
      return false;
    }
  }
  if (hostName.runes.any(
    (int rune) => rune < 0x20 || (rune >= 0x7f && rune <= 0x9f),
  )) {
    return false;
  }
  return utf8.encode(hostName).length <= kMaxHostNameLengthBytes;
}
