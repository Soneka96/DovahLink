/// Synchronization status retained for one projected live-state domain.
enum LiveStateStatus {
  /// The Host has not accepted this domain for the active session.
  notSubscribed,

  /// A synchronized baseline reported no available value.
  unavailable,

  /// The projected value agrees with the latest accepted Host revision.
  synchronized,

  /// A continuity break made the retained value unsafe to treat as current.
  stale,

  /// The SDK is waiting for an authoritative baseline.
  recovering,

  /// The SDK could not establish a trustworthy baseline.
  failed,
}

/// The kind of Skyrim cell containing the player.
enum LiveCellKind {
  /// The current cell is indoors.
  interior,

  /// The current cell is outdoors.
  exterior,
}

/// The state reported for one currently tracked quest objective instance.
enum LiveQuestObjectiveStatus {
  /// The objective is not currently displayed.
  dormant,

  /// The objective is currently displayed.
  displayed,

  /// The objective is complete.
  completed,

  /// The objective is complete and displayed.
  completedAndDisplayed,

  /// The objective failed.
  failed,

  /// The objective failed and is displayed.
  failedAndDisplayed,
}
