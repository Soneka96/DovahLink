/// Public API for the DovahLink Dart Client SDK. Internal codec and transport-wiring classes stay
/// in `src/`. Public storage types support explicit injection and state inspection; the in-memory
/// test fake remains internal.
library;

export 'src/dovahlink_client.dart' show DovahLinkClient;
export 'src/dovahlink_connections.dart' show IDovahLinkConnections;
export 'src/dovahlink_character.dart' show IDovahLinkCharacter;
export 'src/dovahlink_current_host.dart' show IDovahLinkCurrentHost;
export 'src/dovahlink_compatibility_exception.dart'
    show DovahLinkCompatibilityException;
export 'src/dovahlink_discovery_service.dart'
    show DovahLinkDiscoveryService, IDovahLinkDiscoveryService;
export 'src/host_presence_probe.dart'
    show HostPresenceProbe, IHostPresenceProbe;
export 'src/dovahlink_host.dart' show DovahLinkHost;
export 'src/dovahlink_host_id.dart' show DovahLinkHostId;
export 'src/dovahlink_hosts.dart' show IDovahLinkHosts;
export 'src/dovahlink_known_host_state.dart' show DovahLinkKnownHostState;
export 'src/dovahlink_known_host_invalidation.dart'
    show DovahLinkKnownHostInvalidation;
export 'src/dovahlink_known_host_not_found_exception.dart'
    show DovahLinkKnownHostNotFoundException;
export 'src/dovahlink_host_identity_mismatch_exception.dart'
    show DovahLinkHostIdentityMismatchException;
export 'src/dovahlink_pairing.dart' show IDovahLinkPairing;
export 'src/dovahlink_pairing_handshake.dart' show DovahLinkPairingHandshake;
// PairingOutcome and RenameOutcome are exported alongside the other domain enums, not hidden as
// purely internal wire-decode details: their public operations expose them directly, so a consumer
// can name and compare every typed result without reaching into src/.
export 'src/shared/enums.dart'
    show
        AdministrativeInvalidationReason,
        CredentialRejectionReason,
        HostVersionCompatibilityFailure,
        DovahLinkConnectionState,
        DovahLinkInitialConnectionRetryStatus,
        DovahLinkHostAvailability,
        DovahLinkKnownHostSessionState,
        DovahLinkStateArea,
        DovahLinkStateStatus,
        DovahLinkTrustState,
        PairingAvailability,
        PairingCancelStatus,
        PairingOutcome,
        RenameOutcome,
        PairingRecoveryState,
        PairingRenotifyStatus,
        ProtocolErrorCode;
export 'src/hello_result.dart' show HelloResult;
export 'src/pairing_cancel_outcome.dart' show PairingCancelOutcome;
export 'src/pairing_challenge_status.dart' show PairingChallengeStatus;
export 'src/pairing_renotify_result.dart' show PairingRenotifyResult;
export 'src/dovahlink_connection_exception.dart'
    show DovahLinkConnectionException;
export 'src/dovahlink_pairing_exception.dart' show DovahLinkPairingException;
export 'src/dovahlink_protocol_exception.dart' show DovahLinkProtocolException;
export 'src/dovahlink_storage_exception.dart' show DovahLinkStorageException;
export 'src/persistence/client_storage.dart' show IClientStorage;
export 'src/persistence/persisted_client_state.dart' show PersistedClientState;
export 'src/persistence/unsupported_client_storage.dart'
    show UnsupportedClientStorage;
export 'src/state/character_identity_state.dart' show CharacterIdentityState;
export 'src/state/character_level_state.dart' show CharacterLevelState;
export 'src/state/character_supernatural_traits_state.dart'
    show CharacterSupernaturalTraitsState;
export 'src/state/character_vital.dart' show CharacterVital;
export 'src/state/character_vitals_state.dart' show CharacterVitalsState;
export 'src/state/character_xp_state.dart' show CharacterXpState;
export 'src/state/state_synchronization.dart' show StateSynchronization;
