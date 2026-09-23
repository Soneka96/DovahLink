import 'package:flutter/material.dart';

import 'package:dovahlink_client/shared/constants/enums.dart';
import 'package:dovahlink_client/shared/theme/dovah_theme_context.dart';
import 'package:dovahlink_client/shared/theme/dovah_theme_tokens.dart';
import 'package:dovahlink_client/shared/theme/widgets/dovah_surface.widget.dart';

/// A DovahLink themed button. Primary buttons use each preset's approved action fill and label
/// color; secondary buttons use the theme's raised material and primary text tone.
class DovahButton extends StatefulWidget {
  /// Creates a themed button.
  const DovahButton({
    required this.label,
    required this.onPressed,
    this.variant = DovahButtonVariant.primary,
    super.key,
  });

  /// The button's visible text.
  final String label;

  /// Called when the button is tapped, or `null` to render it disabled.
  final VoidCallback? onPressed;

  /// The button's visual emphasis.
  final DovahButtonVariant variant;

  /// Creates the state that tracks the prototype's primary-button hover treatment.
  @override
  State<DovahButton> createState() => _DovahButtonState();
}

/// Tracks pointer hover so primary buttons can apply the prototype's shared brightening and lift.
class _DovahButtonState extends State<DovahButton> {
  /// The shared CSS hover brightening amount.
  static const double _hoverBrightnessIncrease = 0.07;

  /// Whether the pointer is currently over the button.
  bool _isHovered = false;

  /// See [State.build].
  @override
  Widget build(BuildContext context) {
    final DovahThemeTokens tokens = context.dovahTokens;
    final EdgeInsets padding = EdgeInsets.symmetric(
      vertical: 12 * tokens.densityScale,
      horizontal: 17 * tokens.densityScale,
    );
    final bool enabled = widget.onPressed != null;

    final Widget surface = widget.variant == DovahButtonVariant.primary
        ? DovahSurface(
            gradient: tokens.primaryActionGradient,
            padding: padding,
            child: Center(
              widthFactor: 1,
              heightFactor: 1,
              child: Text(
                widget.label,
                style: TextStyle(
                  color: tokens.primaryActionForeground,
                  fontWeight: FontWeight.w800,
                ),
              ),
            ),
          )
        : DovahSurface(
            raised: true,
            padding: padding,
            child: Center(
              widthFactor: 1,
              heightFactor: 1,
              child: Text(
                widget.label,
                style: TextStyle(
                  color: tokens.textPrimary,
                  fontWeight: FontWeight.w700,
                ),
              ),
            ),
          );

    return MouseRegion(
      cursor: enabled ? SystemMouseCursors.click : SystemMouseCursors.basic,
      onEnter: enabled ? (_) => setState(() => _isHovered = true) : null,
      onExit: (_) => setState(() => _isHovered = false),
      child: TweenAnimationBuilder<double>(
        key: const Key('dovah-button-hover-effect'),
        tween: Tween<double>(
          begin: 1,
          end:
              enabled &&
                  widget.variant == DovahButtonVariant.primary &&
                  _isHovered
              ? 1 + _hoverBrightnessIncrease
              : 1,
        ),
        duration: MediaQuery.disableAnimationsOf(context)
            ? Duration.zero
            : const Duration(milliseconds: 160),
        curve: Curves.ease,
        builder: (BuildContext context, double brightness, Widget? child) {
          final Widget button = brightness == 1
              ? child!
              : ColorFiltered(
                  colorFilter: ColorFilter.matrix(<double>[
                    brightness,
                    0,
                    0,
                    0,
                    0,
                    0,
                    brightness,
                    0,
                    0,
                    0,
                    0,
                    0,
                    brightness,
                    0,
                    0,
                    0,
                    0,
                    0,
                    1,
                    0,
                  ]),
                  child: child!,
                );

          return Transform.translate(
            offset: Offset(0, -((brightness - 1) / _hoverBrightnessIncrease)),
            child: button,
          );
        },
        child: Opacity(
          opacity: enabled ? 1 : 0.46,
          child: Semantics(
            key: const Key('dovah-button-semantics'),
            button: true,
            enabled: enabled,
            label: widget.label,
            child: InkWell(
              onTap: widget.onPressed,
              mouseCursor: enabled
                  ? SystemMouseCursors.click
                  : SystemMouseCursors.basic,
              child: Builder(
                builder: (BuildContext context) {
                  final bool focused = Focus.of(context).hasPrimaryFocus;

                  return Container(
                    key: focused
                        ? const Key('dovah-button-focus-outline')
                        : null,
                    foregroundDecoration: focused
                        ? BoxDecoration(
                            border: Border.all(color: tokens.signal, width: 2),
                            borderRadius: BorderRadius.circular(
                              tokens.cornerRadius,
                            ),
                            boxShadow: <BoxShadow>[
                              BoxShadow(
                                color: tokens.focusRingTint,
                                blurRadius: 8,
                              ),
                            ],
                          )
                        : null,
                    child: ConstrainedBox(
                      constraints: const BoxConstraints(
                        minWidth: 48,
                        minHeight: 48,
                      ),
                      child: Center(child: surface),
                    ),
                  );
                },
              ),
            ),
          ),
        ),
      ),
    );
  }
}
