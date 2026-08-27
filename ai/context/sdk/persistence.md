# SDK persistence and caching

## Ownership rule

If persisted data is required for correct reusable DovahLink client behavior, the SDK owns it. If
persisted data exists only for the official product experience, the app owns it.

SDK-owned persistence includes: the stable local `clientId`, the client credential, pairing
recovery state, reusable known-Bridge information required by approved connection semantics,
reusable resource/cache metadata, cache-format version, and SDK persistence-format version.
App-owned persistence includes product/UI preferences such as preferred Bridge selection, dashboard
layout, map zoom, selected marker, and UI filters.

Administrative invalidation reasons are not persisted as authoritative trust state: `blocked`,
`revoked`, `trustReset`, and `factoryReset` may be exposed in the current SDK lifecycle state but
must be re-established from the Bridge after an application restart. When an authoritative device
credential invalidation is received, the SDK removes the obsolete local credential while preserving
the stable local `clientId`; a Factory Reset ending a developer-token session does not delete the
configured developer token.

The app must not persist a competing authoritative copy of SDK-owned protocol or client state: not
the client credential, not pairing `CONFIRMING` recovery state, not actual subscription state, not
reconnect state, not authoritative revision/recovery state, not SDK cache-validity metadata, and not
a duplicated copy of trusted-device authority.

## Storage abstraction

SDK persistence sits behind explicit storage boundaries rather than scattered direct filesystem or
secure-storage calls throughout its state machines. Platform-specific storage facilities (secure
credential storage, cache/filesystem location) stay behind the platform ports defined in
`ai/context/sdk/architecture.md`.

The `IClientStorage` interface implements this boundary for `clientId`, credential, and pairing
recovery state (`sdk/dart/dovahlink_client/lib/src/persistence/client_storage.dart`); its Windows
implementation, `DpapiClientStorage`, is the platform port this section describes, using DPAPI in
the per-user scope `ai/context/protocol/security.md` requires and failing closed on corrupt or
undecryptable state rather than substituting a plausible default.

## Versioning and migration

Persisted SDK formats are versioned. The SDK that owns a persistent format owns its migrations; the
official application must never need to understand or migrate the SDK's private persistence schema.

`PersistedClientState.currentFormatVersion` is the concrete version field this section describes for
`clientId`/credential/recovery state; `DpapiClientStorage` throws `DovahLinkStorageException` on an
unrecognized version rather than guessing a migration, since no migration exists yet.

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
