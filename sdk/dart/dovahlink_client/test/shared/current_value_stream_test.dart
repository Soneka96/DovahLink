import 'dart:async';

import 'package:test/test.dart';

import 'package:dovahlink_client_sdk/src/shared/current_value_stream.dart';

/// Runs current-value-stream behavior tests.
void main() {
  group('Method constructor behaves correctly', () {
    test('Method constructor sets the initial value', () {
      final CurrentValueStream<int> stream = CurrentValueStream<int>(1);

      expect(stream.value, 1);
    });
  });

  group('Method update behaves correctly', () {
    test('Method update replaces the current value', () {
      final CurrentValueStream<int> stream = CurrentValueStream<int>(1);

      stream.update(2);

      expect(stream.value, 2);
    });

    test('Method update does not emit when the value is unchanged for a '
        'type with custom equality', () async {
      final CurrentValueStream<({int id})> stream =
          CurrentValueStream<({int id})>((id: 1));

      final Future<void> expectation = expectLater(
        stream.stream,
        emitsInOrder(<Object>[(id: 1), (id: 2)]),
      );
      // A new record with the same field values -- not the same instance -- must still count as
      // unchanged, since records compare structurally rather than by identity.
      stream.update((id: 1));
      stream.update((id: 2));

      await expectation;
    });

    test('Method update does not emit when the value is unchanged', () async {
      final CurrentValueStream<int> stream = CurrentValueStream<int>(1);

      // If update(1) incorrectly re-announced the unchanged value, the second element here would
      // be another 1 (the bogus emission) instead of 2, and emitsInOrder would fail.
      final Future<void> expectation = expectLater(
        stream.stream,
        emitsInOrder(<Object>[1, 2]),
      );
      stream.update(1);
      stream.update(2);

      await expectation;
    });
  });

  group('Property stream behaves correctly', () {
    test(
      'Property stream replays the current value immediately to a new subscriber',
      () async {
        final CurrentValueStream<int> stream = CurrentValueStream<int>(5);

        await expectLater(stream.stream, emits(5));
      },
    );

    test(
      'Property stream emits each subsequent update after the initial replay',
      () async {
        final CurrentValueStream<int> stream = CurrentValueStream<int>(1);

        final Future<void> expectation = expectLater(
          stream.stream,
          emitsInOrder(<Object>[1, 2, 3]),
        );
        stream.update(2);
        stream.update(3);

        await expectation;
      },
    );

    test(
      'Property stream replays the latest value, not history, to a late subscriber',
      () async {
        final CurrentValueStream<int> stream = CurrentValueStream<int>(1);
        stream.update(2);
        stream.update(3);

        await expectLater(stream.stream, emits(3));
      },
    );

    test(
      'Property stream gives each independent subscriber its own replay',
      () async {
        final CurrentValueStream<int> stream = CurrentValueStream<int>(1);

        final Future<void> first = expectLater(
          stream.stream,
          emitsInOrder(<Object>[1, 2]),
        );
        final Future<void> second = expectLater(
          stream.stream,
          emitsInOrder(<Object>[1, 2]),
        );
        stream.update(2);

        await Future.wait(<Future<void>>[first, second]);
      },
    );

    test('Property stream keeps a still-listening subscriber unaffected when '
        'another subscriber cancels', () async {
      final CurrentValueStream<int> stream = CurrentValueStream<int>(1);

      final Future<void> stillListening = expectLater(
        stream.stream,
        emitsInOrder(<Object>[1, 2]),
      );
      final StreamSubscription<int> cancelling = stream.stream.listen((_) {});
      await cancelling.cancel();
      stream.update(2);

      await stillListening;
    });
  });
}
