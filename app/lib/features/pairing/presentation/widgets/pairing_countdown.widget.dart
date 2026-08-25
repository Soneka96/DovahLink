import 'dart:async';

import 'package:flutter/material.dart';
import 'package:flutter_redux/flutter_redux.dart';

import 'package:dovahlink_client/features/pairing/presentation/state/pairing.selectors.dart';
import 'package:dovahlink_client/shared/state/app_state.dart';

/// Displays a countdown timer for remaining seconds until pairing code expires.
/// Rebuilds periodically to update the display.
class PairingCountdown extends StatefulWidget {
  const PairingCountdown({
    this.textStyle,
    this.formatSeconds = _defaultFormatSeconds,
    super.key,
  });

  /// Optional text style for the countdown display.
  final TextStyle? textStyle;

  /// Function to format remaining seconds for display.
  final String Function(int) formatSeconds;

  static String _defaultFormatSeconds(int seconds) {
    final minutes = seconds ~/ 60;
    final secs = seconds % 60;
    return '$minutes:${secs.toString().padLeft(2, '0')}';
  }

  @override
  State<PairingCountdown> createState() => _PairingCountdownState();
}

class _PairingCountdownState extends State<PairingCountdown> {
  Timer? _timer;

  @override
  void initState() {
    super.initState();
    _startTimer();
  }

  void _startTimer() {
    _timer = Timer.periodic(const Duration(seconds: 1), (_) {
      if (mounted) {
        setState(() {});
      }
    });
  }

  @override
  void dispose() {
    _timer?.cancel();
    super.dispose();
  }

  @override
  Widget build(BuildContext context) {
    return StoreConnector<AppState, int?>(
      converter: (store) =>
          PairingSelectors.codeCountdownSecondsSelector(store.state),
      builder: (context, remainingSeconds) {
        if (remainingSeconds == null) {
          return const SizedBox.shrink();
        }
        return Text(
          widget.formatSeconds(remainingSeconds),
          style: widget.textStyle,
        );
      },
    );
  }
}
