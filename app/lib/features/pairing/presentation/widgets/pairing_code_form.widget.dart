import 'package:flutter/material.dart';
import 'package:flutter/services.dart';

import 'package:dovahlink_client/features/pairing/presentation/widgets/pairing_code_boxes.widget.dart';
import 'package:dovahlink_client/features/pairing/presentation/widgets/pairing_message.widget.dart';
import 'package:dovahlink_client/shared/constants/constants.dart';
import 'package:dovahlink_client/shared/constants/enums.dart';
import 'package:dovahlink_client/shared/theme/dovah_dialog_metrics.dart';
import 'package:dovahlink_client/shared/theme/dovah_theme_context.dart';
import 'package:dovahlink_client/shared/theme/widgets/dovah_button.widget.dart';

/// The pairing-code entry form shown while pairing is awaiting the user to enter a code:
/// [pairingCodeLength] digit boxes over one real text field, the message slot, and the action row
/// whose primary button stays disabled until the code is complete.
class PairingCodeForm extends StatefulWidget {
  /// Called with the entered code.
  final void Function(String code) onSubmit;

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

  /// Owns focus for the code field, so a rejected submit can return focus to it.
  final FocusNode _codeFocusNode = FocusNode();

  /// The last code text used to distinguish edits from selection changes.
  String _lastText = '';

  /// Whether the code text changed since the current external error arrived.
  bool _editedSinceError = false;

  /// The message from a submit attempt with an incomplete code, cleared on the next controller
  /// change, or `null`. Takes the message slot ahead of [PairingCodeForm.errorMessage].
  String? _incompleteCodeMessage;

  /// Whether the entered code has all [pairingCodeLength] digits.
  bool get _isComplete => _codeController.text.length == pairingCodeLength;

  /// See [State.initState].
  @override
  void initState() {
    super.initState();
    // Rebuild for code/selection changes (boxes, button, and focus halo), clear local validation,
    // and hide an external error only after the text itself changes.
    _codeController.addListener(() {
      final String currentText = _codeController.text;
      final bool textChanged = currentText != _lastText;
      _lastText = currentText;
      setState(() {
        _incompleteCodeMessage = null;
        if (textChanged) {
          _editedSinceError = true;
        }
      });
    });
    _codeFocusNode.addListener(() => setState(() {}));
  }

  /// See [State.didUpdateWidget].
  @override
  void didUpdateWidget(covariant PairingCodeForm oldWidget) {
    super.didUpdateWidget(oldWidget);
    if (widget.errorMessage != oldWidget.errorMessage) {
      _editedSinceError = false;
    }
  }

  /// See [State.dispose].
  @override
  void dispose() {
    _codeController.dispose();
    _codeFocusNode.dispose();
    super.dispose();
  }

  /// Submits the code, or, when it is incomplete, shows why and returns focus to the code field.
  void _submit() {
    if (!_isComplete) {
      setState(() {
        _incompleteCodeMessage =
            'Enter the $pairingCodeLength-digit code shown in Skyrim.';
      });
      _codeFocusNode.requestFocus();
      return;
    }
    widget.onSubmit(_codeController.text);
  }

  /// See [State.build].
  @override
  Widget build(BuildContext context) {
    final DovahDialogMetrics metrics = context.dovahDialogMetrics;
    final String? message =
        _incompleteCodeMessage ??
        (_editedSinceError ? null : widget.errorMessage);

    return Column(
      mainAxisSize: MainAxisSize.min,
      children: [
        SizedBox(
          width: metrics.codeRowWidth,
          height: metrics.codeBoxHeight,
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
        SizedBox(height: metrics.codeRowBottomGap),
        PairingMessage(
          key: const Key('pairing-code-message'),
          message: message,
        ),
        SizedBox(height: metrics.actionsTopGap),
        Wrap(
          alignment: WrapAlignment.center,
          runAlignment: WrapAlignment.center,
          spacing: DovahDialogMetrics.actionGap,
          runSpacing: DovahDialogMetrics.actionGap,
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
