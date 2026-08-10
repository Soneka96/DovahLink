# Protocol and transport security

Security rules apply before the bridge accepts any client connection. A local-network connection is not trusted merely because it is local.

## Phase 1 exposure

- The first connection proof binds to loopback only (`127.0.0.1` and `::1`). It must not listen on a LAN or wildcard address.
- The bridge must reject connections whose remote address is not loopback during this phase.
- The bridge requires a cryptographically random, one-time local connection token before accepting `hello`. The token expires after 5 minutes or first successful use, whichever comes first.
- Token validation and consumption are atomic: exactly one connection can successfully consume a token.
- Failed token attempts are globally limited to 5 per 60 seconds; further attempts are rejected until the window expires.
- The token is supplied out of band by the maintainer during development and is never committed, persisted in source, or sent to a non-loopback peer.
- LAN, Wi-Fi, or remote-device support is blocked until the maintainer approves a pairing and authentication design.
- A configuration value or command-line flag must not silently bypass the loopback restriction.

## LAN gate

Before LAN exposure is approved, the design must define and test:

- endpoint identity and trust establishment
- short-lived, user-approved pairing
- authenticated encryption using an established protocol/library
- replay protection and session binding
- authorization for each message category
- revocation and reset behavior
- behavior when pairing fails or expires

Do not invent cryptography or treat an unencrypted pairing code as authentication. A pairing code may bootstrap trust only through an approved authenticated channel.

## Input limits

The transport rejects input before application decoding when it exceeds the approved limits:

- maximum frame size: 1 MiB
- maximum decoded nesting depth: 32
- maximum string length: 4 KiB
- maximum array length: 128 items
- maximum object members: 64
- maximum inbound messages: 100 per second per client
- maximum messages in one session: 10,000; the bridge closes the session before this bound is exceeded
- maximum connected clients during the first proof: 1
- handshake timeout: 5 seconds
- idle connection timeout: 60 seconds without a valid heartbeat or message
- bounded outbound queue: 128 messages per client, with 16 reserved control/recovery slots and 112 event slots

Limit changes require explicit maintainer approval and a documented reason.

## Failure behavior

- Reject malformed, oversized, unauthenticated, unauthorized, expired, replayed, and unsupported messages without invoking game code.
- Close immediately when framing or size validation fails before a safe message can be decoded; do not attempt to send an error over an invalid or oversized frame.
- Close a connection after 3 protocol violations within 30 seconds; do not retry invalid input indefinitely.
- Never disclose pairing secrets, authentication material, filesystem paths, or raw infrastructure exceptions in protocol messages.
- Log security failures with enough context to investigate, but emit at most one matching failure log per peer per second.
- A security failure must not block game-state capture or prevent clean plugin shutdown.

## Secrets and logging

- Keep pairing secrets and credentials out of source control, protocol fixtures, crash reports, and normal logs.
- Redact tokens, keys, and full peer credentials from diagnostics.
- Store credentials only through an approved platform or project storage mechanism once one exists; do not invent persistence during the connection proof.

## Session and replay protection

- The bridge creates a fresh cryptographically random `sessionId` after successful token validation; the client never chooses it.
- The authenticated session is bound to the socket that completed token validation; another socket presenting the same `sessionId` is rejected.
- Every message in an active session must have a unique `messageId`. The bridge retains all seen IDs for the session; the 10,000-message session bound keeps this set bounded and prevents eviction-based replay.
- A message from an old session, an expired token, or an invalid connection attempt is rejected before application handling.
