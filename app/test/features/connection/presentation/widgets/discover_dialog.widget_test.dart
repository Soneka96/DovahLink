import 'package:flutter/material.dart';
import 'package:flutter/semantics.dart';

import 'package:flutter_redux/flutter_redux.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:fpdart/fpdart.dart';
import 'package:mocktail/mocktail.dart';
import 'package:redux/redux.dart';

import 'package:dovahlink_client/features/connection/domain/entities/host.entity.dart';
import 'package:dovahlink_client/features/connection/domain/entities/known_host.entity.dart';
import 'package:dovahlink_client/features/connection/presentation/state/connection.actions.dart';
import 'package:dovahlink_client/features/connection/presentation/state/viewmodels/discover_dialog.viewmodel.dart';
import 'package:dovahlink_client/features/connection/presentation/viewdata/host_card.viewdata.dart';
import 'package:dovahlink_client/features/connection/presentation/widgets/discover_candidate_card.widget.dart';
import 'package:dovahlink_client/features/connection/presentation/widgets/discover_dialog.widget.dart';
import 'package:dovahlink_client/features/pairing/domain/usecases/disconnect.usecase.dart';
import 'package:dovahlink_client/features/pairing/presentation/sections/pairing.section.dart';
import 'package:dovahlink_client/features/pairing/presentation/state/pairing.actions.dart';
import 'package:dovahlink_client/features/pairing/presentation/state/pairing.middleware.dart';
import 'package:dovahlink_client/features/pairing/presentation/state/pairing.state.dart';
import 'package:dovahlink_client/features/pairing/presentation/state/viewmodels/pairing_dialog.viewmodel.dart';
import 'package:dovahlink_client/features/pairing/presentation/state/viewmodels/pairing_section.viewmodel.dart';
import 'package:dovahlink_client/features/pairing/presentation/widgets/pairing_dialog.widget.dart';
import 'package:dovahlink_client/features/pairing/presentation/widgets/pairing_failure.widget.dart';
import 'package:dovahlink_client/injection_container.dart';
import 'package:dovahlink_client/shared/constants/enums.dart';
import 'package:dovahlink_client/shared/state/app_state.dart';
import 'package:dovahlink_client/shared/state/create_store.dart';
import 'package:dovahlink_client/shared/theme/dovah_dialog_metrics.dart';
import 'package:dovahlink_client/shared/theme/dovah_theme_context.dart';
import 'package:dovahlink_client/shared/theme/dovah_theme_presets.dart';
import 'package:dovahlink_client/shared/theme/widgets/dovah_dialog.widget.dart';
import 'package:dovahlink_client/shared/usecase/no_params.dart';
import '../../../../fixtures/fixtures.dart';
import '../../../../shared/theme/widgets/dovah_widget_test_helpers.dart';

import 'package:dovahlink_client/features/connection/presentation/state/connection.state.dart'
    as connection;

/// Mock ViewModel supplied to [DiscoverDialog].
class MockDiscoverDialogViewModel extends Mock
    implements DiscoverDialogViewModel {}

/// Mock Store supplied to the dialog's [StoreConnector].
class MockStore extends Mock implements Store<AppState> {}

/// Mock disconnect resolved by the real [PairingMiddleware] when pairing is disposed.
class MockDisconnectUseCase extends Mock implements DisconnectUseCase {}

/// Exercises [DiscoverDialog]'s live discovery states and selection result.
void main() {
  late MockStore store;
  late MockDiscoverDialogViewModel viewModel;
  late List<String> discoverCalls;
  late List<HostCardViewData> selectedCandidates;
  late List<String> disposeCalls;
  late HostCardViewData candidate;

  setUp(() async {
    await sl.reset();
    store = MockStore();
    viewModel = MockDiscoverDialogViewModel();
    discoverCalls = [];
    selectedCandidates = [];
    disposeCalls = [];
    candidate = Fixtures.buildHostCardViewData(
      host: Fixtures.buildHost(uri: Uri.parse('ws://127.0.0.1:58231/')),
      source: ConnectionHostSelectionSource.candidate,
      title: 'Local Host',
      subtitle: 'DovahLink · Ready to connect',
    );

    when(() => store.state).thenReturn(AppState.initial());
    when(
      () => store.onChange,
    ).thenAnswer((_) => const Stream<AppState>.empty());
    when(() => viewModel.status).thenReturn(ConnectionDiscoveryStatus.idle);
    when(() => viewModel.candidates).thenReturn(const <HostCardViewData>[]);
    when(() => viewModel.failure).thenReturn(null);
    when(() => viewModel.canDiscover).thenReturn(true);
    when(() => viewModel.selectedCandidate).thenReturn(null);
    when(() => viewModel.pairingPhase).thenReturn(PairingPhase.none);
    when(() => viewModel.pairingSupport).thenReturn(PairingSupport.available);
    when(() => viewModel.canSelectCandidate).thenReturn(true);
    when(() => viewModel.shouldContinueToPairing).thenReturn(false);
    when(() => viewModel.hasTrustedCandidate).thenReturn(false);
    when(
      () => viewModel.onDiscover,
    ).thenReturn(() => discoverCalls.add('discover'));
    when(() => viewModel.onSelectCandidate).thenReturn((HostCardViewData card) {
      selectedCandidates.add(card);
      return true;
    });
    when(
      () => viewModel.onDispose,
    ).thenReturn(() => disposeCalls.add('dispose'));
    sl.registerFactoryParam<DiscoverDialogViewModel, Store<AppState>, void>(
      (Store<AppState> _, void _) => viewModel,
    );
  });

  tearDown(() async {
    await sl.reset();
    reset(viewModel);
    reset(store);
  });

  /// Builds the discovery dialog with [preset]'s theme and the mock Store.
  Widget buildDialog({DovahThemePreset preset = DovahThemePreset.dovah}) =>
      StoreProvider<AppState>(
        store: store,
        child: MaterialApp(
          theme: dovahThemeDataFor(preset),
          home: const Scaffold(body: DiscoverDialog()),
        ),
      );

  void registerRealPairingViewModels() {
    sl.registerFactoryParam<PairingDialogViewModel, Store<AppState>, void>(
      (Store<AppState> store, void _) =>
          PairingDialogViewModel.fromStore(store),
    );
    sl.registerFactoryParam<PairingSectionViewModel, Store<AppState>, void>(
      (Store<AppState> store, void _) =>
          PairingSectionViewModel.fromStore(store),
    );
  }

  group('DiscoverDialog opens and searches', () {
    testWidgets('DiscoverDialog starts discovery and displays its title', (
      WidgetTester tester,
    ) async {
      setDovahTestWindow(tester, dovahResponsiveTestSizes.last);
      await tester.pumpWidget(buildDialog());

      expect(discoverCalls, ['discover']);
      expect(find.byType(DovahDialog), findsOneWidget);
      expect(find.text('Discover Skyrim'), findsOneWidget);
    });

    testWidgets(
      'DiscoverDialog does not dispose pairing when closed unselected',
      (WidgetTester tester) async {
        late Future<void> flow;
        setDovahTestWindow(tester, dovahResponsiveTestSizes.last);
        await tester.pumpWidget(
          StoreProvider<AppState>(
            store: store,
            child: MaterialApp(
              theme: dovahThemeDataFor(DovahThemePreset.dovah),
              home: Builder(
                builder: (BuildContext context) => Scaffold(
                  body: TextButton(
                    onPressed: () => flow = DiscoverDialog.show(context),
                    child: const Text('Open Discover'),
                  ),
                ),
              ),
            ),
          ),
        );

        await tester.tap(find.text('Open Discover'));
        await tester.pump();
        await tester.tap(find.byTooltip('Close'));
        await tester.pumpAndSettle();
        await flow;

        expect(disposeCalls, isEmpty);
      },
    );

    testWidgets('DiscoverDialog announces and spins while discovering', (
      WidgetTester tester,
    ) async {
      when(
        () => viewModel.status,
      ).thenReturn(ConnectionDiscoveryStatus.discovering);
      final SemanticsHandle semantics = tester.ensureSemantics();
      try {
        setDovahTestWindow(tester, dovahResponsiveTestSizes.last);
        await tester.pumpWidget(buildDialog());

        expect(
          find.byKey(const Key('discover-searching-spinner')),
          findsOneWidget,
        );
        expect(
          find.text('Searching for DovahLink on this PC…'),
          findsOneWidget,
        );
        expect(
          tester.getSemantics(
            find.byKey(const Key('discover-searching-message')),
          ),
          isSemantics(
            label: 'Searching for DovahLink on this PC…',
            isLiveRegion: true,
          ),
        );
      } finally {
        semantics.dispose();
      }
    });
  });

  group('DiscoverDialog displays available candidates', () {
    testWidgets(
      'DiscoverDialog announces the Local Host result with a success marker',
      (WidgetTester tester) async {
        when(
          () => viewModel.status,
        ).thenReturn(ConnectionDiscoveryStatus.available);
        when(() => viewModel.candidates).thenReturn([candidate]);
        final SemanticsHandle semantics = tester.ensureSemantics();
        try {
          await tester.pumpWidget(buildDialog());

          expect(
            tester.getSemantics(find.bySemanticsLabel('Local Host found.')),
            isSemantics(label: 'Local Host found.', isLiveRegion: true),
          );
          expect(
            find.byWidgetPredicate(
              (Widget widget) => widget is Icon && widget.icon == Icons.circle,
            ),
            findsOneWidget,
          );
          final Finder markerFinder = find.byWidgetPredicate(
            (Widget widget) => widget is Icon && widget.icon == Icons.circle,
          );
          final Icon marker = tester.widget<Icon>(markerFinder);
          expect(marker.size, DovahDialogMetrics.discoveryStatusDotSize);
          expect(
            marker.color,
            tester.element(markerFinder).dovahTokens.success,
          );
        } finally {
          semantics.dispose();
        }
      },
    );

    testWidgets(
      'DiscoverDialog shows the prototype Local Host card with routing data',
      (WidgetTester tester) async {
        when(
          () => viewModel.status,
        ).thenReturn(ConnectionDiscoveryStatus.available);
        when(() => viewModel.candidates).thenReturn([candidate]);
        setDovahTestWindow(tester, dovahResponsiveTestSizes.last);
        await tester.pumpWidget(buildDialog());

        expect(find.text('Local Host found.'), findsOneWidget);
        expect(find.text('AVAILABLE'), findsOneWidget);
        expect(find.text('Local Host'), findsOneWidget);
        expect(find.text('DovahLink · Ready to connect'), findsOneWidget);
        expect(
          find.byKey(Key('discover-candidate-${candidate.host.hostId}')),
          findsOneWidget,
        );
      },
    );

    for (final bool canSelectCandidate in [true, false]) {
      testWidgets(
        'DiscoverDialog presents the candidate as enabled only when it can start pairing ($canSelectCandidate)',
        (WidgetTester tester) async {
          when(
            () => viewModel.status,
          ).thenReturn(ConnectionDiscoveryStatus.available);
          when(() => viewModel.candidates).thenReturn([candidate]);
          when(
            () => viewModel.canSelectCandidate,
          ).thenReturn(canSelectCandidate);
          final SemanticsHandle semantics = tester.ensureSemantics();
          try {
            setDovahTestWindow(tester, dovahResponsiveTestSizes.last);
            await tester.pumpWidget(buildDialog());
            final Finder card = find.byKey(
              Key('discover-candidate-${candidate.host.hostId}'),
            );

            expect(
              tester.widget<DiscoverCandidateCard>(card).onTap != null,
              canSelectCandidate,
            );
            expect(
              tester.getSemantics(
                find.bySemanticsLabel(
                  'Local Host, DovahLink · Ready to connect',
                ),
              ),
              isSemantics(
                label: 'Local Host, DovahLink · Ready to connect',
                isButton: true,
                hasEnabledState: true,
                isEnabled: canSelectCandidate,
                hasTapAction: canSelectCandidate,
              ),
            );
            await tester.tap(card);
            await tester.pump();

            expect(
              selectedCandidates,
              canSelectCandidate ? [candidate] : isEmpty,
            );
          } finally {
            semantics.dispose();
          }
        },
      );
    }

    testWidgets(
      'DiscoverDialog does not dispose a candidate lifecycle when selection did not start one',
      (WidgetTester tester) async {
        when(
          () => viewModel.status,
        ).thenReturn(ConnectionDiscoveryStatus.available);
        when(() => viewModel.candidates).thenReturn([candidate]);
        when(() => viewModel.canSelectCandidate).thenReturn(true);
        when(() => viewModel.onSelectCandidate).thenReturn((
          HostCardViewData _,
        ) {
          return false;
        });
        setDovahTestWindow(tester, dovahResponsiveTestSizes.last);
        await tester.pumpWidget(buildDialog());

        await tester.tap(
          find.byKey(Key('discover-candidate-${candidate.host.hostId}')),
        );
        await tester.pump();
        await tester.pumpWidget(const SizedBox.shrink());

        expect(disposeCalls, isEmpty);
      },
    );

    testWidgets(
      'DiscoverDialog handles available status before its candidate snapshot',
      (WidgetTester tester) async {
        when(
          () => viewModel.status,
        ).thenReturn(ConnectionDiscoveryStatus.available);
        when(() => viewModel.candidates).thenReturn(const <HostCardViewData>[]);
        setDovahTestWindow(tester, dovahResponsiveTestSizes.last);
        await tester.pumpWidget(buildDialog());

        expect(find.text('No other Skyrim PCs found.'), findsOneWidget);
        expect(find.byKey(const Key('discover-retry-button')), findsOneWidget);
      },
    );

    for (final DovahThemePreset preset in DovahThemePreset.values) {
      for (final Size size in const [
        Size(500, 400),
        Size(720, 480),
        Size(1280, 720),
      ]) {
        testWidgets(
          'DiscoverDialog lays out a candidate under $preset at $size',
          (WidgetTester tester) async {
            when(
              () => viewModel.status,
            ).thenReturn(ConnectionDiscoveryStatus.available);
            when(() => viewModel.candidates).thenReturn([candidate]);
            setDovahTestWindow(tester, size);
            await tester.pumpWidget(buildDialog(preset: preset));

            expect(tester.takeException(), isNull);
            expect(find.text('Local Host found.'), findsOneWidget);
            expect(find.byType(DiscoverCandidateCard), findsOneWidget);
          },
        );
      }
    }
  });

  group('DiscoverDialog follows Redux discovery state', () {
    testWidgets(
      'candidate checking stays in discovery until authentication reports its outcome',
      (WidgetTester tester) async {
        final Host discoveredHost = Fixtures.buildHost(
          uri: Uri.parse('ws://127.0.0.1:58231/'),
        );
        final List<Object?> actions = [];

        /// Records Redux actions before reducers handle them.
        void recordActions(
          Store<AppState> store,
          dynamic action,
          NextDispatcher next,
        ) {
          actions.add(action);
          next(action);
        }

        final Store<AppState> realStore = const CreateStore()(
          initialState: AppState(
            connection: connection.ConnectionState(
              hosts: [discoveredHost],
              discoveryStatus: ConnectionDiscoveryStatus.available,
            ),
            pairing: PairingState.initial(),
          ),
          middleware: [recordActions],
        );
        sl.unregister<DiscoverDialogViewModel>();
        sl.registerFactoryParam<DiscoverDialogViewModel, Store<AppState>, void>(
          (Store<AppState> store, void _) =>
              DiscoverDialogViewModel.fromStore(store),
        );
        late Future<void> flow;

        setDovahTestWindow(tester, dovahResponsiveTestSizes.last);
        await tester.pumpWidget(
          StoreProvider<AppState>(
            store: realStore,
            child: MaterialApp(
              theme: dovahThemeDataFor(DovahThemePreset.dovah),
              home: Builder(
                builder: (BuildContext context) => Scaffold(
                  body: TextButton(
                    onPressed: () => flow = DiscoverDialog.show(context),
                    child: const Text('Open Discover'),
                  ),
                ),
              ),
            ),
          ),
        );
        await tester.tap(find.text('Open Discover'));
        await tester.pump();
        expect(find.text('Local Host'), findsOneWidget);
        expect(find.text('DovahLink · Ready to connect'), findsOneWidget);
        expect(realStore.state.connection.knownHosts, isEmpty);

        await tester.tap(
          find.byKey(Key('discover-candidate-${discoveredHost.hostId}')),
        );
        await tester.pump();
        expect(find.text('Checking trusted connection…'), findsOneWidget);
        expect(find.byType(PairingDialog), findsNothing);
        expect(realStore.state.connection.selectedHost, discoveredHost);
        expect(realStore.state.connection.knownHosts, isEmpty);
        expect(realStore.state.pairing.phase, PairingPhase.connecting);

        realStore.dispatch(const PairingDisconnectedAction());
        await tester.pump();
        expect(find.text('Local Host is offline'), findsOneWidget);
        expect(find.text('Waiting for Skyrim…'), findsOneWidget);
        expect(find.byType(PairingDialog), findsNothing);

        realStore.dispatch(
          const PairingAuthenticatedAction(hostVersion: '0.5.0', trusted: true),
        );
        await tester.pump();
        await tester.pumpAndSettle();

        await flow;
        expect(find.byType(DovahDialog), findsNothing);
        expect(find.byType(PairingSection), findsNothing);
        expect(find.byType(PairingDialog), findsNothing);
        expect(realStore.state.pairing.phase, PairingPhase.none);
        expect(
          actions.whereType<PairingDisposedAction>().single.wasTrusted,
          isTrue,
        );
        expect(realStore.state.connection.knownHosts, isEmpty);
      },
    );

    testWidgets(
      'closing during candidate authentication disposes the active attempt',
      (WidgetTester tester) async {
        final Host candidateHost = Fixtures.buildHost(
          uri: Uri.parse('ws://127.0.0.1:58231/'),
        );
        final List<Object?> actions = [];

        /// Records Redux actions before reducers handle them.
        void recordActions(
          Store<AppState> store,
          dynamic action,
          NextDispatcher next,
        ) {
          actions.add(action);
          next(action);
        }

        final Store<AppState> realStore = const CreateStore()(
          initialState: AppState(
            connection: connection.ConnectionState(
              hosts: [candidateHost],
              discoveryStatus: ConnectionDiscoveryStatus.available,
            ),
            pairing: PairingState.initial(),
          ),
          middleware: [recordActions],
        );
        sl.unregister<DiscoverDialogViewModel>();
        sl.registerFactoryParam<DiscoverDialogViewModel, Store<AppState>, void>(
          (Store<AppState> store, void _) =>
              DiscoverDialogViewModel.fromStore(store),
        );
        late Future<void> flow;

        setDovahTestWindow(tester, dovahResponsiveTestSizes.last);
        await tester.pumpWidget(
          StoreProvider<AppState>(
            store: realStore,
            child: MaterialApp(
              theme: dovahThemeDataFor(DovahThemePreset.dovah),
              home: Builder(
                builder: (BuildContext context) => Scaffold(
                  body: TextButton(
                    onPressed: () => flow = DiscoverDialog.show(context),
                    child: const Text('Open Discover'),
                  ),
                ),
              ),
            ),
          ),
        );
        await tester.tap(find.text('Open Discover'));
        await tester.pump();
        await tester.tap(
          find.byKey(Key('discover-candidate-${candidateHost.hostId}')),
        );
        await tester.pump();
        expect(realStore.state.pairing.phase, PairingPhase.connecting);

        await tester.tap(find.byTooltip('Close'));
        await tester.pumpAndSettle();
        await flow;

        expect(
          actions.whereType<PairingDisposedAction>().single.wasTrusted,
          isFalse,
        );
        expect(realStore.state.pairing.phase, PairingPhase.none);
        expect(realStore.state.connection.selectedHost, candidateHost);
      },
    );

    testWidgets(
      'unpaired candidates continue to pairing inside the same modal',
      (WidgetTester tester) async {
        final Host discoveredHost = Fixtures.buildHost(
          uri: Uri.parse('ws://127.0.0.1:58231/'),
        );
        final Store<AppState> realStore = const CreateStore()(
          initialState: AppState(
            connection: connection.ConnectionState(
              hosts: [discoveredHost],
              discoveryStatus: ConnectionDiscoveryStatus.available,
            ),
            pairing: PairingState.initial(),
          ),
        );
        sl.unregister<DiscoverDialogViewModel>();
        sl.registerFactoryParam<DiscoverDialogViewModel, Store<AppState>, void>(
          (Store<AppState> store, void _) =>
              DiscoverDialogViewModel.fromStore(store),
        );
        registerRealPairingViewModels();
        late Future<void> flow;

        setDovahTestWindow(tester, dovahResponsiveTestSizes.last);
        await tester.pumpWidget(
          StoreProvider<AppState>(
            store: realStore,
            child: MaterialApp(
              theme: dovahThemeDataFor(DovahThemePreset.dovah),
              home: Builder(
                builder: (BuildContext context) => Scaffold(
                  body: TextButton(
                    onPressed: () => flow = DiscoverDialog.show(context),
                    child: const Text('Open Discover'),
                  ),
                ),
              ),
            ),
          ),
        );
        await tester.tap(find.text('Open Discover'));
        await tester.pumpAndSettle();
        await tester.tap(
          find.byKey(Key('discover-candidate-${discoveredHost.hostId}')),
        );
        await tester.pump();
        expect(find.text('Checking trusted connection…'), findsOneWidget);
        expect(find.byType(DovahDialog), findsOneWidget);

        realStore.dispatch(
          const PairingAuthenticatedAction(
            hostVersion: '0.5.0',
            trusted: false,
          ),
        );
        expect(realStore.state.pairing.phase, PairingPhase.unpaired);
        expect(
          DiscoverDialogViewModel.fromStore(realStore).shouldContinueToPairing,
          isTrue,
        );
        await tester.pump();
        await tester.pump();
        expect(find.byType(DovahDialog), findsOneWidget);
        expect(
          find.text('Pair with ${discoveredHost.displayName}'),
          findsOneWidget,
        );
        expect(find.byType(PairingSection), findsOneWidget);
        expect(find.byType(PairingDialog), findsNothing);
        expect(realStore.state.connection.knownHosts, isEmpty);

        await tester.tap(find.byTooltip('Close'));
        await tester.pumpAndSettle();
        await flow;
      },
    );

    testWidgets(
      'authentication failures transition to pairing feedback in the same modal',
      (WidgetTester tester) async {
        final Host discoveredHost = Fixtures.buildHost(
          uri: Uri.parse('ws://127.0.0.1:58231/'),
        );
        final Store<AppState> realStore = const CreateStore()(
          initialState: AppState(
            connection: connection.ConnectionState(
              hosts: [discoveredHost],
              discoveryStatus: ConnectionDiscoveryStatus.available,
            ),
            pairing: PairingState.initial(),
          ),
        );
        sl.unregister<DiscoverDialogViewModel>();
        sl.registerFactoryParam<DiscoverDialogViewModel, Store<AppState>, void>(
          (Store<AppState> store, void _) =>
              DiscoverDialogViewModel.fromStore(store),
        );
        registerRealPairingViewModels();
        late Future<void> flow;

        setDovahTestWindow(tester, dovahResponsiveTestSizes.last);
        await tester.pumpWidget(
          StoreProvider<AppState>(
            store: realStore,
            child: MaterialApp(
              theme: dovahThemeDataFor(DovahThemePreset.dovah),
              home: Builder(
                builder: (BuildContext context) => Scaffold(
                  body: TextButton(
                    onPressed: () => flow = DiscoverDialog.show(context),
                    child: const Text('Open Discover'),
                  ),
                ),
              ),
            ),
          ),
        );
        await tester.tap(find.text('Open Discover'));
        await tester.pumpAndSettle();
        await tester.tap(
          find.byKey(Key('discover-candidate-${discoveredHost.hostId}')),
        );
        await tester.pump();

        realStore.dispatch(const PairingFailedAction('Authentication failed.'));
        await tester.pump();
        await tester.pump();

        expect(find.byType(DovahDialog), findsOneWidget);
        expect(find.byType(PairingFailure), findsOneWidget);
        expect(find.text('Authentication failed.'), findsOneWidget);
        expect(find.byType(PairingDialog), findsNothing);

        await tester.tap(find.byTooltip('Close'));
        await tester.pumpAndSettle();
        await flow;
      },
    );
  });

  group('DiscoverDialog owns the embedded pairing lifecycle', () {
    late MockDisconnectUseCase mockDisconnect;
    late List<Object?> actions;
    late Host candidateHost;
    late Store<AppState> realStore;
    late Future<void> flow;

    setUpAll(() => registerFallbackValue(NoParams()));

    setUp(() {
      mockDisconnect = MockDisconnectUseCase();
      when(
        () => mockDisconnect(any()),
      ).thenAnswer((_) async => const Right(unit));
      sl.registerLazySingleton<DisconnectUseCase>(() => mockDisconnect);
      actions = [];
      candidateHost = Fixtures.buildHost(
        uri: Uri.parse('ws://127.0.0.1:58231/'),
      );
      final PairingMiddleware pairingMiddleware = PairingMiddleware();
      addTearDown(pairingMiddleware.shutdown);

      /// Records actions, simulates SDK pairing results, and runs real disposal middleware.
      void recordActions(
        Store<AppState> store,
        dynamic action,
        NextDispatcher next,
      ) {
        actions.add(action);
        if (action is PairingStartedAction ||
            action is PairingCodeSubmittedAction) {
          next(action);
        } else {
          pairingMiddleware.call(store, action, next);
        }
      }

      realStore = const CreateStore()(
        initialState: AppState(
          connection: connection.ConnectionState(
            hosts: [candidateHost],
            discoveryStatus: ConnectionDiscoveryStatus.available,
          ),
          pairing: PairingState.initial(),
        ),
        middleware: [recordActions],
      );
      sl.unregister<DiscoverDialogViewModel>();
      sl.registerFactoryParam<DiscoverDialogViewModel, Store<AppState>, void>(
        (Store<AppState> store, void _) =>
            DiscoverDialogViewModel.fromStore(store),
      );
      registerRealPairingViewModels();
    });

    /// Opens Discover through its real route, selects its candidate, and applies an unpaired SDK
    /// outcome, keeping the route future in [flow].
    Future<void> openEmbeddedPairing(WidgetTester tester) async {
      setDovahTestWindow(tester, dovahResponsiveTestSizes.last);
      await tester.pumpWidget(
        StoreProvider<AppState>(
          store: realStore,
          child: MaterialApp(
            theme: dovahThemeDataFor(DovahThemePreset.dovah),
            home: Builder(
              builder: (BuildContext context) => Scaffold(
                body: TextButton(
                  onPressed: () => flow = DiscoverDialog.show(context),
                  child: const Text('Open Discover'),
                ),
              ),
            ),
          ),
        ),
      );
      await tester.tap(find.text('Open Discover'));
      await tester.pump();
      await tester.tap(
        find.byKey(Key('discover-candidate-${candidateHost.hostId}')),
      );
      await tester.pump();
      expect(find.text('Checking trusted connection…'), findsOneWidget);
      realStore.dispatch(
        const PairingAuthenticatedAction(hostVersion: '0.5.0', trusted: false),
      );
      await tester.pump();
      await tester.pump();
      expect(find.byType(PairingSection), findsOneWidget);
      final PairingSection section = tester.widget(find.byType(PairingSection));
      expect(section.startOnInit, isFalse);
      expect(section.disposeOnRemove, isFalse);
    }

    testWidgets(
      'DiscoverDialog dispatches one PairingDisposedAction and disconnects once when closed during embedded pairing',
      (WidgetTester tester) async {
        await openEmbeddedPairing(tester);

        await tester.tap(find.byTooltip('Close'));
        await tester.pumpAndSettle();
        await flow;
        await tester.pump();

        expect(find.byType(DovahDialog), findsNothing);
        expect(find.byType(PairingSection), findsNothing);
        expect(
          actions.whereType<PairingDisposedAction>().single.wasTrusted,
          isFalse,
        );
        verify(() => mockDisconnect(any())).called(1);
        expect(realStore.state.pairing.phase, PairingPhase.none);
      },
    );

    testWidgets(
      'DiscoverDialog keeps the session when Done closes a successful embedded pairing',
      (WidgetTester tester) async {
        await openEmbeddedPairing(tester);
        realStore.dispatch(
          ConnectionKnownHostsChangedAction([
            KnownHost(
              host: candidateHost,
              availability: HostAvailability.online,
            ),
          ]),
        );
        await tester.pump();

        expect(
          realStore.state.connection.selectedHostSource,
          ConnectionHostSelectionSource.knownHost,
        );
        expect(realStore.state.connection.selectedHost, candidateHost);
        expect(
          realStore.state.connection.knownHosts.single.host,
          candidateHost,
        );
        expect(actions.whereType<ConnectionHostSelectedAction>(), hasLength(1));
        expect(actions.whereType<PairingStartedAction>(), hasLength(1));

        realStore.dispatch(const PairingCodeSubmittedAction(code: '123456'));
        await tester.pump();
        expect(realStore.state.pairing.phase, PairingPhase.confirming);
        realStore.dispatch(const PairingConfirmedAction());
        await tester.pump();
        expect(realStore.state.pairing.phase, PairingPhase.trusted);

        await tester.tap(find.text('Done'));
        await tester.pumpAndSettle();
        await flow;
        await tester.pump();

        expect(find.byType(DovahDialog), findsNothing);
        expect(find.byType(PairingSection), findsNothing);
        expect(
          actions.whereType<PairingDisposedAction>().single.wasTrusted,
          isTrue,
        );
        expect(realStore.state.pairing.phase, PairingPhase.none);
        expect(
          realStore.state.connection.selectedHostSource,
          ConnectionHostSelectionSource.knownHost,
        );
        expect(realStore.state.connection.selectedHost, candidateHost);
        verifyNever(() => mockDisconnect(any()));
      },
    );
  });

  group('DiscoverDialog retries empty and failed searches', () {
    testWidgets('DiscoverDialog offers another search when results are empty', (
      WidgetTester tester,
    ) async {
      when(() => viewModel.status).thenReturn(ConnectionDiscoveryStatus.empty);
      setDovahTestWindow(tester, dovahResponsiveTestSizes.last);
      await tester.pumpWidget(buildDialog());

      expect(find.text('No other Skyrim PCs found.'), findsOneWidget);
      await tester.tap(find.byKey(const Key('discover-retry-button')));
      await tester.pump();

      expect(discoverCalls, ['discover', 'discover']);
    });

    testWidgets('DiscoverDialog displays app-owned failure copy', (
      WidgetTester tester,
    ) async {
      when(() => viewModel.status).thenReturn(ConnectionDiscoveryStatus.failed);
      when(
        () => viewModel.failure,
      ).thenReturn(ConnectionFailureReason.hostUnavailable);
      setDovahTestWindow(tester, dovahResponsiveTestSizes.last);
      await tester.pumpWidget(buildDialog());

      expect(
        find.text(
          'Could not reach the local Host. Check that it is running and try again.',
        ),
        findsOneWidget,
      );
      expect(find.byKey(const Key('discover-retry-button')), findsOneWidget);
    });

    testWidgets(
      'DiscoverDialog keeps an incompatible Host error distinct from empty and offline states',
      (WidgetTester tester) async {
        when(
          () => viewModel.status,
        ).thenReturn(ConnectionDiscoveryStatus.failed);
        when(
          () => viewModel.failure,
        ).thenReturn(ConnectionFailureReason.incompatibleHost);
        setDovahTestWindow(tester, dovahResponsiveTestSizes.last);
        await tester.pumpWidget(buildDialog());

        expect(
          find.text('This local Host version is not compatible with the app.'),
          findsOneWidget,
        );
        expect(find.text('No other Skyrim PCs found.'), findsNothing);
        expect(find.text('Skyrim isn’t running'), findsNothing);
      },
    );

    for (final ConnectionDiscoveryStatus status in [
      ConnectionDiscoveryStatus.empty,
      ConnectionDiscoveryStatus.failed,
    ]) {
      testWidgets('DiscoverDialog exposes retry semantics for $status state', (
        WidgetTester tester,
      ) async {
        when(() => viewModel.status).thenReturn(status);
        when(
          () => viewModel.failure,
        ).thenReturn(ConnectionFailureReason.hostUnavailable);
        final SemanticsHandle semantics = tester.ensureSemantics();
        try {
          setDovahTestWindow(tester, dovahResponsiveTestSizes.last);
          await tester.pumpWidget(buildDialog());

          final String message = status == ConnectionDiscoveryStatus.empty
              ? 'No other Skyrim PCs found.'
              : 'Could not reach the local Host. Check that it is running and try again.';
          expect(
            tester.getSemantics(find.bySemanticsLabel(message)),
            isSemantics(label: message, isLiveRegion: true),
          );
          expect(
            tester.getSemantics(find.bySemanticsLabel('Search again')),
            isSemantics(
              label: 'Search again',
              isButton: true,
              isEnabled: true,
              hasTapAction: true,
            ),
          );
        } finally {
          semantics.dispose();
        }
      });
    }
  });

  group('DiscoverDialog remains usable across themes and sizes', () {
    for (final DovahThemePreset preset in DovahThemePreset.values) {
      for (final Size size in dovahResponsiveTestSizes) {
        testWidgets(
          'DiscoverDialog lays out available candidates under $preset at $size without overflow',
          (WidgetTester tester) async {
            when(
              () => viewModel.status,
            ).thenReturn(ConnectionDiscoveryStatus.available);
            when(() => viewModel.candidates).thenReturn([candidate]);
            setDovahTestWindow(tester, size);
            await tester.pumpWidget(buildDialog(preset: preset));

            expect(tester.takeException(), isNull);
            expect(find.byType(DovahDialog), findsOneWidget);
            expect(find.byType(DiscoverCandidateCard), findsOneWidget);
            expect(
              tester.getSize(find.byType(DovahDialog)).width,
              size.width * DovahDialogMetrics.widthFraction <
                      DovahDialogMetrics.maxWidth
                  ? size.width * DovahDialogMetrics.widthFraction
                  : DovahDialogMetrics.maxWidth,
            );
          },
        );
      }
    }
  });

  group('DiscoverDialog retry states remain usable across themes and sizes', () {
    for (final ConnectionDiscoveryStatus status in [
      ConnectionDiscoveryStatus.empty,
      ConnectionDiscoveryStatus.failed,
    ]) {
      for (final DovahThemePreset preset in DovahThemePreset.values) {
        for (final Size size in dovahResponsiveTestSizes) {
          testWidgets(
            'DiscoverDialog lays out $status under $preset at $size without overflow',
            (WidgetTester tester) async {
              when(() => viewModel.status).thenReturn(status);
              when(
                () => viewModel.failure,
              ).thenReturn(ConnectionFailureReason.hostUnavailable);
              setDovahTestWindow(tester, size);
              await tester.pumpWidget(buildDialog(preset: preset));

              expect(tester.takeException(), isNull);
              expect(find.byType(DovahDialog), findsOneWidget);
              expect(
                find.byKey(const Key('discover-retry-message')),
                findsOneWidget,
              );
              expect(
                find.byKey(const Key('discover-retry-button')),
                findsOneWidget,
              );
            },
          );
        }
      }
    }
  });
}
