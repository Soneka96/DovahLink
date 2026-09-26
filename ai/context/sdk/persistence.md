# SDK persistence and caching

## Ownership rule

If persisted data is required for correct reusable DovahLink client behavior, the SDK owns it. If
persisted data exists only for the official product experience, the app owns it.

SDK-owned persistence includes: the stable local `clientId`, the client credential, pairing
recovery state, reusable known-Host information required by approved connection semantics, reusable
resource/cache metadata, cache-format version, and SDK persistence-format version.
App-owned persistence includes product/UI preferences such as preferred Host selection, dashboard
layout, map zoom, selected marker, and UI filters.

Administrative invalidation reasons are not persisted as authoritative trust state: `blocked`,
`revoked`, `trustReset`, and `factoryReset` may be exposed in the current SDK lifecycle state but
must be re-established from the Host after an application restart. When an authoritative device
credential invalidation is received, the SDK removes the obsolete local credential while preserving
the stable local `clientId` and Known Host metadata; a Factory Reset ending a developer-token session
does not delete the configured developer token.

Known Host metadata means the Host this client previously associated with, stored as `hostId`, the
last known `hostName`, and the last known `endpoint`. `hostId` is identity; the name and endpoint are
mutable metadata. This record is not authoritative trust state: the Host must establish current
trust on every session, and no trusted/connected/offline/blocked/revoked status is persisted. A
discovery claim or unpaired `hello_ack` alone never writes Known Host metadata. When a Host issues a
pairing credential, successful code confirmation atomically persists that credential, the Host
that issued it, and `confirming` recovery state. This becomes the client's most recent durable Host
association immediately; if final `pairing_ack` later reports `pending_not_found` or
`pairing_invalidated`, the SDK clears the incomplete credential and recovery state but keeps that
new Known Host. It never restores an older Host association. A successfully trusted session may
bind an unbound legacy credential or refresh name/endpoint metadata only when its Host ID matches
the stored ID. During pending pairing recovery, a different reported Host ID fails before session
admission, preserving the pending credential and Known Host. A mismatch raises a typed SDK error
and leaves the stored Host unchanged. Credential removal and failed pending-pairing recovery
preserve Known Host metadata; none of this metadata establishes current trust.

The app must not persist a competing authoritative copy of SDK-owned protocol or client state: not
the client credential, not pairing `CONFIRMING` recovery state, not actual subscription state, not
reconnect state, not authoritative revision/recovery state, not SDK cache-validity metadata, and not
a duplicated copy of trusted-device authority.

## Storage abstraction

SDK persistence sits behind explicit storage boundaries rather than scattered direct filesystem or
secure-storage calls throughout its state machines. Platform-specific storage facilities (secure
credential storage, cache/filesystem location) stay behind the platform ports defined in
`ai/context/sdk/architecture.md`.

The `IClientStorage` interface implements this boundary for `clientId`, credential, pairing
recovery state, and Known Host metadata
(`sdk/dart/dovahlink_client/lib/src/persistence/client_storage.dart`); its Windows implementation,
`DpapiClientStorage`, is the platform port this section describes, using DPAPI in the per-user scope
`ai/context/protocol/security.md` requires and failing closed on corrupt or undecryptable state rather
than substituting a plausible default.

The standard SDK entry point does not expose or import Windows storage. Windows consumers import
`dovahlink_client_windows.dart` for `DpapiClientStorage` and inject it through `IClientStorage`.
Until secure storage is implemented on another platform, composition may use
`UnsupportedClientStorage`: constructing the client remains safe, while the first persistence
operation throws `UnsupportedError`. No plaintext or in-memory credential fallback is used.

## Versioning and migration

Persisted SDK formats are versioned. The SDK that owns a persistent format owns its migrations; the
official application must never need to understand or migrate the SDK's private persistence schema.

`PersistedClientState.currentFormatVersion` is the concrete version field this section describes for
SDK client state. Version 2 stores `knownHost` as a nested object containing `hostId`, `hostName`,
and `endpoint`, or `null` when no Host is known. Version 1 contains `clientId`, `credential`, and
`recoveryState` only; it migrates to v2 with those values preserved and `knownHost: null`. The SDK
does not fabricate a Host from an endpoint, computer name, or discovery result. A legacy state is
written as v2 on its next persistence mutation. Tests exercise the current SDK decoding legacy
stored data; they do not run an older SDK binary. Unknown future versions and malformed v2 Host
objects throw `DovahLinkStorageException`.

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
