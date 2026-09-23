import 'package:mocktail/mocktail.dart';

import 'package:dovahlink_client_sdk/src/internal/session/session_service.dart';

/// A contract double for state-recovery lifecycle notifications.
class MockSessionService extends Mock implements ISessionService {}
