import 'package:flutter_test/flutter_test.dart';

import 'package:dovahlink_client/features/session/presentation/state/session_shell.actions.dart';

/// Exercises value behavior for Session Shell navigation actions.
void main() {
  group(
    'Behavior equality in SessionShellBackRequestedAction behaves correctly',
    () {
      test('SessionShellBackRequestedAction instances are equal', () {
        expect(
          const SessionShellBackRequestedAction(),
          const SessionShellBackRequestedAction(),
        );
      });
    },
  );
}
