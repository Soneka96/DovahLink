import 'dart:convert';

import 'package:dovahlink_client_sdk/src/protocol/json_map.dart';

/// One queued fake-transport reply awaiting release in test order.
class PendingReply {
  /// Creates an uncorrelated reply released without a request message ID.
  PendingReply.immediate(this._raw) : _decoded = null;

  /// Creates a reply that receives the next request message ID before release.
  PendingReply.correlated(this._decoded) : _raw = null;

  /// The raw reply when no correlation rewrite is needed.
  final String? _raw;

  /// The decoded reply whose correlation ID is rewritten before release.
  final JsonMap? _decoded;

  /// Whether this reply must wait for a request message ID.
  bool get needsCorrelation => _decoded != null;

  /// Resolves this reply with an optional request message ID.
  String resolve([String? messageId]) {
    final JsonMap? decoded = _decoded;
    if (decoded == null) {
      return _raw!;
    }
    decoded['correlationId'] = messageId;
    return jsonEncode(decoded);
  }
}
