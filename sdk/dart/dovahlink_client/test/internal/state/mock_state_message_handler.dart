import 'package:mocktail/mocktail.dart';

import 'package:dovahlink_client_sdk/src/internal/state/state_message_handler.dart';

/// A contract double for unsolicited state-message routing tests.
class MockStateMessageHandler extends Mock implements IStateMessageHandler {}
