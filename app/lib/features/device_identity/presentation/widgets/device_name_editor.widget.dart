import 'dart:async';
import 'dart:convert';
import 'dart:math' as math;

import 'package:flutter/material.dart';
import 'package:flutter/services.dart';

import 'package:dovahlink_client/shared/constants/constants.dart';
import 'package:dovahlink_client/shared/constants/enums.dart';
import 'package:dovahlink_client/shared/theme/dovah_dialog_metrics.dart';
import 'package:dovahlink_client/shared/theme/dovah_theme_context.dart';
import 'package:dovahlink_client/shared/theme/dovah_theme_tokens.dart';
import 'package:dovahlink_client/shared/theme/widgets/dovah_button.widget.dart';

/// Edits a local device-name override and presents its separate Host outcome.
class DeviceNameEditor extends StatefulWidget {
  /// The resolved name to show in the field, or `null` if loading failed.
  final String? displayName;

  /// The user-safe failure from loading the saved preference.
  final String? loadFailure;

  /// Whether local persistence and any active-Host rename are pending.
  final bool isSaving;

  /// The local validation or persistence error, if one occurred.
  final String? saveFailure;

  /// The latest active-Host rename outcome, or `null` before a completed save.
  final DeviceNameRenameStatus? remoteRenameStatus;

  /// Called with the text currently in the field when Save or Enter is used.
  final ValueChanged<String> onSave;

  /// Creates the device-name input and Save action.
  /// @param displayName The resolved name to display.
  /// @param loadFailure The user-safe preference-load failure, if any.
  /// @param isSaving Whether a save operation is in progress.
  /// @param saveFailure The local validation or persistence failure, if any.
  /// @param remoteRenameStatus The latest active-Host rename result.
  /// @param onSave Receives the proposed name when the user saves.
  const DeviceNameEditor({
    required this.displayName,
    required this.onSave,
    this.loadFailure,
    this.isSaving = false,
    this.saveFailure,
    this.remoteRenameStatus,
    super.key,
  });

  /// See [StatefulWidget.createState].
  @override
  State<DeviceNameEditor> createState() => _DeviceNameEditorState();
}

/// Owns the local input, focus, and prototype's temporary Saved label.
class _DeviceNameEditorState extends State<DeviceNameEditor> {
  /// Controls the current input text.
  final TextEditingController _controller = TextEditingController();

  /// Controls keyboard focus for the name field.
  final FocusNode _focusNode = FocusNode();

  /// Hides the Saved label after the prototype's one-second interval.
  Timer? _savedLabelTimer;

  /// Whether the last completed save is still showing its temporary button label.
  bool _showSavedLabel = false;

  /// See [State.initState].
  @override
  void initState() {
    super.initState();
    _controller.text = widget.displayName ?? '';
  }

  /// Synchronizes persisted text after a successful local save.
  @override
  void didUpdateWidget(covariant DeviceNameEditor oldWidget) {
    super.didUpdateWidget(oldWidget);
    final String? persistedName = widget.displayName;
    if (oldWidget.isSaving &&
        !widget.isSaving &&
        widget.saveFailure == null &&
        persistedName != null) {
      _controller.value = TextEditingValue(
        text: persistedName,
        selection: TextSelection.collapsed(offset: persistedName.length),
      );
      _savedLabelTimer?.cancel();
      _showSavedLabel = true;
      _savedLabelTimer = Timer(const Duration(seconds: 1), () {
        if (mounted) {
          setState(() => _showSavedLabel = false);
        }
      });
    }
  }

  /// Disposes local input and timer resources.
  @override
  void dispose() {
    _savedLabelTimer?.cancel();
    _controller.dispose();
    _focusNode.dispose();
    super.dispose();
  }

  /// Builds the device-name input, Save action, and user-safe feedback.
  @override
  Widget build(BuildContext context) {
    final DovahThemeTokens tokens = context.dovahTokens;
    final InputBorder enabledBorder = OutlineInputBorder(
      borderRadius: BorderRadius.circular(tokens.cornerRadius),
      borderSide: BorderSide(color: tokens.lineStrong),
    );
    final Widget input = Semantics(
      label: 'Device name',
      textField: true,
      child: TextField(
        key: const Key('settings-device-name-input'),
        controller: _controller,
        focusNode: _focusNode,
        enabled: !widget.isSaving,
        textInputAction: TextInputAction.done,
        onSubmitted: (String value) {
          if (!widget.isSaving) {
            widget.onSave(value);
          }
        },
        onChanged: (String _) {
          _savedLabelTimer?.cancel();
          if (_showSavedLabel) {
            setState(() => _showSavedLabel = false);
          }
        },
        inputFormatters: [
          TextInputFormatter.withFunction((
            TextEditingValue oldValue,
            TextEditingValue newValue,
          ) {
            if (utf8.encode(newValue.text).length > maxDeviceNameLengthBytes) {
              return oldValue;
            }
            return newValue;
          }),
        ],
        style: TextStyle(color: tokens.textPrimary, fontSize: 14),
        cursorColor: tokens.accentPrimary,
        decoration: InputDecoration(
          isDense: true,
          filled: true,
          fillColor: tokens.surfaceRaised,
          contentPadding: const EdgeInsets.symmetric(
            horizontal: 10,
            vertical: 9,
          ),
          counterText: '',
          enabledBorder: enabledBorder,
          border: enabledBorder,
          focusedBorder: OutlineInputBorder(
            borderRadius: BorderRadius.circular(tokens.cornerRadius),
            borderSide: BorderSide(color: tokens.accentPrimary),
          ),
          disabledBorder: OutlineInputBorder(
            borderRadius: BorderRadius.circular(tokens.cornerRadius),
            borderSide: BorderSide(color: tokens.lineSubtle),
          ),
        ),
      ),
    );
    final Widget saveButton = DovahButton(
      key: const Key('settings-device-name-save'),
      label: widget.isSaving
          ? 'Saving…'
          : _showSavedLabel
          ? 'Saved'
          : 'Save',
      labelFontSize: DovahDialogMetrics.settingsSaveButtonFontSize,
      variant: DovahButtonVariant.secondary,
      onPressed: widget.isSaving ? null : () => widget.onSave(_controller.text),
    );
    final String? localFailure = widget.loadFailure ?? widget.saveFailure;
    final String? feedback =
        localFailure ??
        switch (widget.remoteRenameStatus) {
          null => null,
          DeviceNameRenameStatus.notAttempted =>
            'Saved locally. This name will be used for future pairings.',
          DeviceNameRenameStatus.renamed =>
            'Saved locally and confirmed by the active Host.',
          DeviceNameRenameStatus.invalidDisplayName =>
            'Saved locally, but the Host rejected the name.',
          DeviceNameRenameStatus.notTrusted =>
            'Saved locally, but the active Host no longer trusts this device.',
          DeviceNameRenameStatus.unconfirmed =>
            'Saved locally, but the Host did not confirm the rename.',
        };
    final Color feedbackColor =
        widget.loadFailure != null || widget.saveFailure != null
        ? tokens.danger
        : switch (widget.remoteRenameStatus) {
            DeviceNameRenameStatus.renamed => tokens.success,
            DeviceNameRenameStatus.notAttempted => tokens.textMuted,
            DeviceNameRenameStatus.invalidDisplayName ||
            DeviceNameRenameStatus.notTrusted ||
            DeviceNameRenameStatus.unconfirmed => tokens.warning,
            null => tokens.textMuted,
          };

    return LayoutBuilder(
      builder: (BuildContext context, BoxConstraints constraints) {
        final double controlWidth = math.min(
          DovahDialogMetrics.settingsDeviceNameInputWidth,
          constraints.maxWidth,
        );
        final Widget controls =
            constraints.maxWidth <
                DovahDialogMetrics.settingsDeviceControlsStackBreakpoint
            ? Column(
                crossAxisAlignment: CrossAxisAlignment.start,
                children: [
                  SizedBox(width: controlWidth, child: input),
                  const SizedBox(
                    height: DovahDialogMetrics.settingsDeviceControlGap,
                  ),
                  saveButton,
                ],
              )
            : Row(
                mainAxisSize: MainAxisSize.min,
                children: [
                  SizedBox(width: controlWidth, child: input),
                  const SizedBox(
                    width: DovahDialogMetrics.settingsDeviceControlGap,
                  ),
                  saveButton,
                ],
              );
        return Column(
          crossAxisAlignment: CrossAxisAlignment.start,
          mainAxisSize: MainAxisSize.min,
          children: [
            controls,
            if (feedback != null) ...[
              const SizedBox(
                height: DovahDialogMetrics.settingsDeviceFeedbackTopGap,
              ),
              Semantics(
                liveRegion: true,
                child: Text(
                  feedback,
                  style: TextStyle(
                    color: feedbackColor,
                    fontSize: DovahDialogMetrics.messageFontSize,
                    height: 1.35,
                  ),
                ),
              ),
            ],
          ],
        );
      },
    );
  }
}
