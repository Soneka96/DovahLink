import 'package:flutter/material.dart';

import 'package:dovahlink_client/shared/constants/enums.dart';
import 'package:dovahlink_client/shared/theme/dovah_preset_theme.dart';
import 'package:dovahlink_client/shared/theme/frostbound_theme.dart';
import 'package:dovahlink_client/shared/theme/hearth_theme.dart';

/// Returns the complete [ThemeData] -- Material theme plus the attached [DovahThemeTokens]
/// extension -- for the given [preset]. The one place that maps a [DovahThemePreset] to its
/// built theme; callers never construct a preset's [ThemeData] another way.
ThemeData dovahThemeDataFor(DovahThemePreset preset) => switch (preset) {
  DovahThemePreset.frostbound => buildFrostboundTheme(),
  DovahThemePreset.dovah => buildDovahPresetTheme(),
  DovahThemePreset.hearth => buildHearthTheme(),
};
