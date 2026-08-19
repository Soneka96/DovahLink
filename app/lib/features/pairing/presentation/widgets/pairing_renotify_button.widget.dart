import 'dart:async';

import 'package:flutter/material.dart';
import 'package:flutter_redux/flutter_redux.dart';

import 'package:dovahlink_client/features/pairing/presentation/state/pairing.actions.dart';
import 'package:dovahlink_client/features/pairing/presentation/state/pairing.selectors.dart';
import 'package:dovahlink_client/shared/state/app_state.dart';

/// Button to request pairing code redisplay, disabled when in cooldown. [PairingState.
/// renotifyAvailableAt] is a static absolute time (matching [PairingCountdown]'s own
/// no-clock-drift design), so this widget ticks its own timer the same way -- otherwise nothing
/// would trigger a rebuild once the cooldown elapses, and the button would stay disabled
/// indefinitely until an unrelated Redux dispatch happened to reshuffle state.
class PairingRenotifyButton extends StatefulWidget {
  const PairingRenotifyButton({
    this.label = 'Send Code Again',
    this.cooldownLabel,
    super.key,
  });

  /// Label displayed when button is enabled.
  final String label;

  /// Label displayed during cooldown; if null, shows "[label] (Xs)" format.
  final String? cooldownLabel;

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
    return StoreConnector<AppState, _RenotifyButtonViewModel>(
      converter: (store) {
        final cooldownSeconds =
            PairingSelectors.renotifyCooldownSecondsSelector(store.state);
        final isAvailable = cooldownSeconds == null || cooldownSeconds == 0;
        return _RenotifyButtonViewModel(
          isAvailable: isAvailable,
          cooldownSeconds: cooldownSeconds,
          onPressed: isAvailable
              ? () => store.dispatch(const PairingRenotifyRequestedAction())
              : null,
        );
      },
      builder: (context, viewModel) {
        final buttonLabel = _buildLabel(viewModel);
        return ElevatedButton(
          onPressed: viewModel.onPressed,
          child: Text(buttonLabel),
        );
      },
    );
  }

  String _buildLabel(_RenotifyButtonViewModel viewModel) {
    if (viewModel.isAvailable) {
      return widget.label;
    }
    if (widget.cooldownLabel != null) {
      return widget.cooldownLabel!;
    }
    return '${widget.label} (${viewModel.cooldownSeconds}s)';
  }
}

class _RenotifyButtonViewModel {
  _RenotifyButtonViewModel({
    required this.isAvailable,
    required this.cooldownSeconds,
    required this.onPressed,
  });

  final bool isAvailable;
  final int? cooldownSeconds;
  final VoidCallback? onPressed;
}
