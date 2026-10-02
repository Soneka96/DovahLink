import 'package:flutter_test/flutter_test.dart';

import 'package:dovahlink_client/shared/navigation/app_routes.dart';

/// Exercises centralized application route locations.
void main() {
  group('Property session behaves correctly', () {
    test('sessionFor encodes a Host ID into the approved session route', () {
      expect(AppRoutes.session, '/session/:hostId');
      expect(AppRoutes.sessionFor('host/id'), '/session/host%2Fid');
    });
  });
}
