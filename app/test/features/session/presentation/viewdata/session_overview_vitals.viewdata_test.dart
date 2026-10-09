import 'package:flutter_test/flutter_test.dart';

import 'package:dovahlink_client/features/session/presentation/viewdata/session_overview_vitals.viewdata.dart';
import '../../../../fixtures/fixtures.dart';

import 'package:dovahlink_client_sdk/dovahlink_client.dart'
    show CharacterVitalsState, DovahLinkStateStatus;

/// Exercises safe ratios and synchronization treatment for Overview vitals.
void main() {
  group('Method fromSynchronization behaves correctly', () {
    test('Method fromSynchronization calculates all three vital ratios', () {
      final SessionOverviewVitalsViewData viewData =
          SessionOverviewVitalsViewData.fromSynchronization(
            Fixtures.buildStateSynchronization<CharacterVitalsState>(
              value: Fixtures.buildCharacterVitals(
                health: Fixtures.buildCharacterVital(current: 86, max: 100),
                magicka: Fixtures.buildCharacterVital(current: 62, max: 100),
                stamina: Fixtures.buildCharacterVital(current: 74, max: 100),
              ),
            ),
          );

      expect(viewData.healthRatio, closeTo(0.86, 0.0001));
      expect(viewData.magickaRatio, closeTo(0.62, 0.0001));
      expect(viewData.staminaRatio, closeTo(0.74, 0.0001));
      expect(viewData.healthCurrent, 86);
      expect(viewData.magickaCurrent, 62);
      expect(viewData.staminaCurrent, 74);
    });

    test('Method fromSynchronization retains precise current values', () {
      final SessionOverviewVitalsViewData
      viewData = SessionOverviewVitalsViewData.fromSynchronization(
        Fixtures.buildStateSynchronization<CharacterVitalsState>(
          value: Fixtures.buildCharacterVitals(
            health: Fixtures.buildCharacterVital(current: 19.01, max: 20.01),
            magicka: Fixtures.buildCharacterVital(current: 19.31, max: 20.31),
            stamina: Fixtures.buildCharacterVital(current: 19.99, max: 20.99),
          ),
        ),
      );

      expect(viewData.healthCurrent, 19.01);
      expect(viewData.magickaCurrent, 19.31);
      expect(viewData.staminaCurrent, 19.99);
      expect(viewData.healthRatio, closeTo(19.01 / 20.01, 0.0001));
      expect(viewData.magickaRatio, closeTo(19.31 / 20.31, 0.0001));
      expect(viewData.staminaRatio, closeTo(19.99 / 20.99, 0.0001));
    });

    test('Method fromSynchronization clamps values outside their bounds', () {
      final SessionOverviewVitalsViewData viewData =
          SessionOverviewVitalsViewData.fromSynchronization(
            Fixtures.buildStateSynchronization<CharacterVitalsState>(
              value: Fixtures.buildCharacterVitals(
                health: Fixtures.buildCharacterVital(current: 120, max: 100),
                magicka: Fixtures.buildCharacterVital(current: -20, max: 100),
                stamina: Fixtures.buildCharacterVital(current: 50, max: 100),
              ),
            ),
          );

      expect(viewData.healthRatio, 1);
      expect(viewData.magickaRatio, 0);
      expect(viewData.staminaRatio, 0.5);
    });

    test(
      'Method fromSynchronization omits invalid maxima and current values',
      () {
        final SessionOverviewVitalsViewData viewData =
            SessionOverviewVitalsViewData.fromSynchronization(
              Fixtures.buildStateSynchronization<CharacterVitalsState>(
                value: Fixtures.buildCharacterVitals(
                  health: Fixtures.buildCharacterVital(current: 50, max: 0),
                  magicka: Fixtures.buildCharacterVital(current: 50, max: -1),
                  stamina: Fixtures.buildCharacterVital(
                    current: double.nan,
                    max: 100,
                  ),
                ),
              ),
            );

        expect(viewData.healthRatio, isNull);
        expect(viewData.magickaRatio, isNull);
        expect(viewData.staminaRatio, isNull);
      },
    );

    test(
      'Method fromSynchronization omits non-finite maxima and current values',
      () {
        final SessionOverviewVitalsViewData viewData =
            SessionOverviewVitalsViewData.fromSynchronization(
              Fixtures.buildStateSynchronization<CharacterVitalsState>(
                value: Fixtures.buildCharacterVitals(
                  health: Fixtures.buildCharacterVital(
                    current: double.infinity,
                    max: 100,
                  ),
                  magicka: Fixtures.buildCharacterVital(
                    current: 50,
                    max: double.nan,
                  ),
                  stamina: Fixtures.buildCharacterVital(
                    current: 50,
                    max: double.infinity,
                  ),
                ),
              ),
            );

        expect(viewData.healthRatio, isNull);
        expect(viewData.magickaRatio, isNull);
        expect(viewData.staminaRatio, isNull);
      },
    );

    test('Method fromSynchronization preserves synchronization status', () {
      for (final DovahLinkStateStatus status in <DovahLinkStateStatus>[
        DovahLinkStateStatus.stale,
        DovahLinkStateStatus.recovering,
      ]) {
        final SessionOverviewVitalsViewData viewData =
            SessionOverviewVitalsViewData.fromSynchronization(
              Fixtures.buildStateSynchronization<CharacterVitalsState>(
                status: status,
                value: Fixtures.buildCharacterVitals(),
              ),
            );

        expect(viewData.status, status);
        expect(viewData.healthRatio, closeTo(0.8, 0.0001));
      }
    });

    test('Method fromSynchronization keeps unavailable vitals dormant', () {
      final SessionOverviewVitalsViewData viewData =
          SessionOverviewVitalsViewData.fromSynchronization(
            Fixtures.buildStateSynchronization<CharacterVitalsState>(
              status: DovahLinkStateStatus.unavailable,
              value: Fixtures.buildCharacterVitals(isAvailable: false),
            ),
          );

      expect(viewData.status, DovahLinkStateStatus.unavailable);
      expect(viewData.healthRatio, isNull);
      expect(viewData.magickaRatio, isNull);
      expect(viewData.staminaRatio, isNull);
    });

    test(
      'Method fromSynchronization does not invent ratios without a value',
      () {
        for (final DovahLinkStateStatus status in <DovahLinkStateStatus>[
          DovahLinkStateStatus.notSubscribed,
          DovahLinkStateStatus.failed,
        ]) {
          final SessionOverviewVitalsViewData viewData =
              SessionOverviewVitalsViewData.fromSynchronization(
                Fixtures.buildStateSynchronization<CharacterVitalsState>(
                  status: status,
                  value: null,
                ),
              );

          expect(viewData.status, status);
          expect(viewData.healthRatio, isNull);
          expect(viewData.magickaRatio, isNull);
          expect(viewData.staminaRatio, isNull);
        }
      },
    );
  });
}
