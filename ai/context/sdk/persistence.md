# SDK persistence and caching

## Ownership rule

If persisted data is required for correct reusable DovahLink client behavior, the SDK owns it. If
persisted data exists only for the official product experience, the app owns it.

SDK-owned persistence includes: the stable local `clientId`, Host-scoped bearer credentials, the
single Host-owned pairing recovery operation, reusable Known Host information required by approved
connection semantics, reusable
resource/cache metadata, cache-format version, and SDK persistence-format version.
App-owned persistence includes product/UI preferences such as preferred Host selection, dashboard
layout, map zoom, selected marker, and UI filters.

Administrative invalidation reasons are not persisted as authoritative trust state. The approved
Known Host recovery presentation will add only a per-Host `pairingRequired` hint after a Host-reported
`revoked`/`unrecognized` credential rejection or an administrative
`revoked`/`trust_reset`/`factory_reset` event for an admitted Known Host session. The implementation
will not persist the reason, set the hint for `blocked`, or retain an existing hint when the Host
reports `blocked`. This is last-known UI guidance, not a statement of current trust; an offline Host
can make it stale. Selecting the action must run normal SDK authentication and pairing, and the
Host's current response remains authoritative. Clear the hint after trusted Known Host
authentication, successful pairing, or forgetting the Host. An administrative invalidation ending a
developer-token session must not set a Known Host hint or delete the configured developer token.
Because the hint changes SDK-owned storage, implementation requires a format version bump and
migration that preserves `clientId`, credentials, endpoint metadata, and pending pairing recovery.

Each Known Host relationship currently stores `hostId`, last-known `hostName`, last-known `endpoint`,
and the current bearer credential issued by that Host. `hostId` is identity; name and endpoint are
mutable metadata. The approved `pairingRequired` hint will not be authoritative trust state: the Host
establishes current trust on every session, and no trusted/connected/offline/blocked/revoked status
is persisted. A discovery claim or unpaired `hello_ack` alone never writes Known Host metadata or
sets the hint. Successful code confirmation atomically adds or updates only the issuing Host's
relationship, clears the hint, and records `confirming` recovery with that Host ID. A second Host's
pairing preserves earlier relationships and credentials. If final
`pairing_ack` reports `pending_not_found` or `pairing_invalidated`, the SDK removes that Host's
incomplete credential and recovery state but keeps its Known Host metadata. A trusted Known Host
session refreshes only the matching record's name and endpoint. During pending recovery, another
Host ID fails before session admission. Credential removal and failed recovery preserve the owning
Known Host metadata; none of this metadata establishes current trust. The SDK canonicalizes Host
UUIDs to lowercase when persisted state is constructed, so Known Host keys, stored Host IDs, and the
pending recovery owner share one form.

Current persisted client state stores `knownHosts` keyed by `hostId`. Each relationship contains
Host metadata and the current bearer credential issued by that Host. The single pending pairing
recovery record contains its owning `hostId` and phase. A credential or recovery operation cannot
be selected for a different Host. Host-selection preference remains app-owned.

The app must not persist a competing authoritative copy of SDK-owned protocol or client state: not
the client credential, not pairing `CONFIRMING` recovery state, not actual subscription state, not
reconnect state, not authoritative revision/recovery state, not SDK cache-validity metadata, and not
a duplicated copy of trusted-device authority.

## Storage abstraction

SDK persistence sits behind explicit storage boundaries rather than scattered direct filesystem or
secure-storage calls throughout its state machines. Platform-specific storage facilities (secure
credential storage, cache/filesystem location) stay behind the platform ports defined in
`ai/context/sdk/architecture.md`.

The `IClientStorage` interface implements this boundary for `clientId`, Host-scoped credentials,
Host-owned pairing recovery, and Known Hosts
(`sdk/dart/dovahlink_client/lib/src/persistence/client_storage.dart`); its Windows implementation,
`DpapiClientStorage`, is the platform port this section describes, using DPAPI in the per-user scope
`ai/context/protocol/security.md` requires and failing closed on corrupt or undecryptable state rather
than substituting a plausible default.

`ClientStateService` is the single SDK owner of persisted client-state reads and mutations. It
serializes updates to the complete `PersistedClientState`, preserving atomic pairing writes across
the Host relationship and Host-owned `CONFIRMING` recovery. It updates in-memory state and publishes
semantic projections such as `knownHostsChanges` only after storage succeeds; a failed save
publishes no speculative state. Equivalent public Known Hosts views do not produce duplicate
stream events. A failed initial read is reported as a stream error; the same subscriber remains
attached and receives state from a later successful SDK load or mutation. A failed read is never
represented as an empty collection.

The standard SDK entry point does not expose or import Windows storage. Windows consumers import
`dovahlink_client_windows.dart` for `DpapiClientStorage` and inject it through `IClientStorage`.
Until secure storage is implemented on another platform, composition may use
`UnsupportedClientStorage`: constructing the client remains safe, while the first persistence
operation throws `UnsupportedError`. No plaintext or in-memory credential fallback is used.

## Versioning and migration

Persisted SDK formats are versioned. The SDK that owns a persistent format owns its migrations; the
official application must never need to understand or migrate the SDK's private persistence schema.

`PersistedClientState.currentFormatVersion` is the concrete version field this section describes for
SDK client state. Version 3 stores Host relationships by Host ID and an optional Host-owned pending
pairing recovery record. Versions 1 and 2 contain a global bearer credential whose Host owner cannot
be established as a cryptographic identity; because these are unreleased development formats, the
SDK preserves the stable `clientId` and invalidates their Host metadata, credentials, and recovery
state. The user must pair again. The SDK does not fabricate a Host from an endpoint, computer name,
or discovery result. Unknown future versions and malformed v3 Host objects throw
`DovahLinkStorageException`.

## Cache ownership

The SDK is not merely a WebSocket wrapper: reusable DovahLink domain/resource caching belongs in
the SDK when cache correctness is part of reusable client behavior. Distinguish:

- **SDK/domain cache** — resource identity, resource version, reusable downloaded resource data,
  cache metadata, cache validity, invalidation, and cache-format migration.
- **App/presentation state** — current zoom, viewport position, selected marker, rendering choices,
  visual filters, and other product-specific rendering preferences.

The SDK must not assume a cached resource is valid merely because a file exists; every reusable
cache eventually needs an explicit identity, validity, invalidation, and migration model appropriate
to its feature. Do not prematurely invent the final map (or other future feature) cache identity
here — the future feature phase that introduces the cache owns its exact semantics; this file only
fixes the ownership boundary.
