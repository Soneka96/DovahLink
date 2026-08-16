/// A decoded JSON object. Deliberately duplicated from
/// `features/connection/data/models/json_map.dart` rather than imported: this client library is
/// self-contained and independent of any Flutter feature, which is expected to depend on it later,
/// not the reverse.
typedef JsonMap = Map<String, dynamic>;
