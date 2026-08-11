import 'package:flutter_test/flutter_test.dart';

import 'package:dovahlink_client/features/connection/presentation/state/viewmodels/connection_status_screen.viewmodel.dart';
import 'package:dovahlink_client/injection_container.dart';

void main() {
  group('injection_container — shared registrations', () {
    test('initDependencies registers the connection ViewModel factory', () {
      initDependencies();

      expect(sl.isRegistered<ConnectionStatusScreenViewModel>(), isTrue);
    });
  });
}
