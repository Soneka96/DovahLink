Scriptname DovahLinkAdmin Hidden

; Declares the native functions DovahLink Bridge registers via SKSE's Papyrus interface
; (bridge/game_state/commonlib_trust_admin_papyrus_adapter.cpp). This script carries no logic of
; its own -- every function below is implemented natively by the bridge plugin, not in Papyrus.
; See ../README.md and ai/context/protocol/security.md's "Trust administration surface".

; Lists every currently trusted client as a formatted, multi-line string.
String Function List() global native

; Revokes the trusted client identified by its five-digit administration-only shortId.
String Function Revoke(String akId) global native

; Resets all persistent trust.
String Function Reset() global native
