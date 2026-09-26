import 'package:dovahlink_client_sdk/dovahlink_client.dart';

/// Defines shutdown access to an already-created SDK client.
abstract interface class IExistingDovahLinkClient {
  /// Disconnects the SDK client only when pairing has created it.
  /// @return A future completing after disconnect, or immediately when no client exists.
  Future<void> disconnectIfCreated();
}

/// Holds the SDK client created by the pairing dependency registration, if any.
class ExistingDovahLinkClient implements IExistingDovahLinkClient {
  /// The client created by pairing, or `null` before its first resolution.
  DovahLinkClient? _client;

  /// Whether pairing has created the SDK client.
  bool get hasClient => _client != null;

  /// Records [client] after the pairing composition factory constructs it.
  /// @param client The single SDK client shared by pairing and shutdown.
  void clientCreated(DovahLinkClient client) {
    _client = client;
  }

  /// Implements [IExistingDovahLinkClient.disconnectIfCreated].
  @override
  Future<void> disconnectIfCreated() async {
    await _client?.disconnect();
  }
}
