Scriptname DovahLinkAdmin Hidden

; Declares the native functions DovahLink Bridge registers via SKSE's Papyrus interface
; (bridge/game_state/commonlib_trust_admin_papyrus_adapter.cpp). This script carries no logic of
; its own -- every function below is implemented natively by the bridge plugin, not in Papyrus.
; See ../README.md and ai/context/protocol/security.md's "Trust administration surface".

; Lists known devices for an empty/all, trust, or block scope as a formatted, multi-line string.
String Function List(String akScope) global native

; Shows the canonical trust-administration commands and their descriptions.
String Function Help() global native

; Revokes the trusted client identified by its five-digit administration-only shortId.
String Function Revoke(String akId) global native

; Starts the Factory Reset confirmation challenge: a six-digit code, printed in Skyrim, expiring
; after 60 seconds. Performs no mutation itself; confirm with ConfirmReset.
String Function Reset() global native

; Blocks the trusted or revoked known device identified by its five-digit administration-only
; shortId. An unpaired device is not eligible.
String Function Block(String akId) global native

; Unblocks the known device identified by its five-digit administration-only shortId.
String Function Unblock(String akId) global native

; Forgets the known device identified by its five-digit administration-only shortId, deleting its
; record entirely. Only eligible from Revoked or Unpaired.
String Function Forget(String akId) global native

; Confirms a pending Factory Reset challenge with its six-digit code, permanently erasing every
; known device record and revocation tombstone and invalidating every session, including
; developer-token ones. A wrong code cancels the challenge; start over with Reset.
String Function ConfirmReset(String akCode) global native

; The recoverable, non-destructive bulk revoke: converts every currently trusted device to
; revoked, cancels any pending pairing, and disconnects each affected session. Devices remain
; known and can re-pair. Requires no confirmation code, unlike Reset/ConfirmReset.
String Function ResetTrust() global native
