import 'package:flutter/material.dart';

import 'package:dovahlink_client/features/pairing/presentation/widgets/pairing_mark.widget.dart';
import 'package:dovahlink_client/features/pairing/presentation/widgets/pairing_message.widget.dart';
import 'package:dovahlink_client/features/pairing/presentation/widgets/pairing_request_code_button.widget.dart';
import 'package:dovahlink_client/features/pairing/presentation/widgets/pairing_state_layout.widget.dart';
import 'package:dovahlink_client/shared/theme/dovah_theme_tokens.dart';

/// The pairing state where the Host accepted the connection but this device is not paired yet:
/// the user asks for a code to be shown in Skyrim. Also carries the reason a previously trusted
/// device has to pair again, when there is one.
class PairingReady extends StatelessWidget {
  /// The Host's name, shown in the copy.
  final String hostName;

  /// Why pairing is needed again, or `null` when this is a first pairing.
  final String? message;

  /// Called when the user asks for a pairing code.
  final VoidCallback onRequestCode;

  /// Creates a ready-to-pair state.
  const PairingReady({
    required this.hostName,
    required this.message,
    required this.onRequestCode,
    super.key,
  });

  /// See [StatelessWidget.build].
  @override
  Widget build(BuildContext context) {
    return PairingStateLayout(
      mark: const PairingMark(icon: Icons.desktop_windows_outlined),
      heading: 'Pair this device',
      body:
          'Request a pairing code and DovahLink will show it inside Skyrim. Enter it to connect this device to ',
      highlight: hostName,
      bodyEnd: '.',
      children: [
        PairingMessage(key: const Key('pairing-message'), message: message),
        const SizedBox(height: DovahThemeTokens.spacing8),
        PairingRequestCodeButton(onRequestCode: onRequestCode),
      ],
    );
  }
}
