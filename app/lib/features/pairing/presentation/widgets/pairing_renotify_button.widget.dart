import 'dart:async';

import 'package:flutter/material.dart';

import 'package:flutter_redux/flutter_redux.dart';
import 'package:redux/redux.dart';

import 'package:dovahlink_client/features/pairing/presentation/state/viewmodels/pairing_renotify_button.viewmodel.dart';
import 'package:dovahlink_client/injection_container.dart';
import 'package:dovahlink_client/shared/constants/enums.dart';
import 'package:dovahlink_client/shared/state/app_state.dart';
import 'package:dovahlink_client/shared/theme/widgets/dovah_button.widget.dart';

/// Button to request pairing code redisplay, disabled when in cooldown. [PairingState.
/// renotifyAvailableAt] is a static absolute time (matching [PairingCountdown]'s own
/// no-clock-drift design), so this widget ticks its own timer the same way -- otherwise nothing
/// would trigger a rebuild once the cooldown elapses, and the button would stay disabled
/// indefinitely until an unrelated Redux dispatch happened to reshuffle state.
class PairingRenotifyButton extends StatefulWidget {
  /// Label displayed when button is enabled.
  final String label;

  /// Label displayed during cooldown; if null, shows "[label] (Xs)" format.
  final String? cooldownLabel;
  const PairingRenotifyButton({
    this.label = 'Send Code Again',
    this.cooldownLabel,
    super.key,
  });

  @override
  State<PairingRenotifyButton> createState() => _PairingRenotifyButtonState();
}

class _PairingRenotifyButtonState extends State<PairingRenotifyButton> {
  Timer? _timer;

  @override
  void initState() {
    super.initState();
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
    return StoreConnector<AppState, PairingRenotifyButtonViewModel>(
      distinct: true,
      converter: (Store<AppState> store) =>
          sl<PairingRenotifyButtonViewModel>(param1: store),
      builder: (context, viewModel) {
        return DovahButton(
          key: const Key('pairing-renotify-button'),
          label: viewModel.displayLabel(
            label: widget.label,
            cooldownLabel: widget.cooldownLabel,
          ),
          variant: DovahButtonVariant.secondary,
          onPressed: viewModel.onPressed,
        );
      },
    );
  }
}
