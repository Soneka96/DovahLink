import 'package:flutter/material.dart';

import 'package:flutter_redux/flutter_redux.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:mocktail/mocktail.dart';
import 'package:redux/redux.dart';

import 'package:dovahlink_client/features/device_identity/presentation/sections/device_identity.section.dart';
import 'package:dovahlink_client/features/device_identity/presentation/state/viewmodels/device_identity_section.viewmodel.dart';
import 'package:dovahlink_client/injection_container.dart';
import 'package:dovahlink_client/shared/constants/enums.dart';
import 'package:dovahlink_client/shared/state/app_state.dart';
import 'package:dovahlink_client/shared/theme/dovah_theme_presets.dart';

/// Mock ViewModel used by the device-identity section's StoreConnector.
class MockDeviceIdentitySectionViewModel extends Mock
    implements DeviceIdentitySectionViewModel {}

/// Mock Redux store used by the section's StoreConnector.
class MockStore extends Mock implements Store<AppState> {}

/// Exercises the device-name section as presented inside Settings.
void main() {
  late MockStore store;
  late MockDeviceIdentitySectionViewModel viewModel;
  final List<String> savedNames = [];

  setUp(() async {
    await sl.reset();
    store = MockStore();
    viewModel = MockDeviceIdentitySectionViewModel();
    savedNames.clear();
    when(() => store.state).thenReturn(AppState.initial());
    when(
      () => store.onChange,
    ).thenAnswer((_) => const Stream<AppState>.empty());
    when(() => viewModel.displayName).thenReturn('Gaming PC');
    when(() => viewModel.loadFailure).thenReturn(null);
    when(() => viewModel.isSaving).thenReturn(false);
    when(() => viewModel.saveFailure).thenReturn(null);
    when(() => viewModel.remoteRenameStatus).thenReturn(null);
    when(() => viewModel.onSave).thenReturn(savedNames.add);
    sl.registerFactoryParam<
      DeviceIdentitySectionViewModel,
      Store<AppState>,
      void
    >((Store<AppState> _, void _) => viewModel);
  });

  tearDown(() async {
    await sl.reset();
    reset(viewModel);
    reset(store);
  });

  Widget buildWidget() => StoreProvider<AppState>(
    store: store,
    child: MaterialApp(
      theme: dovahThemeDataFor(DovahThemePreset.dovah),
      home: const Scaffold(body: DeviceIdentitySection()),
    ),
  );

  testWidgets('DeviceIdentitySection displays its label and explanation', (
    WidgetTester tester,
  ) async {
    await tester.pumpWidget(buildWidget());

    expect(find.text('This device'), findsOneWidget);
    expect(
      find.text('Used to identify this companion after pairing.'),
      findsOneWidget,
    );
    expect(find.text('Gaming PC'), findsOneWidget);
  });

  testWidgets('DeviceIdentitySection delegates the name to its ViewModel', (
    WidgetTester tester,
  ) async {
    await tester.pumpWidget(buildWidget());
    await tester.tap(find.byKey(const Key('settings-device-name-save')));

    expect(savedNames, ['Gaming PC']);
  });

  testWidgets('DeviceIdentitySection stacks its row in a narrow window', (
    WidgetTester tester,
  ) async {
    tester.view.physicalSize =
        const Size(440, 720) * tester.view.devicePixelRatio;
    addTearDown(tester.view.reset);
    await tester.pumpWidget(buildWidget());

    expect(find.text('This device'), findsOneWidget);
    expect(find.byKey(const Key('settings-device-name-save')), findsOneWidget);
    expect(tester.takeException(), isNull);
  });
}
