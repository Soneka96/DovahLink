/// Owns bounded automatic recovery from ordinary transport loss, per
/// `ai/context/sdk/architecture.md`'s "Internal composition". Driven by `SessionService`
/// through the `onOrdinaryTransportLoss` callback described in
/// `ai/context/sdk/architecture.md`'s "Callbacks".
abstract interface class ReconnectService {
  /// Reports that ordinary transport loss finished tearing down the connection previously
  /// established at [uri], starting bounded automatic recovery.
  void onOrdinaryTransportLoss(Uri uri);
}
