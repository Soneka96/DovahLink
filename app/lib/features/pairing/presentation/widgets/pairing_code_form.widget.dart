import 'package:flutter/material.dart';
import 'package:flutter/services.dart';

import 'package:dovahlink_client/features/pairing/presentation/widgets/pairing_code_boxes.widget.dart';
import 'package:dovahlink_client/shared/constants/constants.dart';
import 'package:dovahlink_client/shared/constants/enums.dart';
import 'package:dovahlink_client/shared/theme/dovah_theme_context.dart';
import 'package:dovahlink_client/shared/theme/dovah_theme_tokens.dart';
import 'package:dovahlink_client/shared/theme/widgets/dovah_button.widget.dart';

/// The pairing-code and optional device-name entry form shown while pairing is awaiting the user
/// to enter a code: [pairingCodeLength] digit boxes over one real text field, the message slot,
/// the device name, and the action row whose primary button stays disabled until the code is
/// complete.
class PairingCodeForm extends StatefulWidget {
  /// Called with the entered code and optional display name.
  final void Function(String code, String? displayName) onSubmit;

  /// A message from outside the form, such as the Host rejecting the last code, shown in the
  /// message slot until the user edits the code, or `null` for none.
  final String? errorMessage;

  /// Buttons laid out before the primary button in the action row, such as cancel.
  final List<Widget> secondaryActions;

  /// Creates a pairing code form.
  const PairingCodeForm({
    required this.onSubmit,
    this.errorMessage,
    this.secondaryActions = const <Widget>[],
    super.key,
  });

  /// See [StatefulWidget.createState].
  @override
  State<PairingCodeForm> createState() => _PairingCodeFormState();
}

/// State for [PairingCodeForm].
class _PairingCodeFormState extends State<PairingCodeForm> {
  /// Controls the code field.
  final TextEditingController _codeController = TextEditingController();

  /// Controls the optional display-name field.
  final TextEditingController _displayNameController = TextEditingController();

  /// Owns focus for the code field, fixing its place first in traversal
  /// order ahead of the display-name field.
  final FocusNode _codeFocusNode = FocusNode();

  /// Owns focus for the display-name field.
  final FocusNode _displayNameFocusNode = FocusNode();

  /// The message from a submit attempt with an incomplete code, cleared on the next edit, or
  /// `null`. Takes the message slot ahead of [PairingCodeForm.errorMessage].
  String? _incompleteCodeMessage;

  /// Whether the entered code has all [pairingCodeLength] digits.
  bool get _isComplete => _codeController.text.length == pairingCodeLength;

  /// See [State.initState].
  @override
  void initState() {
    super.initState();
    // Rebuild for the edited code (boxes and primary button), clearing a stale incomplete-code
    // message, and for focus changes (the focused box's halo).
    _codeController.addListener(
      () => setState(() => _incompleteCodeMessage = null),
    );
    _codeFocusNode.addListener(() => setState(() {}));
  }

  /// See [State.dispose].
  @override
  void dispose() {
    _codeController.dispose();
    _displayNameController.dispose();
    _codeFocusNode.dispose();
    _displayNameFocusNode.dispose();
    super.dispose();
  }

  /// Submits the code and device name, or, when the code is incomplete, shows why and returns
  /// focus to the code field.
  void _submit() {
    if (!_isComplete) {
      setState(() {
        _incompleteCodeMessage =
            'Enter the $pairingCodeLength-digit code shown in Skyrim.';
      });
      _codeFocusNode.requestFocus();
      return;
    }
    final String displayName = _displayNameController.text.trim();
    widget.onSubmit(
      _codeController.text,
      displayName.isEmpty ? null : displayName,
    );
  }

  /// See [State.build].
  @override
  Widget build(BuildContext context) {
    final DovahThemeTokens tokens = context.dovahTokens;
    final String? message = _incompleteCodeMessage ?? widget.errorMessage;

    return Column(
      mainAxisSize: MainAxisSize.min,
      children: [
        Text(
          'Enter the code shown in Skyrim.',
          style: TextStyle(color: tokens.textMuted),
        ),
        const SizedBox(height: DovahThemeTokens.spacing8),
        SizedBox(
          width: PairingCodeBoxes.width,
          height: DovahThemeTokens.pairingCodeBoxHeight,
          child: Stack(
            children: [
              PairingCodeBoxes(
                code: _codeController.text,
                isFocused: _codeFocusNode.hasFocus,
              ),
              Positioned.fill(
                child: Semantics(
                  label: 'Pairing code, $pairingCodeLength digits',
                  child: TextSelectionTheme(
                    data: const TextSelectionThemeData(
                      cursorColor: Colors.transparent,
                      selectionColor: Colors.transparent,
                      selectionHandleColor: Colors.transparent,
                    ),
                    child: TextField(
                      key: const Key('pairing-code-field'),
                      controller: _codeController,
                      focusNode: _codeFocusNode,
                      autofocus: true,
                      keyboardType: TextInputType.number,
                      autofillHints: const [AutofillHints.oneTimeCode],
                      inputFormatters: [
                        FilteringTextInputFormatter.digitsOnly,
                        LengthLimitingTextInputFormatter(pairingCodeLength),
                      ],
                      style: const TextStyle(color: Colors.transparent),
                      decoration: const InputDecoration(
                        border: InputBorder.none,
                        contentPadding: EdgeInsets.zero,
                        counterText: '',
                      ),
                      onSubmitted: (_) => _submit(),
                    ),
                  ),
                ),
              ),
            ],
          ),
        ),
        const SizedBox(height: DovahThemeTokens.spacing8),
        ConstrainedBox(
          constraints: const BoxConstraints(
            minHeight: DovahThemeTokens.formErrorMinHeight,
          ),
          child: Semantics(
            liveRegion: true,
            child: Text(
              message ?? '',
              key: const Key('pairing-code-message'),
              textAlign: TextAlign.center,
              style: TextStyle(
                color: tokens.danger,
                fontSize: DovahThemeTokens.formErrorFontSize,
              ),
            ),
          ),
        ),
        const SizedBox(height: DovahThemeTokens.spacing8),
        SizedBox(
          width: PairingCodeBoxes.width,
          child: TextField(
            key: const Key('pairing-display-name-field'),
            controller: _displayNameController,
            focusNode: _displayNameFocusNode,
            style: TextStyle(color: tokens.textPrimary),
            cursorColor: tokens.accentPrimary,
            decoration: InputDecoration(
              labelText: 'Device name (optional)',
              labelStyle: TextStyle(color: tokens.textMuted),
              isDense: true,
              filled: true,
              fillColor: tokens.background,
              enabledBorder: OutlineInputBorder(
                borderRadius: BorderRadius.circular(tokens.cornerRadius),
                borderSide: BorderSide(color: tokens.lineStrong),
              ),
              focusedBorder: OutlineInputBorder(
                borderRadius: BorderRadius.circular(tokens.cornerRadius),
                borderSide: BorderSide(color: tokens.accentPrimary),
              ),
            ),
            onSubmitted: (_) => _submit(),
          ),
        ),
        const SizedBox(height: DovahThemeTokens.spacing12),
        Wrap(
          alignment: WrapAlignment.center,
          runAlignment: WrapAlignment.center,
          spacing: DovahThemeTokens.dialogActionGap,
          runSpacing: DovahThemeTokens.dialogActionGap,
          // A DovahButton fills the width it is offered, so each action is laid out unconstrained
          // to size to its label and let the Wrap decide when to start a new row.
          children: [
            for (final Widget action in widget.secondaryActions)
              UnconstrainedBox(child: action),
            UnconstrainedBox(
              child: DovahButton(
                key: const Key('pairing-confirm-button'),
                label: 'Pair',
                variant: DovahButtonVariant.primary,
                onPressed: _isComplete ? _submit : null,
              ),
            ),
          ],
        ),
      ],
    );
  }
}
