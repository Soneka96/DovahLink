import 'dart:async';

/// Broadcasts changes to one current value of type [T], per
/// `ai/context/sdk/api-design.md`'s "New-subscriber state replay": a new [stream] subscriber
/// receives [value] immediately, then every subsequent [update]. Not a Service -- a small,
/// no-interface supporting object per `ai/context/sdk/architecture.md`'s "Not everything is a
/// Service", reused wherever a typed SDK stream represents current state rather than a discrete
/// event log.
class CurrentValueStream<T> {
  /// Creates a stream whose current [value] starts at [initial].
  CurrentValueStream(T initial) : _value = initial;

  /// The most recently set value.
  T _value;

  /// Broadcasts every actual change to [value]; replayed to each new [stream] subscriber via
  /// [stream] itself, not by this controller directly.
  final StreamController<T> _controller = StreamController<T>.broadcast();

  /// The most recently set value.
  T get value => _value;

  /// Replaces [value] with [next] and notifies subscribers, unless [next] equals the current
  /// [value] -- an unchanged value is not a real transition and must not be re-announced.
  void update(T next) {
    if (next == _value) {
      return;
    }
    _value = next;
    _controller.add(next);
  }

  /// A broadcast stream of every value this instance has held: the current [value] immediately on
  /// listen, then each subsequent [update]. Built fresh per listener via [Stream.multi] so a late
  /// subscriber's replay never depends on when an earlier subscriber attached. No error or done
  /// event is ever forwarded: nothing in this class's public API can produce one, since [update]
  /// is the only way [_controller] is ever fed.
  Stream<T> get stream =>
      Stream<T>.multi((MultiStreamController<T> controller) {
        controller.add(_value);
        final StreamSubscription<T> subscription = _controller.stream.listen(
          controller.add,
        );
        controller.onCancel = subscription.cancel;
      });
}
