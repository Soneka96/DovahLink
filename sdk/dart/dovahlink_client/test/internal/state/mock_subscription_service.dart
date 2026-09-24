import 'package:mocktail/mocktail.dart';

import 'package:dovahlink_client_sdk/src/internal/state/subscription_service.dart';

/// A contract double for session-admission and pairing lifecycle tests.
class MockSubscriptionService extends Mock implements ISubscriptionService {}
