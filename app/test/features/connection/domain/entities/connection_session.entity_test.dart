import 'package:flutter_test/flutter_test.dart';

import 'package:dovahlink_client/features/connection/domain/entities/connection_session.entity.dart';

void main() {
  test('stores the server-issued session identity and protocol version', () {
    const ConnectionSessionEntity session = ConnectionSessionEntity(
      sessionId: 'session-1',
      protocolVersion: 1,
    );

    expect(session.sessionId, 'session-1');
    expect(session.protocolVersion, 1);
  });
}
