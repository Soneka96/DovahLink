/// Every small cross-cutting constant value in the app lives here, regardless of which feature
/// uses it, grouped by the area it belongs to.
library;

// ---- Bridges ----

/// The Phase 1 hardcoded default Bridge endpoint (`bridge/README.md`'s "Default loopback port").
/// The sole entry in the static Bridge list until Bridge discovery exists.
final Uri defaultBridgeUri = Uri.parse('ws://127.0.0.1:58231/');
