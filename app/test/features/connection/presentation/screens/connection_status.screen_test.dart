import 'package:flutter/material.dart' hide ConnectionState;
import 'package:flutter_redux/flutter_redux.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:mocktail/mocktail.dart';
import 'package:redux/redux.dart';

import 'package:dovahlink_client/features/connection/presentation/screens/connection_status.screen.dart';
import 'package:dovahlink_client/features/connection/presentation/state/viewmodels/connection_status_screen.viewmodel.dart';
import 'package:dovahlink_client/injection_container.dart';
import 'package:dovahlink_client/shared/constants/enums.dart';
import 'package:dovahlink_client/shared/state/app_state.dart';

/// Mocktail double for [ConnectionStatusScreenViewModel], the DI-resolved
/// dependency [ConnectionStatusScreen] converts to -- mocked the same as any
/// other, never hand-built or derived from a real store.
class MockConnectionStatusScreenViewModel extends Mock
    implements ConnectionStatusScreenViewModel {}

/// Mocktail double for [Store], standing in for the `Store<AppState>` passed
/// to [StoreProvider]. [ConnectionStatusScreen] has no `onInit`/`onDispose`
/// dispatch of its own, so this only needs to satisfy `StoreConnector`'s
/// internal `onChange` subscription.
class MockStore extends Mock implements Store<AppState> {}

/// Exercises connection-status rendering against a directly-mocked
/// [ConnectionStatusScreenViewModel] -- reducer and ViewModel-derivation
/// correctness belong to their own test files, not here.
void main() {
  late MockConnectionStatusScreenViewModel mockViewModel;
  late MockStore store;

  setUp(() async {
    await sl.reset();

    mockViewModel = MockConnectionStatusScreenViewModel();
    when(() => mockViewModel.phase).thenReturn(ConnectionPhase.disconnected);
    when(() => mockViewModel.statusLabel).thenReturn('Disconnected');
    when(() => mockViewModel.sessionId).thenReturn(null);
    when(() => mockViewModel.error).thenReturn(null);
    sl.registerFactoryParam<
      ConnectionStatusScreenViewModel,
      Store<AppState>,
      void
    >((Store<AppState> store, void _) {
      return mockViewModel;
    });

    store = MockStore();
    when(
      () => store.onChange,
    ).thenAnswer((_) => const Stream<AppState>.empty());
  });

  tearDown(() async {
    await sl.reset();
    reset(mockViewModel);
    reset(store);
  });

  Widget buildWidget() => StoreProvider<AppState>(
    store: store,
    child: const MaterialApp(home: ConnectionStatusScreen()),
  );

  group('ConnectionStatusScreen contains widgets', () {
    testWidgets('contains the disconnected status with correct parameters', (
      WidgetTester tester,
    ) async {
      await tester.pumpWidget(buildWidget());

      expect(find.byKey(const Key('connection-status')), findsOneWidget);
      expect(find.text('Disconnected'), findsOneWidget);
      expect(find.byKey(const Key('connection-error')), findsNothing);
      expect(find.textContaining('Session:'), findsNothing);
    });

    testWidgets('contains the connected session with correct parameters', (
      WidgetTester tester,
    ) async {
      when(() => mockViewModel.phase).thenReturn(ConnectionPhase.connected);
      when(() => mockViewModel.statusLabel).thenReturn('Connected');
      when(() => mockViewModel.sessionId).thenReturn('session-1');

      await tester.pumpWidget(buildWidget());

      expect(find.text('Connected'), findsOneWidget);
      expect(find.text('Session: session-1'), findsOneWidget);
      expect(find.byKey(const Key('connection-error')), findsNothing);
    });
  });

  group('ConnectionStatusScreen displays connection states', () {
    testWidgets('displays a connection error when it exists', (
      WidgetTester tester,
    ) async {
      when(() => mockViewModel.phase).thenReturn(ConnectionPhase.unavailable);
      when(() => mockViewModel.statusLabel).thenReturn('Unavailable');
      when(() => mockViewModel.error).thenReturn('Bridge unavailable');

      await tester.pumpWidget(buildWidget());

      expect(find.text('Unavailable'), findsOneWidget);
      expect(find.byKey(const Key('connection-error')), findsOneWidget);
      expect(find.text('Bridge unavailable'), findsOneWidget);
    });
  });

  group(
    'ConnectionStatusScreen meets accessibility recommended guidelines',
    () {
      testWidgets('contains a semantic connection status label', (
        WidgetTester tester,
      ) async {
        final SemanticsHandle handle = tester.ensureSemantics();
        try {
          await tester.pumpWidget(buildWidget());

          expect(
            tester.getSemantics(find.byKey(const Key('connection-status'))),
            matchesSemantics(label: 'Disconnected'),
          );
        } finally {
          handle.dispose();
        }
      });
    },
  );
}
