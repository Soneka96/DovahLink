import 'package:dovahlink_client_sdk/src/protocol/protocol_format_exception.dart';

/// Validates UTC RFC 3339 timestamps used by protocol payloads.
class ProtocolTimestampValidator {
  /// Matches the canonical UTC form emitted by the Bridge, with optional microsecond precision.
  static final RegExp _utcRfc3339 = RegExp(
    r'^(\d{4})-(\d{2})-(\d{2})T(\d{2}):(\d{2}):(\d{2})(?:\.(\d{1,6}))?Z$',
  );

  /// Rejects [value] when it is not a valid UTC RFC 3339 timestamp.
  static void validateUtcRfc3339(String value) {
    final RegExpMatch? match = _utcRfc3339.firstMatch(value);
    final DateTime? parsed = DateTime.tryParse(value);
    if (match == null ||
        parsed == null ||
        !parsed.isUtc ||
        !_matchesComponents(match, parsed)) {
      throw const ProtocolFormatException(
        'occurredAt must be a valid UTC RFC 3339 timestamp.',
      );
    }
  }

  /// Confirms that Dart did not normalize an invalid calendar or clock component during parsing.
  static bool _matchesComponents(RegExpMatch match, DateTime parsed) {
    return parsed.year == int.parse(match.group(1)!) &&
        parsed.month == int.parse(match.group(2)!) &&
        parsed.day == int.parse(match.group(3)!) &&
        parsed.hour == int.parse(match.group(4)!) &&
        parsed.minute == int.parse(match.group(5)!) &&
        parsed.second == int.parse(match.group(6)!);
  }
}
