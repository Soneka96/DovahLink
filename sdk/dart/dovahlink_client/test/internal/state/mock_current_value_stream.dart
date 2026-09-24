import 'package:mocktail/mocktail.dart';

import 'package:dovahlink_client_sdk/src/shared/current_value_stream.dart';

/// A contract double for the revision tracker's current-state stream dependency.
class MockCurrentValueStream<T> extends Mock implements CurrentValueStream<T> {}
