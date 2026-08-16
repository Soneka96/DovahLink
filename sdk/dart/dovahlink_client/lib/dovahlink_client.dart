/// Public API for the DovahLink Dart Client SDK. Internal transport, codec, persistence, and
/// protocol-model classes stay in `src/` and are not exported here -- see
/// `ai/context/sdk/api-design.md`'s "curated public exports".
library;

export 'src/dovahlink_client.dart' show DovahLinkClient;
export 'src/enums.dart'
    show DovahLinkConnectionState, DovahLinkTrustState, PairingAvailability;
export 'src/hello_result.dart' show HelloResult;
export 'src/dovahlink_client_exception.dart'
    show
        DovahLinkConnectionException,
        DovahLinkPairingException,
        DovahLinkProtocolException;
// DovahLinkTransport is exported alongside DovahLinkClient, not hidden as a purely internal type:
// DovahLinkClient's own public constructor accepts one (for a real socket by default, or an
// injected implementation for advanced/test use), so a consumer must be able to name and
// implement the interface without reaching into src/.
export 'src/transport/dovahlink_transport.dart' show DovahLinkTransport;
