import 'package:flutter/material.dart';

import 'package:dovahlink_client/shared/constants/enums.dart';
import 'package:dovahlink_client/shared/theme/dovah_control_metrics.dart';
import 'package:dovahlink_client/shared/theme/dovah_theme_context.dart';
import 'package:dovahlink_client/shared/theme/dovah_theme_tokens.dart';
import 'package:dovahlink_client/shared/theme/materials/dovah_color_filter.dart';
import 'package:dovahlink_client/shared/theme/widgets/dovah_focus_ring.widget.dart';
import 'package:dovahlink_client/shared/theme/widgets/dovah_surface.widget.dart';

/// A DovahLink themed button. Primary buttons use each preset's approved primary-action material
/// and label color; secondary buttons use the theme's control material and primary text tone.
class DovahButton extends StatefulWidget {
  /// The button's visible text.
  final String label;

  /// Called when the button is tapped, or `null` to render it disabled.
  final VoidCallback? onPressed;

  /// The button's visual emphasis.
  final DovahButtonVariant variant;

  /// An optional icon shown before the label, in the label's color.
  final IconData? icon;

  /// Creates a themed button.
  const DovahButton({
    required this.label,
    required this.onPressed,
    this.variant = DovahButtonVariant.primary,
    this.icon,
    super.key,
  });

  /// Creates the state that tracks the prototype's primary-button hover treatment.
  @override
  State<DovahButton> createState() => _DovahButtonState();
}

/// Tracks pointer hover so primary buttons can apply the prototype's shared brightening and lift.
class _DovahButtonState extends State<DovahButton> {
  /// Whether the pointer is currently over the button.
  bool _isHovered = false;

  /// See [State.build].
  @override
  Widget build(BuildContext context) {
    final DovahThemeTokens tokens = context.dovahTokens;
    const EdgeInsets padding = EdgeInsets.symmetric(
      vertical: DovahControlMetrics.buttonVerticalPadding,
      horizontal: DovahControlMetrics.buttonHorizontalPadding,
    );
    final bool enabled = widget.onPressed != null;

    final bool primary = widget.variant == DovahButtonVariant.primary;
    final Color foreground = primary
        ? tokens.primaryActionForeground
        : tokens.textPrimary;
    final Text label = Text(
      widget.label,
      style: TextStyle(
        color: foreground,
        fontSize: DovahControlMetrics.buttonFontSize,
        fontWeight: primary ? FontWeight.w800 : FontWeight.w700,
        height: DovahThemeTokens.bodyLineHeight,
      ),
    );
    final Widget surface = DovahSurface(
      castsShadow: enabled || !primary,
      role: primary
          ? DovahMaterialRole.primaryAction
          : DovahMaterialRole.control,
      cornerRadius: primary ? tokens.primaryActionCornerRadius : null,
      padding: padding,
      child: Center(
        widthFactor: 1,
        heightFactor: 1,
        child: widget.icon == null
            ? label
            : Row(
                mainAxisSize: MainAxisSize.min,
                children: [
                  Icon(
                    widget.icon,
                    size: DovahControlMetrics.buttonIconSize,
                    color: foreground,
                  ),
                  const SizedBox(width: DovahControlMetrics.buttonIconGap),
                  label,
                ],
              ),
      ),
    );

    final Widget shownSurface = primary && !enabled
        ? ColorFiltered(
            colorFilter: const DovahColorFilter(
              saturate: DovahControlMetrics.disabledPrimarySaturation,
            ).toColorFilter(),
            child: surface,
          )
        : surface;

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
              ? 1 + DovahControlMetrics.primaryButtonHoverBrightness
              : 1,
        ),
        duration: MediaQuery.disableAnimationsOf(context)
            ? Duration.zero
            : DovahControlMetrics.buttonHoverDuration,
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
            offset: Offset(
              0,
              -((brightness - 1) /
                  DovahControlMetrics.primaryButtonHoverBrightness),
            ),
            child: button,
          );
        },
        child: Opacity(
          opacity: enabled ? 1 : DovahControlMetrics.disabledControlOpacity,
          child: Semantics(
            key: const Key('dovah-button-semantics'),
            excludeSemantics: true,
            button: true,
            enabled: enabled,
            label: widget.label,
            onTap: widget.onPressed,
            child: InkWell(
              onTap: widget.onPressed,
              mouseCursor: enabled
                  ? SystemMouseCursors.click
                  : SystemMouseCursors.basic,
              child: Builder(
                builder: (BuildContext context) {
                  final bool focused = Focus.of(context).hasPrimaryFocus;

                  return ConstrainedBox(
                    constraints: const BoxConstraints(
                      minWidth: DovahControlMetrics.minimumTapTargetSize,
                      minHeight: DovahControlMetrics.minimumTapTargetSize,
                    ),
                    child: Center(
                      child: DovahFocusRing(
                        focused: focused,
                        cornerRadius: primary
                            ? tokens.primaryActionCornerRadius
                            : tokens.cornerRadius,
                        child: shownSurface,
                      ),
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
