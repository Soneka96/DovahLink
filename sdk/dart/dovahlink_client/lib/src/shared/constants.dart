import 'package:dovahlink_client_sdk/src/shared/enums.dart';

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

// ---- Reconnect policy ----

/// The delay before each bounded-reconnect attempt after ordinary transport loss, in order; the
/// first attempt fires immediately. Bounded to this many attempts total, and further bounded by
/// [kReconnectDeadline] overall -- whichever runs out first stops recovery. These are initial
/// values for this local, same-machine/LAN connection, not yet tuned against real-world data.
const List<Duration> kReconnectAttemptDelays = <Duration>[
  Duration.zero,
  Duration(seconds: 1),
  Duration(seconds: 2),
  Duration(seconds: 4),
];

/// The hard overall deadline for one bounded-reconnect cycle, measured from the first attempt and
/// including connection and re-authentication time. Recovery stops once this elapses even if
/// [kReconnectAttemptDelays] has attempts remaining.
const Duration kReconnectDeadline = Duration(seconds: 10);

// ---- Transport ----

/// The bounded wait allowed for one transport `connect()` attempt before it is treated as failed
/// and abandoned, so a socket handshake that never completes cannot block `SessionService.connect`
/// -- and, in turn, `ReconnectService`'s own bounded-recovery deadline -- indefinitely. An initial
/// value for this local, same-machine/LAN connection, not yet tuned against real-world latency
/// data.
const Duration kConnectTimeout = Duration(seconds: 5);
