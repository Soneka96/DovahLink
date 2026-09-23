import 'package:mocktail/mocktail.dart';

import 'package:dovahlink_client_sdk/src/internal/state/state_revision_tracker.dart';

/// A contract double for the state-recovery service's revision tracker dependency.
class MockStateRevisionTracker<T> extends Mock
    implements IStateRevisionTracker<T> {}
