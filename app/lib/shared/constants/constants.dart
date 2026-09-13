/// Every small cross-cutting constant value in the app lives here, regardless of which feature
/// uses it, grouped by the area it belongs to.
library;

// ---- Hosts ----

/// The sole entry in the static Host list until Host discovery exists (matches the retired
/// Bridge component's original default loopback port).
final Uri defaultHostUri = Uri.parse('ws://127.0.0.1:58231/');
