import 'package:mocktail/mocktail.dart';

import 'package:dovahlink_client_sdk/src/internal/readiness/gameplay_readiness_service.dart';

/// A contract double for consumers of the gameplay-readiness owner.
class MockGameplayReadinessService extends Mock
    implements IGameplayReadinessService {}
