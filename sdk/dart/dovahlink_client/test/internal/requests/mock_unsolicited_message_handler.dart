import 'package:mocktail/mocktail.dart';

import 'package:dovahlink_client_sdk/src/internal/requests/unsolicited_message_handler.dart';

/// A contract double for MessageRouter's unsolicited-message dependency.
class MockUnsolicitedMessageHandler extends Mock
    implements IUnsolicitedMessageHandler {}
