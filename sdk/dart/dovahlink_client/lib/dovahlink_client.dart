/// Public API for the DovahLink Dart Client SDK. Internal codec and transport-wiring classes stay
/// in `src/`. Public storage types support explicit injection and state inspection; the in-memory
/// test fake remains internal.
library;

export 'src/dovahlink_client.dart' show DovahLinkClient;
export 'src/dovahlink_compatibility_exception.dart'
    show DovahLinkCompatibilityException;
export 'src/dovahlink_discovery_service.dart'
    show DovahLinkDiscoveryService, IDovahLinkDiscoveryService;
export 'src/dovahlink_host.dart' show DovahLinkHost;
export 'src/dovahlink_host_identity_mismatch_exception.dart'
    show DovahLinkHostIdentityMismatchException;
// PairingOutcome is exported alongside the other domain enums, not hidden as a purely internal
// wire-decode detail: DovahLinkPairingException.outcome exposes it directly, so a consumer must be
// able to name and compare against it without reaching into src/.
export 'src/shared/enums.dart'
    show
        AdministrativeInvalidationReason,
        CredentialRejectionReason,
        HostVersionCompatibilityFailure,
        DovahLinkConnectionState,
        DovahLinkStateArea,
        DovahLinkStateStatus,
        DovahLinkTrustState,
        PairingAvailability,
        PairingCancelStatus,
        PairingOutcome,
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
export 'src/state/character_health_state.dart' show CharacterHealthState;
export 'src/state/character_level_state.dart' show CharacterLevelState;
export 'src/state/character_magicka_state.dart' show CharacterMagickaState;
export 'src/state/character_stamina_state.dart' show CharacterStaminaState;
export 'src/state/character_xp_state.dart' show CharacterXpState;
export 'src/state/state_synchronization.dart' show StateSynchronization;
