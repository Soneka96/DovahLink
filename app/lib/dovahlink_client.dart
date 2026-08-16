/// Public API for the DovahLink pre-SDK client library
/// (`ai/context/protocol/security.md`-compatible connect/hello/pairing/disconnect, real WebSocket
/// transport, no Flutter or Redux dependency). Internal transport, codec, and protocol-model
/// classes stay in `src/` and are not exported here -- see `ai/context/sdk/api-design.md`'s
/// "curated public exports".
library;

export 'src/dovahlink_client/dovahlink_client.dart'
    show
        DovahLinkClient,
        DovahLinkConnectionState,
        DovahLinkTrustState,
        HelloResult,
        PairingAvailability;
export 'src/dovahlink_client/dovahlink_client_exception.dart'
    show
        DovahLinkConnectionException,
        DovahLinkPairingException,
        DovahLinkProtocolException;
// DovahLinkTransport is exported alongside DovahLinkClient, not hidden as a purely internal type:
// DovahLinkClient's own public constructor accepts one (for a real socket by default, or an
// injected implementation for advanced/test use), so a consumer must be able to name and
// implement the interface without reaching into src/.
export 'src/dovahlink_client/transport/dovahlink_transport.dart'
    show DovahLinkTransport;
