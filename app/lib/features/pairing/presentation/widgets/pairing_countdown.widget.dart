import 'dart:async';

import 'package:flutter/material.dart';

import 'package:flutter_redux/flutter_redux.dart';
import 'package:redux/redux.dart';

import 'package:dovahlink_client/features/pairing/presentation/state/viewmodels/pairing_countdown.viewmodel.dart';
import 'package:dovahlink_client/injection_container.dart';
import 'package:dovahlink_client/shared/state/app_state.dart';

/// Displays a countdown timer for remaining seconds until pairing code expires.
/// Rebuilds periodically to update the display.
class PairingCountdown extends StatefulWidget {
  /// Optional text style for the countdown display.
  final TextStyle? textStyle;

  /// Function to format remaining seconds for display.
  final String Function(int) formatSeconds;

  /// Text shown before the formatted time, such as `Code expires in `.
  final String label;
  const PairingCountdown({
    this.textStyle,
    this.label = '',
    this.formatSeconds = _defaultFormatSeconds,
    super.key,
  });

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
    return StoreConnector<AppState, PairingCountdownViewModel>(
      distinct: true,
      converter: (Store<AppState> store) =>
          sl<PairingCountdownViewModel>(param1: store),
      builder: (context, viewModel) {
        final int? remainingSeconds = viewModel.remainingSeconds;
        if (remainingSeconds == null) {
          return const SizedBox.shrink();
        }
        return Text(
          '${widget.label}${widget.formatSeconds(remainingSeconds)}',
          style: widget.textStyle,
        );
      },
    );
  }
}
