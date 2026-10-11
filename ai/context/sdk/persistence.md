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

Administrative invalidation reasons are not persisted as authoritative trust state. Known Host
recovery presentation uses only a per-Host `pairingRequired` hint after a Host-reported
`revoked`/`unrecognized` credential rejection or an administrative
`revoked`/`trust_reset`/`factory_reset` event for an admitted Known Host session. The implementation
does not persist the reason, set the hint for `blocked`, or retain an existing hint when the Host
reports `blocked`. This is last-known UI guidance, not a statement of current trust; an offline Host
can make it stale. Selecting the action must run normal SDK authentication and pairing, and the
Host's current response remains authoritative. Clear the hint after trusted Known Host
authentication, successful pairing, or forgetting the Host. An administrative invalidation ending a
developer-token session must not set a Known Host hint or delete the configured developer token.
The v4 format stores the hint and migrates v3 Known Hosts while preserving `clientId`, credentials,
endpoint metadata, and pending pairing recovery.

Each Known Host relationship currently stores `hostId`, last-known `hostName`, last-known `endpoint`,
the current bearer credential issued by that Host, and its `pairingRequired` hint. `hostId` is
identity; name and endpoint are mutable metadata. The hint is not authoritative trust state: the Host
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

`PersistedClientState.currentFormatVersion` is 4, the current SDK client-state format. Version 4
stores Host relationships by Host ID, an optional Host-owned pending pairing recovery record, and
the `pairingRequired` hint. Version 3 is migration input to version 4: migration preserves the
stable `clientId`, every Known Host and its metadata, credentials, and pending pairing recovery;
the new `pairingRequired` hint defaults to `false`. Versions 1 and 2 contain a global bearer
credential whose Host owner cannot be established as a cryptographic identity; because these are
unreleased development formats, the SDK preserves the stable `clientId` and invalidates their Host
metadata, credentials, and recovery state. The user must pair again. The SDK does not fabricate a
Host from an endpoint, computer name, or discovery result. Unknown future versions and malformed v3
or v4 Host objects throw `DovahLinkStorageException`.

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

## Historical gameplay snapshots

**Status: approved ownership rule; not implemented.** No historical storage, port, or API exists
today. The SDK capability that provides it is future work (T11 in
[`roadmap/deviations/sdk-flutter-architecture-refactoring`](../../../roadmap/deviations/sdk-flutter-architecture-refactoring/README.md));
this section fixes who owns the persistence and how it stays separate from client identity and
trust. The semantic distinction between live and historical values is owned by
[SDK API design, "Live versus historical state"](api-design.md#live-versus-historical-state).

### Ownership and separation

The SDK owns historical gameplay snapshot persistence, because a reusable client's remembered
observations are part of reusable DovahLink client behavior (see "Ownership rule"). Flutter must not
implement an independent gameplay snapshot database or its own capture, merge, or retention rules.
Flutter-owned presentation preferences stay app-owned.

Four kinds of data stay distinct and do not share a storage responsibility:

| Data | Meaning | Owner |
| --- | --- | --- |
| Client credentials and `clientId` | Identify and authenticate this installation | `IClientStorage` / `ClientStateService` |
| Known Host records | Describe established Host relationships | `IClientStorage` / `ClientStateService` |
| Live state | The current admitted session's synchronized values | The SDK's typed domain trackers; runtime only |
| Historical snapshots | Previously accepted Host-authoritative observations | A dedicated SDK storage boundary |

Historical snapshots get their own storage boundary behind the platform-port rule in
`ai/context/sdk/architecture.md`. They are not an extension of `IClientStorage` or
`PersistedClientState`, which are fail-closed credential and trust storage with different recovery,
write-rate, and deletion needs. Credential and trust persistence is unchanged by this capability.

### Identity and coherence

- A snapshot is associated with an authoritative Known Host identity (`hostId`), never solely with a
  mutable endpoint or display name, and is shown only for the matching Host.
- A snapshot is coherent: every domain value in it comes from one `(stateAuthorityId, playContextId)`
  pair. When that pair changes, the Host's snapshot is replaced as a whole; a domain not yet
  baselined in the new context is absent, not carried over from the old one. A write still in flight
  for a superseded pair must not be committed.
- `playContextId` is runtime identity that the Adapter issues for every load. It is recorded as
  provenance only and is never a character or save identity.

### Capture, durability, and failure isolation

- The capability records trustworthy observations **while the session is live**. It must not depend
  on a disconnect, teardown, or shutdown hook, and must not read values back from live trackers after
  they reset, so unexpected termination does not lose everything. Saving whatever values happen to
  remain at disconnect is not an acceptable design.
- Only values the SDK has already accepted through the existing state gate, carrying their real
  identity and revision, are eligible. Values that failed validation, `notSubscribed`/`unavailable`
  placeholders, and values read back from history are never recorded.
- A commit writes a complete, versioned record durably and atomically, so a crash leaves the previous
  valid record or the new one, never a partial mix.
- Historical persistence never writes into the live trackers or `StateRevisionTracker`. A read or
  write failure never fails connection, synchronization, or the live Overview; it surfaces as a typed,
  non-fatal condition.
- The format is versioned under the rules in "Versioning and migration" and
  `ai/context/common.md`'s "Persisted-format migrations".

### Retention and deletion

One last-known Overview per Known Host is kept through disconnect, `close()`, restart, credential
removal (`forgetCredential`), administrative invalidation, and terminal recovery rejection. Deletion
is attached only to an operation the player actually requested; credential or trust handling must not
erase history as a side effect. Known Host removal and erase-all-local-data operations do not exist
today and are not defined here; each needs its own approved SDK task that invokes history deletion.

### Open decisions

These are recorded in the audit (Section 11.5) and are **not** decided by this document; do not treat
them as requirements:

- Whether an unreadable or unknown-version historical store is left untouched and shown as empty, or
  discarded and rewritten.
- The stored encoding (reuse each area's validated protocol `data` with its existing decoder, or add
  model encoders).
- The quiet period and the maximum staleness bound for writes.
- Whether snapshots need encryption beyond per-user storage.
- The storage technology (none is assumed; the smallest durable store meeting these rules is
  preferred).
- The name and signature of the history-clear operation, and whether revocation or block should ever
  delete history (a security-deletion policy).
