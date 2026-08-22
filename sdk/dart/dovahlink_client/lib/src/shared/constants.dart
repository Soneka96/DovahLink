import 'enums.dart';

// ---- Request policy ----

/// The bounded wait this SDK allows a request of each [TimeoutClass] before treating its
/// connection as unhealthy. Centralized so every call site shares the same tuned policy rather
/// than choosing an arbitrary literal; see `ai/context/sdk/api-design.md`'s "Request retry
/// safety, session requirement, and timeout class". These are initial values for this local,
/// same-machine/LAN connection, not yet tuned against real-world latency data.
const Map<TimeoutClass, Duration> kTimeoutClassDurations =
    <TimeoutClass, Duration>{
      TimeoutClass.short: Duration(seconds: 5),
      TimeoutClass.normal: Duration(seconds: 15),
      TimeoutClass.heavy: Duration(seconds: 30),
    };
