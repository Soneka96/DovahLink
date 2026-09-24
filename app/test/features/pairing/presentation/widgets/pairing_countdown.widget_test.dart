import 'package:flutter/material.dart';

import 'package:flutter_redux/flutter_redux.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:mocktail/mocktail.dart';
import 'package:redux/redux.dart';

import 'package:dovahlink_client/features/pairing/presentation/state/viewmodels/pairing_countdown.viewmodel.dart';
import 'package:dovahlink_client/features/pairing/presentation/widgets/pairing_countdown.widget.dart';
import 'package:dovahlink_client/injection_container.dart';
import 'package:dovahlink_client/shared/state/app_state.dart';

/// Mocks the countdown's Redux store subscription.
class MockStore extends Mock implements Store<AppState> {}

/// Mocks the countdown's presentation contract.
class MockPairingCountdownViewModel extends Mock
    implements PairingCountdownViewModel {}

/// Exercises [PairingCountdown] using its ViewModel presentation contract.
void main() {
  late Store<AppState> store;
  late MockPairingCountdownViewModel viewModel;
  Store<AppState>? resolvedStore;

  setUp(() async {
    await sl.reset();
    store = MockStore();
    viewModel = MockPairingCountdownViewModel();
    resolvedStore = null;
    when(
      () => store.onChange,
    ).thenAnswer((_) => const Stream<AppState>.empty());
    when(() => viewModel.remainingSeconds).thenReturn(null);
    sl.registerFactoryParam<PairingCountdownViewModel, Store<AppState>, void>((
      Store<AppState> storeParam,
      void _,
    ) {
      resolvedStore = storeParam;
      return viewModel;
    });
  });

  tearDown(() async {
    await sl.reset();
  });

  Widget buildWidget({
    TextStyle? textStyle,
    String Function(int seconds)? formatSeconds,
  }) => MaterialApp(
    home: StoreProvider<AppState>(
      store: store,
      child: Scaffold(
        body: formatSeconds == null
            ? PairingCountdown(textStyle: textStyle)
            : PairingCountdown(
                textStyle: textStyle,
                formatSeconds: formatSeconds,
              ),
      ),
    ),
  );

  group('PairingCountdown displays', () {
    testWidgets('PairingCountdown resolves its ViewModel with its Store', (
      WidgetTester tester,
    ) async {
      await tester.pumpWidget(buildWidget());

      expect(resolvedStore, same(store));
    });

    testWidgets('PairingCountdown renders nothing when seconds are null', (
      WidgetTester tester,
    ) async {
      await tester.pumpWidget(buildWidget());

      expect(find.byType(Text), findsNothing);
    });

    testWidgets(
      'PairingCountdown displays the default format when seconds remain',
      (WidgetTester tester) async {
        when(() => viewModel.remainingSeconds).thenReturn(125);

        await tester.pumpWidget(buildWidget());

        expect(find.text('2:05'), findsOneWidget);
      },
    );

    testWidgets('PairingCountdown displays zero for an expired code', (
      WidgetTester tester,
    ) async {
      when(() => viewModel.remainingSeconds).thenReturn(0);

      await tester.pumpWidget(buildWidget());

      expect(find.text('0:00'), findsOneWidget);
    });

    testWidgets('PairingCountdown applies the supplied text style', (
      WidgetTester tester,
    ) async {
      when(() => viewModel.remainingSeconds).thenReturn(10);
      const TextStyle style = TextStyle(
        fontSize: 32,
        fontWeight: FontWeight.bold,
      );

      await tester.pumpWidget(buildWidget(textStyle: style));

      expect(tester.widget<Text>(find.byType(Text)).style, style);
    });

    testWidgets('PairingCountdown uses the supplied format function', (
      WidgetTester tester,
    ) async {
      when(() => viewModel.remainingSeconds).thenReturn(90);
      int? formattedSeconds;

      await tester.pumpWidget(
        buildWidget(
          formatSeconds: (int seconds) {
            formattedSeconds = seconds;
            return '$seconds seconds remaining';
          },
        ),
      );

      expect(formattedSeconds, 90);
      expect(find.text('90 seconds remaining'), findsOneWidget);
    });
  });

  group('PairingCountdown tracks local timer ticks', () {
    testWidgets('PairingCountdown refreshes its ViewModel on timer ticks', (
      WidgetTester tester,
    ) async {
      await sl.unregister<PairingCountdownViewModel>();
      int resolutions = 0;
      const PairingCountdownViewModel initial = PairingCountdownViewModel(
        remainingSeconds: 60,
      );
      const PairingCountdownViewModel elapsed = PairingCountdownViewModel(
        remainingSeconds: 59,
      );
      sl.registerFactoryParam<PairingCountdownViewModel, Store<AppState>, void>(
        (Store<AppState> _, void _) {
          resolutions++;
          return resolutions == 1 ? initial : elapsed;
        },
      );

      await tester.pumpWidget(
        buildWidget(formatSeconds: (int seconds) => '${seconds}s'),
      );
      expect(find.text('60s'), findsOneWidget);

      await tester.pump(const Duration(seconds: 1));

      expect(resolutions, greaterThan(1));
      expect(find.text('59s'), findsOneWidget);
    });
  });
}
