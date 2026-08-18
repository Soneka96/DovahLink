/// Public API for the DovahLink Dart Client SDK. Internal codec and transport-wiring classes stay
/// in `src/` and are not exported here -- see `ai/context/sdk/api-design.md`'s "curated public
/// exports". Persistence is a partial exception: [ClientStorage], the value types it stores, and
/// the real Windows implementation are exported because a consumer must be able to name, inject,
/// or construct them directly, even though [DovahLinkClient.windows] wires the default choice
/// automatically; the in-memory test fake stays internal since no real consumer needs it yet.
library;

export 'src/dovahlink_client.dart' show DovahLinkClient;
export 'src/shared/enums.dart'
    show
        CredentialRejectionReason,
        DovahLinkConnectionState,
        DovahLinkTrustState,
        PairingAvailability,
        PairingCancelStatus,
        PairingRenotifyStatus;
export 'src/hello_result.dart' show HelloResult;
export 'src/pairing_cancel_outcome.dart' show PairingCancelOutcome;
export 'src/pairing_challenge_status.dart' show PairingChallengeStatus;
export 'src/pairing_renotify_result.dart' show PairingRenotifyResult;
export 'src/dovahlink_client_exception.dart'
    show
        DovahLinkConnectionException,
        DovahLinkPairingException,
        DovahLinkProtocolException,
        DovahLinkStorageException;
export 'src/persistence/client_storage.dart' show ClientStorage;
export 'src/persistence/persisted_client_state.dart'
    show PairingRecoveryState, PersistedClientState;
export 'src/persistence/windows/dpapi_client_storage.dart'
    show DpapiClientStorage;
// DovahLinkTransport is exported alongside DovahLinkClient, not hidden as a purely internal type:
// DovahLinkClient's own public constructor accepts one (for a real socket by default, or an
// injected implementation for advanced/test use), so a consumer must be able to name and
// implement the interface without reaching into src/.
export 'src/transport/dovahlink_transport.dart' show DovahLinkTransport;
