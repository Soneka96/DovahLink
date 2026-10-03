import 'package:flutter/material.dart';

import 'package:flutter_redux/flutter_redux.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:mocktail/mocktail.dart';
import 'package:redux/redux.dart';

import 'package:dovahlink_client/features/appearance/presentation/state/viewmodels/appearance_section.viewmodel.dart';
import 'package:dovahlink_client/features/device_identity/presentation/state/viewmodels/device_identity_section.viewmodel.dart';
import 'package:dovahlink_client/features/settings/presentation/widgets/settings_dialog.widget.dart';
import 'package:dovahlink_client/injection_container.dart';
import 'package:dovahlink_client/shared/constants/enums.dart';
import 'package:dovahlink_client/shared/state/app_state.dart';
import 'package:dovahlink_client/shared/theme/dovah_theme_presets.dart';
import 'package:dovahlink_client/shared/theme/widgets/dovah_dialog.widget.dart';

/// Mock ViewModel for appearance choices in Settings.
class MockAppearanceSectionViewModel extends Mock
    implements AppearanceSectionViewModel {}

/// Mock ViewModel for the device identity editor in Settings.
class MockDeviceIdentitySectionViewModel extends Mock
    implements DeviceIdentitySectionViewModel {}

/// Mock Redux store used by the Settings sections.
class MockStore extends Mock implements Store<AppState> {}

/// Exercises the shared Settings dialog and its two sections.
void main() {
  late MockStore store;
  late MockAppearanceSectionViewModel appearanceViewModel;
  late MockDeviceIdentitySectionViewModel deviceIdentityViewModel;
  final List<DovahThemePreset> selectedPresets = [];
  final List<String> savedNames = [];

  setUp(() async {
    await sl.reset();
    store = MockStore();
    appearanceViewModel = MockAppearanceSectionViewModel();
    deviceIdentityViewModel = MockDeviceIdentitySectionViewModel();
    selectedPresets.clear();
    savedNames.clear();
    when(() => store.state).thenReturn(AppState.initial());
    when(
      () => store.onChange,
    ).thenAnswer((_) => const Stream<AppState>.empty());
    when(
      () => appearanceViewModel.activePreset,
    ).thenReturn(DovahThemePreset.dovah);
    when(
      () => appearanceViewModel.onSelectPreset,
    ).thenReturn(selectedPresets.add);
    when(
      () => deviceIdentityViewModel.displayName,
    ).thenReturn('Living Room PC');
    when(() => deviceIdentityViewModel.loadFailure).thenReturn(null);
    when(() => deviceIdentityViewModel.isSaving).thenReturn(false);
    when(() => deviceIdentityViewModel.saveFailure).thenReturn(null);
    when(() => deviceIdentityViewModel.remoteRenameStatus).thenReturn(null);
    when(() => deviceIdentityViewModel.onSave).thenReturn(savedNames.add);
    sl.registerFactoryParam<AppearanceSectionViewModel, Store<AppState>, void>(
      (Store<AppState> _, void _) => appearanceViewModel,
    );
    sl.registerFactoryParam<
      DeviceIdentitySectionViewModel,
      Store<AppState>,
      void
    >((Store<AppState> _, void _) => deviceIdentityViewModel);
  });

  tearDown(() async {
    await sl.reset();
    reset(appearanceViewModel);
    reset(deviceIdentityViewModel);
    reset(store);
  });

  Widget buildWidget(DovahThemePreset preset) => StoreProvider<AppState>(
    store: store,
    child: MaterialApp(
      theme: dovahThemeDataFor(preset),
      home: Scaffold(
        body: Builder(
          builder: (BuildContext context) => TextButton(
            onPressed: () => SettingsDialog.show(context),
            child: const Text('Open Settings'),
          ),
        ),
      ),
    ),
  );

  Future<void> openSettings(
    WidgetTester tester,
    DovahThemePreset preset,
    Size size,
  ) async {
    tester.view.physicalSize = size * tester.view.devicePixelRatio;
    addTearDown(tester.view.reset);
    await tester.pumpWidget(buildWidget(preset));
    await tester.tap(find.text('Open Settings'));
    await tester.pumpAndSettle();
  }

  for (final DovahThemePreset preset in DovahThemePreset.values) {
    testWidgets('SettingsDialog contains its content under $preset', (
      WidgetTester tester,
    ) async {
      await openSettings(tester, preset, const Size(1280, 720));

      expect(find.byType(DovahDialog), findsOneWidget);
      expect(find.text('Settings'), findsOneWidget);
      expect(find.text('This device'), findsOneWidget);
      expect(
        find.text('Used to identify this companion after pairing.'),
        findsOneWidget,
      );
      expect(find.text('Choose your Skyrim atmosphere'), findsOneWidget);
      expect(
        find.text(
          'The interface stays familiar, but its material, shape, density and motion change.',
        ),
        findsOneWidget,
      );
      expect(
        find.byKey(const Key('appearance-preset-card-frostbound')),
        findsOneWidget,
      );
      expect(
        find.byKey(const Key('appearance-preset-card-dovah')),
        findsOneWidget,
      );
      expect(
        find.byKey(const Key('appearance-preset-card-hearth')),
        findsOneWidget,
      );
      expect(find.text('One DovahLink'), findsOneWidget);
      expect(
        find.text(
          'Navigation, connection state and accessibility remain consistent in every preset.',
        ),
        findsOneWidget,
      );
      expect(find.text('Designed for landscape'), findsOneWidget);
      expect(find.text('Landscape'), findsOneWidget);
      expect(tester.takeException(), isNull);
    });
  }

  testWidgets('SettingsDialog delegates device and appearance changes', (
    WidgetTester tester,
  ) async {
    await openSettings(tester, DovahThemePreset.dovah, const Size(1280, 720));
    await tester.enterText(
      find.byKey(const Key('settings-device-name-input')),
      'Gaming PC',
    );
    await tester.tap(find.byKey(const Key('settings-device-name-save')));
    await tester.tap(find.byKey(const Key('appearance-preset-card-hearth')));

    expect(savedNames, ['Gaming PC']);
    expect(selectedPresets, [DovahThemePreset.hearth]);
  });

  testWidgets('SettingsDialog fits a compact-height window without overflow', (
    WidgetTester tester,
  ) async {
    await openSettings(tester, DovahThemePreset.dovah, const Size(900, 560));

    expect(tester.takeException(), isNull);
  });
}
