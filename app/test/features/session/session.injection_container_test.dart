import 'package:flutter_test/flutter_test.dart';

import 'package:dovahlink_client/features/session/presentation/state/viewmodels/session_overview.viewmodel.dart';
import 'package:dovahlink_client/features/session/presentation/state/viewmodels/session_shell.viewmodel.dart';
import 'package:dovahlink_client/features/session/session.injection_container.dart';
import 'package:dovahlink_client/injection_container.dart';
import 'package:dovahlink_client/shared/state/create_store.dart';

import 'package:dovahlink_client_sdk/dovahlink_client.dart'
    show DovahLinkStateStatus;

/// Exercises Session feature dependency registration.
void main() {
  setUp(() async {
    await sl.reset();
  });

  tearDown(() async {
    await sl.reset();
  });

  group('initSessionDependencies behaves correctly', () {
    test(
      'initSessionDependencies registers a Store-backed Overview ViewModel',
      () {
        initSessionDependencies();
        final store = const CreateStore()();

        final SessionOverviewViewModel viewModel = sl<SessionOverviewViewModel>(
          param1: store,
        );

        expect(viewModel, isA<SessionOverviewViewModel>());
        expect(
          viewModel.characterVitals.status,
          DovahLinkStateStatus.notSubscribed,
        );
        expect(
          viewModel.trackedQuests.status,
          DovahLinkStateStatus.notSubscribed,
        );

        final SessionShellViewModel shellViewModel = sl<SessionShellViewModel>(
          param1: store,
          param2: 'missing-host',
        );
        expect(shellViewModel.host, isNull);
      },
    );
  });
}
