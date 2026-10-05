import 'package:flutter/material.dart';

import 'package:dovahlink_client/features/connection/domain/entities/host.entity.dart';
import 'package:dovahlink_client/features/connection/domain/entities/known_host.entity.dart';
import 'package:dovahlink_client/features/connection/presentation/viewdata/host_card.viewdata.dart';
import 'package:dovahlink_client/features/pairing/data/models/pairing_handshake.model.dart';
import 'package:dovahlink_client/features/pairing/domain/entities/pairing_handshake.entity.dart';
import 'package:dovahlink_client/features/pairing/domain/entities/pairing_renotify_result.entity.dart';
import 'package:dovahlink_client/features/pairing/domain/usecases/params/authenticate.params.dart';
import 'package:dovahlink_client/shared/constants/constants.dart';
import 'package:dovahlink_client/shared/constants/enums.dart';
import 'package:dovahlink_client/shared/theme/dovah_theme_tokens.dart';

import 'package:dovahlink_client_sdk/dovahlink_client.dart'
    show
        CredentialRejectionReason,
        DovahLinkHost,
        DovahLinkHostAvailability,
        DovahLinkKnownHostSessionState,
        DovahLinkKnownHostState,
        DovahLinkTrustState,
        HelloResult;

/// Central test-owned catalog of representative Flutter app values.
abstract final class Fixtures {
  // ---- Connection ----

  /// Builds a Host identity with the representative local endpoint.
  static Host buildHost({
    /// The stable Host installation identity.
    String hostId = '81869993-955c-4ba3-a7d0-d35ca86078ea',

    /// The user-facing Host name.
    String displayName = 'Local Host',

    /// The Host endpoint, or the representative local endpoint when omitted.
    Uri? uri,
  }) => Host(
    hostId: hostId,
    displayName: displayName,
    uri: uri ?? defaultHostUri,
  );

  /// Builds a Known Host with representative metadata and unknown availability by default.
  static KnownHost buildKnownHost({
    /// The Host metadata, or the representative local Host when omitted.
    Host? host,

    /// The runtime reachability evidence.
    HostAvailability availability = HostAvailability.unknown,

    /// The session lifecycle for this Known Host.
    KnownHostSessionState sessionState = KnownHostSessionState.disconnected,

    /// Whether the last-known Host response requires pairing again.
    bool pairingRequired = false,
  }) => KnownHost(
    host: host ?? buildHost(),
    availability: availability,
    sessionState: sessionState,
    pairingRequired: pairingRequired,
  );

  /// Builds a Host card's display data for the representative local Host.
  static HostCardViewData buildHostCardViewData({
    /// The Host the card selects, or the representative Host when omitted.
    Host? host,

    /// The semantic selection intent represented by the card.
    ConnectionHostSelectionSource source =
        ConnectionHostSelectionSource.candidate,

    /// The card's primary line.
    String title = 'Local Host',

    /// The card's secondary line.
    String subtitle = 'Discovered candidate',

    /// The card's trailing detail.
    String detail = '127.0.0.1:58231',

    /// The card's visual state.
    DovahConnectionCardState state = DovahConnectionCardState.unknown,

    /// Whether the Known Host's saved SDK hint says pairing is required.
    bool pairingRequired = false,
  }) => HostCardViewData(
    host: host ?? buildHost(),
    source: source,
    title: title,
    subtitle: subtitle,
    detail: detail,
    state: state,
    pairingRequired: pairingRequired,
  );

  /// Builds an SDK Known Host state around [host].
  static DovahLinkKnownHostState buildSdkKnownHostState({
    /// The SDK Host metadata.
    required DovahLinkHost host,

    /// The SDK-reported runtime reachability evidence.
    DovahLinkHostAvailability availability = DovahLinkHostAvailability.unknown,

    /// The SDK-reported session lifecycle for this exact relationship.
    DovahLinkKnownHostSessionState sessionState =
        DovahLinkKnownHostSessionState.disconnected,

    /// Whether the SDK's last-known Host response requires pairing again.
    bool pairingRequired = false,
  }) => DovahLinkKnownHostState(
    host: host,
    availability: availability,
    sessionState: sessionState,
    pairingRequired: pairingRequired,
  );

  // ---- Pairing ----

  /// Builds authentication parameters targeting the representative local Host.
  static AuthenticateParams buildAuthenticateParams({
    /// The Host endpoint to authenticate with, or the representative local endpoint when omitted.
    Uri? hostUri,

    /// The stable Known Host ID to authenticate with instead of the endpoint.
    String? hostId,
  }) => hostId == null
      ? AuthenticateParams(hostUri: hostUri ?? defaultHostUri)
      : AuthenticateParams.knownHost(hostId: hostId);

  /// Builds the SDK handshake value consumed by pairing tests.
  /// @param hostId The stable Host installation identity.
  /// @param hostName The representative OS-derived computer name.
  /// @param hostVersion The Host release version.
  /// @param trustState The trust tier reported by the SDK.
  /// @param recoveredFromRejectedCredential The rejected stored credential reason, when present.
  static HelloResult buildSdkHelloResult({
    String hostId = '81869993-955c-4ba3-a7d0-d35ca86078ea',
    String hostName = 'Soneka-Desktop',
    String hostVersion = '1.2.3',
    DovahLinkTrustState trustState = DovahLinkTrustState.trusted,
    CredentialRejectionReason? recoveredFromRejectedCredential,
  }) => HelloResult(
    hostId: hostId,
    hostName: hostName,
    hostVersion: hostVersion,
    trustState: trustState,
    recoveredFromRejectedCredential: recoveredFromRejectedCredential,
  );

  /// Builds a pairing handshake with representative trusted-session defaults.
  static PairingHandshake buildPairingHandshake({
    /// The Host's own release version reported by the handshake.
    String hostVersion = '1.2.3',

    /// Whether the session already holds a trusted credential.
    bool trusted = true,

    /// The Host's typed rejection reason, when it rejected a stored credential.
    PairingCredentialRejectionReason? credentialRejectionReason,

    /// The user-safe explanation for a rejected credential, when applicable.
    String? credentialRejectedMessage,
  }) => PairingHandshake(
    hostVersion: hostVersion,
    trusted: trusted,
    credentialRejectionReason: credentialRejectionReason,
    credentialRejectedMessage: credentialRejectedMessage,
  );

  /// Builds a typed pairing-code redisplay result.
  static PairingRenotifyResult buildPairingRenotifyResult({
    /// The Host response to the redisplay request.
    PairingRenotifyOutcome outcome = PairingRenotifyOutcome.renotified,

    /// Host-reported retry interval, or `null` when absent.
    int? retryAfterSeconds = 5,
  }) => PairingRenotifyResult(
    outcome: outcome,
    retryAfterSeconds: retryAfterSeconds,
  );

  /// Builds a data-layer pairing handshake with representative trusted-session defaults.
  static PairingHandshakeModel buildPairingHandshakeModel({
    /// The Host's own release version reported by the handshake.
    String hostVersion = '1.2.3',

    /// Whether the session already holds a trusted credential.
    bool trusted = true,

    /// The Host's typed rejection reason, when it rejected a stored credential.
    PairingCredentialRejectionReason? credentialRejectionReason,

    /// The user-safe explanation for a rejected credential, when applicable.
    String? credentialRejectedMessage,
  }) => PairingHandshakeModel(
    hostVersion: hostVersion,
    trusted: trusted,
    credentialRejectionReason: credentialRejectionReason,
    credentialRejectedMessage: credentialRejectedMessage,
  );

  // ---- Theme ----

  /// Builds a complete [DovahThemeTokens] with representative defaults (matching the Dovah
  /// preset's real values), overridable per field for a test's specific scenario.
  static DovahThemeTokens buildDovahThemeTokens({
    Color background = const Color(0xFF05090E),
    Color surface = const Color(0xFF0B141D),
    Color surfaceRaised = const Color(0xFF101D28),

    /// The strongest flat surface tone.
    Color surface3 = const Color(0xFF162735),
    Color lineSubtle = const Color(0xFF294052),
    Color lineStrong = const Color(0xFF4A6B84),
    Color textPrimary = const Color(0xFFF1F6F9),
    Color textMuted = const Color(0xFF9AABB7),
    Color textFaint = const Color(0xFF667C8B),
    Color accentPrimary = const Color(0xFF8ED6FF),
    Color accentSecondary = const Color(0xFF54AEE0),
    Color signal = const Color(0xFF74BDE8),
    Color ember = const Color(0xFFE2A55E),
    Color success = const Color(0xFF8ED6FF),
    Color warning = const Color(0xFFE2A55E),
    Color danger = const Color(0xFFE18080),
    Color primaryActionForeground = const Color(0xFF1A0E04),

    /// The low-opacity focus halo color.
    Color soft = const Color(0x2174BDE8),
    Color health = const Color(0xFFD16F62),
    Color magicka = const Color(0xFF65B8E7),
    Color stamina = const Color(0xFF78A984),
    DovahPanelCornerStyle cornerStyle = DovahPanelCornerStyle.doubleBevel,
    double cornerRadius = 3,
    double cornerCutSize = 12,
    String displayFontFamily = 'Georgia',
    List<String> displayFontFamilyFallback = const ['Times New Roman'],
    Color eyebrow = const Color(0xFFE2A55E),
    bool uppercaseLabels = false,
    double rootHeaderRuleFraction = 0.36,
    double pageTitleLineHeight = 1.14,
    DovahThemePreset preset = DovahThemePreset.dovah,
    double panelCornerRadius = 0,
    double primaryActionCornerRadius = 0,
    Color statusOffline = const Color(0xFF7C8993),
    Color brandTagline = const Color(0xFF72899A),
    Color brandAccent = const Color(0xFF74BDE8),
    Color markIcon = const Color(0xFFE2A55E),
    Color iconTileForeground = const Color(0xFF8ED6FF),
    Color barTrack = const Color(0xFF202B34),
    Color panelNote = const Color(0xFF667C8B),
    Gradient? heroScrim,
    Gradient? heroFloorScrim,
  }) => DovahThemeTokens(
    background: background,
    surface: surface,
    surfaceRaised: surfaceRaised,
    surface3: surface3,
    lineSubtle: lineSubtle,
    lineStrong: lineStrong,
    textPrimary: textPrimary,
    textMuted: textMuted,
    textFaint: textFaint,
    accentPrimary: accentPrimary,
    accentSecondary: accentSecondary,
    signal: signal,
    ember: ember,
    success: success,
    warning: warning,
    danger: danger,
    primaryActionForeground: primaryActionForeground,
    soft: soft,
    health: health,
    magicka: magicka,
    stamina: stamina,
    cornerStyle: cornerStyle,
    cornerRadius: cornerRadius,
    cornerCutSize: cornerCutSize,
    displayFontFamily: displayFontFamily,
    displayFontFamilyFallback: displayFontFamilyFallback,
    eyebrow: eyebrow,
    uppercaseLabels: uppercaseLabels,
    rootHeaderRuleFraction: rootHeaderRuleFraction,
    pageTitleLineHeight: pageTitleLineHeight,
    preset: preset,
    panelCornerRadius: panelCornerRadius,
    primaryActionCornerRadius: primaryActionCornerRadius,
    statusOffline: statusOffline,
    brandTagline: brandTagline,
    brandAccent: brandAccent,
    markIcon: markIcon,
    iconTileForeground: iconTileForeground,
    barTrack: barTrack,
    panelNote: panelNote,
    heroScrim:
        heroScrim ??
        const LinearGradient(
          begin: Alignment.centerLeft,
          end: Alignment.centerRight,
          colors: [Color(0xE0050A0F), Color(0x5C050A0F), Color(0x0F050A0F)],
          stops: [0, 0.52, 1],
        ),
    heroFloorScrim:
        heroFloorScrim ??
        const LinearGradient(
          begin: Alignment.bottomCenter,
          end: Alignment.topCenter,
          colors: [Color(0xE00B141D), Color(0x000B141D)],
          stops: [0, 0.72],
        ),
  );
}
