import 'package:flutter/material.dart';

import 'package:dovahlink_client/shared/theme/dovah_connection_card_metrics.dart';
import 'package:dovahlink_client/shared/theme/dovah_connection_card_theme_metrics.dart';
import 'package:dovahlink_client/shared/theme/dovah_dialog_metrics.dart';
import 'package:dovahlink_client/shared/theme/dovah_dialog_theme_metrics.dart';
import 'package:dovahlink_client/shared/theme/dovah_overview_metrics.dart';
import 'package:dovahlink_client/shared/theme/dovah_overview_theme_metrics.dart';
import 'package:dovahlink_client/shared/theme/dovah_page_metrics.dart';
import 'package:dovahlink_client/shared/theme/dovah_page_theme_metrics.dart';
import 'package:dovahlink_client/shared/theme/dovah_root_metrics.dart';
import 'package:dovahlink_client/shared/theme/dovah_root_theme_metrics.dart';
import 'package:dovahlink_client/shared/theme/dovah_session_metrics.dart';
import 'package:dovahlink_client/shared/theme/dovah_session_theme_metrics.dart';
import 'package:dovahlink_client/shared/theme/dovah_theme_tokens.dart';
import 'package:dovahlink_client/shared/theme/materials/dovah_theme_materials.dart';

/// Reads the active [DovahThemeTokens] from the nearest [Theme]. Every shared DovahLink surface
/// reads its look through this accessor rather than resolving [DovahThemeTokens] a different way.
extension DovahThemeContext on BuildContext {
  /// The active theme's [DovahThemeTokens]. Throws if no DovahLink preset theme is in scope,
  /// which is a composition error: every DovahLink theme attaches this extension.
  DovahThemeTokens get dovahTokens =>
      Theme.of(this).extension<DovahThemeTokens>()!;

  /// The active theme's [DovahThemeMaterials]. Throws if no DovahLink preset theme is in scope.
  DovahThemeMaterials get dovahMaterials =>
      Theme.of(this).extension<DovahThemeMaterials>()!;

  /// The [DovahDialogMetrics] for the active theme and the size of the window this context is
  /// shown in.
  DovahDialogMetrics get dovahDialogMetrics => DovahDialogMetrics.forWindow(
    themeMetrics: Theme.of(this).extension<DovahDialogThemeMetrics>()!,
    window: MediaQuery.sizeOf(this),
  );

  /// The [DovahRootMetrics] for the active theme and the size of the window this context is shown
  /// in. It follows the theme's own transition, so its measurements animate with the theme.
  DovahRootMetrics get dovahRootMetrics => DovahRootMetrics.forWindow(
    themeMetrics: Theme.of(this).extension<DovahRootThemeMetrics>()!,
    window: MediaQuery.sizeOf(this),
  );

  /// The [DovahConnectionCardMetrics] for the active theme and the size of the window this context
  /// is shown in.
  DovahConnectionCardMetrics get dovahConnectionCardMetrics =>
      DovahConnectionCardMetrics.forWindow(
        themeMetrics: Theme.of(
          this,
        ).extension<DovahConnectionCardThemeMetrics>()!,
        window: MediaQuery.sizeOf(this),
      );

  /// The [DovahPageMetrics] for the active theme and the size of the window this context is shown
  /// in.
  DovahPageMetrics get dovahPageMetrics => DovahPageMetrics.forWindow(
    themeMetrics: Theme.of(this).extension<DovahPageThemeMetrics>()!,
    window: MediaQuery.sizeOf(this),
  );

  /// The [DovahSessionMetrics] for the active theme and the size of the window this context is
  /// shown in.
  DovahSessionMetrics get dovahSessionMetrics => DovahSessionMetrics.forWindow(
    themeMetrics: Theme.of(this).extension<DovahSessionThemeMetrics>()!,
    window: MediaQuery.sizeOf(this),
  );

  /// The [DovahOverviewMetrics] for the active theme and the size of the window this context is
  /// shown in.
  DovahOverviewMetrics get dovahOverviewMetrics =>
      DovahOverviewMetrics.forWindow(
        themeMetrics: Theme.of(this).extension<DovahOverviewThemeMetrics>()!,
        window: MediaQuery.sizeOf(this),
      );
}
