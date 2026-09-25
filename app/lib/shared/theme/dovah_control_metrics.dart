/// The geometry and interaction constants shared by every themed control (buttons and icon
/// buttons), the same in every theme and at every window size in the approved prototype. Values
/// that vary by theme or window size do not belong here; they live in a resolved metrics class.
abstract final class DovahControlMetrics {
  /// Vertical padding of a themed button (the prototype's `.primary` `padding:12px 17px`).
  static const double buttonVerticalPadding = 12;

  /// Horizontal padding of a themed button (the prototype's `.primary` `padding:12px 17px`).
  static const double buttonHorizontalPadding = 17;

  /// Horizontal padding of a themed secondary button: the prototype's `.secondary`
  /// `padding:11px 15px` plus its 1px border, which the material paints inside the padding here.
  static const double secondaryButtonHorizontalPadding = 16;

  /// Vertical padding of a themed quiet button: the prototype's `.pair-tools .secondary`
  /// `padding:7px 10px` plus its 1px transparent border.
  static const double quietButtonVerticalPadding = 8;

  /// Horizontal padding of a themed quiet button: the prototype's `.pair-tools .secondary`
  /// `padding:7px 10px` plus its 1px transparent border.
  static const double quietButtonHorizontalPadding = 11;

  /// Font size of a themed quiet button's label (the prototype's `.pair-tools .secondary`).
  static const double quietButtonFontSize = 12;

  /// Font size of a themed button's label (the prototype's inherited 16px button font).
  static const double buttonFontSize = 16;

  /// Size of the icon inside a themed button (the prototype's `.primary svg`).
  static const double buttonIconSize = 17;

  /// Gap between a themed button's icon and its label (the prototype's `.primary` `gap:9px`).
  static const double buttonIconGap = 9;

  /// Width and height of a themed icon-only button's visible surface (the prototype's
  /// `.icon-btn`).
  static const double iconButtonSize = 40;

  /// Size of the icon inside a themed icon-only button (the prototype's `.icon-btn svg`).
  static const double iconButtonIconSize = 19;

  /// Brightness increase applied to hovered primary buttons (the prototype's
  /// `.primary:hover{filter:brightness(1.07)}`).
  static const double primaryButtonHoverBrightness = 0.07;

  /// Duration of the primary-button hover transition (the prototype's `transition:.16s ease`).
  static const Duration buttonHoverDuration = Duration(milliseconds: 160);

  /// Duration of the hover slide of a card (the prototype's `transition:transform .18s ease`).
  static const Duration liftDuration = Duration(milliseconds: 180);

  /// Disabled-control opacity (the prototype's `.primary:disabled{opacity:.46}`).
  static const double disabledControlOpacity = 0.46;

  /// Saturation of a disabled primary button (the prototype's
  /// `.primary:disabled{filter:saturate(.45)}`).
  static const double disabledPrimarySaturation = 0.45;

  /// Width of themed focus outlines (the prototype's `:focus-visible` 2px outline).
  static const double focusOutlineWidth = 2;

  /// Gap between a focused control and its outline (the prototype's `outline-offset:3px`).
  static const double focusOutlineOffset = 3;

  /// Minimum interactive target width and height for themed controls. An accessibility floor, not
  /// a prototype value.
  static const double minimumTapTargetSize = 48;
}
