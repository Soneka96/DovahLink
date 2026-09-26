# Security feasibility spikes

These experiments are isolated from production Host, SDK, App, and protocol paths. They create no
production identities or trust state.

## Host WSS spike

Run from the repository root after restoring/building the spike:

```powershell
dotnet build tooling/security_feasibility/host_poc/host_poc.csproj --configuration Release
Push-Location tooling/security_feasibility/client_poc
try { dart pub get; dart analyze } finally { Pop-Location }
pwsh -NoProfile -NonInteractive -File tooling/security_feasibility/run-host-poc.tests.ps1
pwsh -NoProfile -NonInteractive -File tooling/security_feasibility/run-host-poc.ps1
pwsh -NoProfile -NonInteractive -File tooling/security_feasibility/run-client-key-poc.ps1
```

The Windows Host creates a one-day self-signed certificate from a unique per-user Microsoft Software
KSP ECDSA P-256 key with export disabled. It binds only to loopback, permits TLS 1.3, and serves one
single-frame WebSocket message through Kestrel. The Dart client uses `dart:io`'s WSS client with an
empty trust store, pins
`SHA-256(SubjectPublicKeyInfo DER)` using canonical unpadded base64url, and checks that a wrong pin
fails before a WebSocket message is exchanged, regardless of system CA trust. The Host also renews
the certificate around the same key in memory and checks that the SPKI stays unchanged while the
certificate thumbprint changes. The runner removes only its randomly named POC CNG key after the
Host process exits. The committed client source is `client_poc/lib/client.dart`; the runner invokes
that path so the repository-wide `**/bin/` ignore rule does not hide it.

This is a feasibility test, not a production implementation. The Dart spike uses `asn1lib` only to
extract SPKI from the DER certificate that `dart:io` supplies. The package parses general ASN.1 and
is not yet approved for production pin validation; S2 must assess its maintenance/security posture or
a platform-backed alternative. The CNG test proves non-exportability through the software KSP, not
hardware protection, at-rest key ACLs, or behavior under the packaged Host's eventual Windows
identity. The server intentionally accepts only one unfragmented text frame; production framing and
input limits remain governed by the existing transport contract. No final stack choice is made here.

## Current production baseline audited for S2

- The Host targets `net9.0-windows`; its public listener currently binds IPv4 and IPv6 loopback TCP
  sockets and upgrades WebSocket traffic without TLS.
- Host trust is a DPAPI-protected JSON trust store. Authentication currently admits the developer
  token, unpaired pairing path, or persisted bearer credential.
- The Dart SDK declares Dart `^3.12.2`. Its production `IDovahLinkTransport` is injectable, and
  `WebSocketTransport` uses the standard `dart:io` WebSocket API without custom certificate
  validation or a secure-storage key-operations port.
- Client persistence currently stores one `knownHost`, a `clientId`, a bearer credential, and
  pairing recovery state. Windows uses DPAPI; the App selects unsupported storage on other
  platforms. The app has a Windows desktop target; `app/android/` has no Gradle project, and there
  is no `app/ios/` target. The Windows environment has Android SDK tooling but no connected device,
  and no Xcode toolchain.
- The current protocol still defines `trusted_device_credential` and the existing pairing messages.
  S1 explicitly keeps that wire schema and production behavior until later migration slices.

## Evidence

- [.NET `SslProtocols`](https://learn.microsoft.com/dotnet/api/system.security.authentication.sslprotocols)
- [Kestrel HTTPS configuration](https://learn.microsoft.com/aspnet/core/fundamentals/servers/kestrel)
- [CNG key creation parameters](https://learn.microsoft.com/dotnet/api/system.security.cryptography.cngkeycreationparameters)
- [Non-exportable CNG export policy](https://learn.microsoft.com/dotnet/api/system.security.cryptography.cngexportpolicies)
- [Dart `WebSocket.connect`](https://api.dart.dev/dart-io/WebSocket/connect.html)
- [Dart `HttpClient.badCertificateCallback`](https://api.dart.dev/dart-io/HttpClient/badCertificateCallback.html)
- [Dart `X509Certificate.der`](https://api.dart.dev/dart-io/X509Certificate/der.html)
- [Dart `SecurityContext`](https://api.dart.dev/dart-io/SecurityContext/SecurityContext.html)
- [Dart `SecureSocket`](https://api.dart.dev/dart-io/SecureSocket-class.html)
- [Android `KeyGenParameterSpec`](https://developer.android.com/reference/android/security/keystore/KeyGenParameterSpec)
- [Android hardware key attestation](https://developer.android.com/privacy-and-security/security-key-attestation)
- [Apple Secure Enclave key protection](https://developer.apple.com/documentation/security/protecting-keys-with-the-secure-enclave)
- [Apple permanent key generation](https://developer.apple.com/documentation/security/generating-new-cryptographic-keys)
- [Flutter `MethodChannel`](https://api.flutter.dev/flutter/services/MethodChannel-class.html)

## Client key operations spike

Run after building the Host project above:

```powershell
pwsh -NoProfile -NonInteractive -File tooling/security_feasibility/run-client-key-poc.ps1
```

This uses a unique software CNG ECDSA P-256 key, closes it, reopens it by key name, signs a
POC-only domain-separated transcript containing a fresh 32-byte challenge and test Host/Client IDs,
then verifies it with the exported SPKI public key. A local verifier simulation consumes the
challenge once and rejects duplicate use and the captured signature against a different challenge.
This proves the key-operation flow, not production Host admission or replay-state storage. The
temporary key is deleted when the check exits. The transcript format is not a proposed protocol
contract.

Platform feasibility evidence:

- **Android:** `KeyGenParameterSpec` + `AndroidKeyStore` support non-exportable P-256 signing keys;
  the framework can return the public key from the associated certificate. Hardware-backed
  protection varies by device and is detectable through key characteristics/attestation, not an
  assumption. No production Android project or connected test device exists in this checkout.
- **iOS:** Apple `SecKey` can create persistent Keychain keys and Secure Enclave P-256 keys, retrieve
  their public keys, and sign without exporting private key material. Locked-device/background
  calls can fail with `errSecInteractionNotAllowed`; callers must surface that as an operation
  failure and retry only when allowed. No iOS target or Xcode is available here.
- **Flutter/Dart:** `MethodChannel` supports a Dart-facing native method boundary. The shared SDK
  can own a small client-key operations port while Android/iOS/Windows adapters perform key creation,
  public-key export, and signing. No such production port is added in S2.
- **Authentication direction:** app-level fresh challenge/signature over the already pinned TLS
  connection fits these non-exportable keys. Dart's TLS `SecurityContext` loads certificate and key
  bytes, so using them for client-certificate mTLS would require a platform TLS integration that this
  reusable SDK does not currently have. Step 4 will record the final comparison and selection.
