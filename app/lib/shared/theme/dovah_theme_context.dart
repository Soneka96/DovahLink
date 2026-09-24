import 'package:flutter/material.dart';

import 'package:dovahlink_client/shared/theme/dovah_dialog_metrics.dart';
import 'package:dovahlink_client/shared/theme/dovah_theme_tokens.dart';

/// Reads the active [DovahThemeTokens] from the nearest [Theme]. Every shared DovahLink surface
/// reads its look through this accessor rather than resolving [DovahThemeTokens] a different way.
extension DovahThemeContext on BuildContext {
  /// The active theme's [DovahThemeTokens]. Throws if no DovahLink preset theme is in scope,
  /// which is a composition error: every DovahLink theme attaches this extension.
  DovahThemeTokens get dovahTokens =>
      Theme.of(this).extension<DovahThemeTokens>()!;

  /// The [DovahDialogMetrics] for the height of the window this context is shown in.
  DovahDialogMetrics get dovahDialogMetrics =>
      DovahDialogMetrics.forWindowHeight(MediaQuery.sizeOf(this).height);
}
