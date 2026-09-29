import 'package:dovahlink_client_sdk/dovahlink_client.dart';

/// Defines shutdown access to an already-created SDK client.
abstract interface class IExistingDovahLinkClient {
  /// Closes the SDK client only when app composition has created it.
  /// @return A future completing after close, or immediately when no client exists.
  Future<void> closeIfCreated();
}

/// Holds the shared SDK client created by app composition, if any.
class ExistingDovahLinkClient implements IExistingDovahLinkClient {
  /// The app-composed client, or `null` before its first resolution.
  DovahLinkClient? _client;

  /// Whether app composition has created the SDK client.
  bool get hasClient => _client != null;

  /// Records [client] after the app composition factory constructs it.
  /// @param client The single SDK client shared by app features and shutdown.
  void clientCreated(DovahLinkClient client) {
    _client = client;
  }

  /// Implements [IExistingDovahLinkClient.closeIfCreated].
  @override
  Future<void> closeIfCreated() async {
    await _client?.close();
  }
}
