import 'package:flutter/material.dart';

import 'package:dovahlink_client/features/connection/domain/entities/host.entity.dart';
import 'package:dovahlink_client/features/pairing/domain/entities/pairing_handshake.entity.dart';
import 'package:dovahlink_client/shared/constants/constants.dart';
import 'package:dovahlink_client/shared/constants/enums.dart';
import 'package:dovahlink_client/shared/theme/dovah_theme_tokens.dart';

/// Central test-owned catalog of representative Flutter app values.
abstract final class Fixtures {
  // ---- Connection ----

  /// Builds a Host identity with the representative local endpoint.
  static HostEntity buildHostEntity({
    /// The user-facing Host name.
    String displayName = 'Local Host',

    /// The Host endpoint, or the representative local endpoint when omitted.
    Uri? uri,
  }) => HostEntity(displayName: displayName, uri: uri ?? defaultHostUri);

  // ---- Pairing ----

  /// Builds a pairing handshake with representative trusted-session defaults.
  static PairingHandshakeEntity buildPairingHandshakeEntity({
    /// The Host's own release version reported by the handshake.
    String hostVersion = '1.2.3',

    /// Whether the session already holds a trusted credential.
    bool trusted = true,

    /// The user-safe explanation for a rejected credential, when applicable.
    String? credentialRejectedMessage,
  }) => PairingHandshakeEntity(
    hostVersion: hostVersion,
    trusted: trusted,
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
    Gradient primaryActionGradient = const LinearGradient(
      begin: Alignment.topLeft,
      end: Alignment.bottomRight,
      colors: [Color(0xFFF0BD73), Color(0xFFC77D38)],
    ),
    Color primaryActionForeground = const Color(0xFF1A0E04),

    /// The low-opacity focus halo color.
    Color soft = const Color(0x2174BDE8),
    Color health = const Color(0xFFD16F62),
    Color magicka = const Color(0xFF65B8E7),
    Color stamina = const Color(0xFF78A984),
    DovahPanelCornerStyle cornerStyle = DovahPanelCornerStyle.doubleBevel,
    double cornerRadius = 3,
    double cornerCutSize = 12,
    List<BoxShadow>? panelShadow,
    Gradient? materialGradient,
    Gradient? materialRaisedGradient,
    double densityScale = 1,
    String displayFontFamily = 'Georgia',
    String? environmentAssetPath,
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
    primaryActionGradient: primaryActionGradient,
    primaryActionForeground: primaryActionForeground,
    soft: soft,
    health: health,
    magicka: magicka,
    stamina: stamina,
    cornerStyle: cornerStyle,
    cornerRadius: cornerRadius,
    cornerCutSize: cornerCutSize,
    panelShadow:
        panelShadow ??
        const [
          BoxShadow(
            color: Color(0x4A000000),
            blurRadius: 38,
            offset: Offset(0, 17),
          ),
        ],
    materialGradient:
        materialGradient ??
        const LinearGradient(
          begin: Alignment.topLeft,
          end: Alignment.bottomRight,
          colors: [Color(0xFF11212D), Color(0xFF071018)],
        ),
    materialRaisedGradient:
        materialRaisedGradient ??
        const LinearGradient(
          begin: Alignment.topLeft,
          end: Alignment.bottomRight,
          colors: [Color(0xFF162A38), Color(0xFF09141D)],
        ),
    densityScale: densityScale,
    displayFontFamily: displayFontFamily,
    environmentAssetPath: environmentAssetPath,
  );
}
