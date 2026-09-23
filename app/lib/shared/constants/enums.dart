/// Every enum in the app lives here, regardless of which feature uses it.
library;

/// Describes the current lifecycle of the local device pairing flow.
enum PairingPhase {
  /// Sentinel value indicating that no phase was selected.
  none,

  /// The client is opening a transport connection and authenticating.
  connecting,

  /// The client could not reach the host (a transport-level failure, not a
  /// rejected pairing attempt). Distinct from [failed] per
  /// `ai/context/flutter/architecture.md`'s "Connection and recovery state".
  disconnected,

  /// The session authenticated without a trusted credential; pairing has not
  /// been requested yet.
  unpaired,

  /// The client is asking the host to start or query a pairing challenge.
  requestingCode,

  /// A pairing challenge is active; the user may enter the code shown in
  /// Skyrim.
  awaitingCode,

  /// The client is submitting the entered code and completing the trust
  /// handshake.
  confirming,

  /// The client holds a trusted credential for this host.
  trusted,

  /// The most recent pairing attempt failed with a user-safe error message.
  failed;

  /// Returns the concise user-visible label for this phase.
  String get label => switch (this) {
    PairingPhase.none => 'Unknown',
    PairingPhase.connecting => 'Connecting',
    PairingPhase.disconnected => 'Waiting for host',
    PairingPhase.unpaired => 'Not paired',
    PairingPhase.requestingCode => 'Requesting code',
    PairingPhase.awaitingCode => 'Awaiting code',
    PairingPhase.confirming => 'Confirming',
    PairingPhase.trusted => 'Paired',
    PairingPhase.failed => 'Failed',
  };
}

/// The host connection's status while a trusted pairing session is active, observed from the
/// SDK's full `connectionStateChanges` feed rather than a narrower administrative-only slice.
enum PairingConnectionStatus {
  /// Ordinary transport loss; the SDK's own bounded recovery may still succeed without user
  /// action.
  lost,

  /// The connection recovered after [lost].
  restored,

  /// The host administratively ended this session (revoked, blocked, trust reset, or factory
  /// reset). Terminal for the current session; recovery is always an explicit user action.
  invalidated,
}

/// One of DovahLink's three first-class visual themes. Every value fully determines a concrete
/// theme, so the type always resolves to one of these and carries no unselected sentinel; see
/// `ai/context/flutter/dart-style.md`'s "Enums".
enum DovahThemePreset {
  /// Cold, severe, and compact: fractured stone and scratched iron.
  frostbound,

  /// The balanced DovahLink identity: midnight steel, ember, and ice.
  dovah,

  /// Warm, spacious, and storybook-like: parchment, walnut, and bronze.
  hearth;

  /// Returns the concise user-visible label for this preset.
  String get label => switch (this) {
    DovahThemePreset.frostbound => 'Frostbound',
    DovahThemePreset.dovah => 'Dovah',
    DovahThemePreset.hearth => 'Hearth',
  };
}

/// The visual state a `DovahConnectionCard` renders. Presentation-only: it does not derive from
/// real SDK/discovery/recovery state today (mapping that state onto these visuals remains future
/// roadmap work), so it stays distinct from any domain connection-status type. Every card that
/// exists shows exactly one of these, so the type carries no unselected sentinel; see
/// `ai/context/flutter/dart-style.md`'s "Enums".
enum DovahConnectionCardState {
  /// The connection is reachable and ready to enter.
  available,

  /// The connection was seen before but is not currently reachable.
  offline,

  /// Trust changed and the connection needs pairing again before use.
  repair;

  /// Returns the concise user-visible label for this state.
  String get label => switch (this) {
    DovahConnectionCardState.available => 'Connected',
    DovahConnectionCardState.offline => 'Offline',
    DovahConnectionCardState.repair => 'Pair again',
  };
}

/// The corner treatment a DovahLink theme applies to panels, surfaces, buttons, and cards.
/// Every theme resolves to exactly one style, so the type carries no unselected sentinel; see
/// `ai/context/flutter/dart-style.md`'s "Enums".
enum DovahPanelCornerStyle {
  /// One bevelled corner (top-right), sharp elsewhere, no rounding. Frostbound.
  singleBevel,

  /// Two bevelled corners on opposite edges, slight rounding. Dovah.
  doubleBevel,

  /// No bevel; plain rounded corners. Hearth.
  rounded;

  /// Returns the concise label for this corner treatment.
  String get label => switch (this) {
    DovahPanelCornerStyle.singleBevel => 'Single bevel',
    DovahPanelCornerStyle.doubleBevel => 'Double bevel',
    DovahPanelCornerStyle.rounded => 'Rounded',
  };
}

/// A [DovahButton]'s visual emphasis. Every button renders as exactly one of these, so the type
/// carries no unselected sentinel; see `ai/context/flutter/dart-style.md`'s "Enums".
enum DovahButtonVariant {
  /// The theme's high-emphasis action gradient (the approved prototype's `.primary`).
  primary,

  /// A bordered, low-emphasis surface (the approved prototype's `.secondary`).
  secondary;

  /// Returns the concise label for this variant.
  String get label => switch (this) {
    DovahButtonVariant.primary => 'Primary',
    DovahButtonVariant.secondary => 'Secondary',
  };
}
