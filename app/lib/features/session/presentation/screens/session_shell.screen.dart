import 'dart:ui' as ui;

import 'package:flutter/material.dart';

import 'package:flutter_redux/flutter_redux.dart';
import 'package:redux/redux.dart';

import 'package:dovahlink_client/features/appearance/presentation/sections/appearance.section.dart';
import 'package:dovahlink_client/features/session/presentation/state/viewmodels/session_shell.viewmodel.dart';
import 'package:dovahlink_client/injection_container.dart';
import 'package:dovahlink_client/shared/constants/enums.dart';
import 'package:dovahlink_client/shared/state/app_state.dart';
import 'package:dovahlink_client/shared/theme/dovah_session_metrics.dart';
import 'package:dovahlink_client/shared/theme/dovah_theme_context.dart';
import 'package:dovahlink_client/shared/theme/dovah_theme_tokens.dart';
import 'package:dovahlink_client/shared/theme/widgets/dovah_button.widget.dart';
import 'package:dovahlink_client/shared/theme/widgets/dovah_dialog.widget.dart';
import 'package:dovahlink_client/shared/theme/widgets/dovah_environment_background.widget.dart';
import 'package:dovahlink_client/shared/theme/widgets/dovah_icon_button.widget.dart';
import 'package:dovahlink_client/shared/theme/widgets/dovah_sigil.widget.dart';

/// The minimal shell shown while the SDK reports an admitted Known Host session.
class SessionShellScreen extends StatelessWidget {
  /// The stable identity of the Known Host represented by this route.
  final String hostId;

  /// Creates the Session Shell for [hostId].
  const SessionShellScreen({required this.hostId, super.key});

  /// See [StatelessWidget.build].
  @override
  Widget build(
    BuildContext context,
  ) => StoreConnector<AppState, SessionShellViewModel>(
    distinct: true,
    converter: (Store<AppState> store) =>
        sl<SessionShellViewModel>(param1: store, param2: hostId),
    builder: (BuildContext context, SessionShellViewModel viewModel) {
      final DovahThemeTokens tokens = context.dovahTokens;
      final DovahSessionMetrics metrics = context.dovahSessionMetrics;
      final String hostName = viewModel.host?.title ?? 'Host unavailable';
      final DovahConnectionCardState status =
          viewModel.host?.state ?? DovahConnectionCardState.unknown;
      final Color statusColor = switch (status) {
        DovahConnectionCardState.connected ||
        DovahConnectionCardState.available => tokens.success,
        DovahConnectionCardState.reconnecting ||
        DovahConnectionCardState.connecting ||
        DovahConnectionCardState.repair => tokens.warning,
        DovahConnectionCardState.offline => tokens.statusOffline,
        DovahConnectionCardState.checking ||
        DovahConnectionCardState.unknown => tokens.textMuted,
      };
      final String shownHostName = tokens.uppercaseLabels
          ? hostName.toUpperCase()
          : hostName;
      return Scaffold(
        body: DovahEnvironmentBackground(
          child: Column(
            children: [
              ClipRect(
                child: BackdropFilter(
                  filter: ui.ImageFilter.blur(sigmaX: 11, sigmaY: 11),
                  child: Container(
                    key: const Key('session-shell-header'),
                    height: metrics.barHeight,
                    decoration: BoxDecoration(
                      color: tokens.surface.withValues(alpha: 0.88),
                      border: Border(
                        bottom: BorderSide(color: tokens.lineSubtle),
                      ),
                    ),
                    child: Center(
                      child: ConstrainedBox(
                        constraints: const BoxConstraints(
                          maxWidth: DovahSessionMetrics.barMaxWidth,
                        ),
                        child: Padding(
                          padding: EdgeInsets.symmetric(
                            horizontal: metrics.barSideMargin,
                          ),
                          child: Row(
                            children: [
                              DovahButton(
                                key: const Key('session-shell-back-button'),
                                label: 'Connections',
                                icon: Icons.chevron_left,
                                variant: DovahButtonVariant.quiet,
                                onPressed: viewModel.onBack,
                              ),
                              const SizedBox(width: DovahSessionMetrics.barGap),
                              Container(
                                width: DovahSessionMetrics.dividerWidth,
                                height: DovahSessionMetrics.dividerHeight,
                                color: tokens.lineSubtle,
                              ),
                              const SizedBox(width: DovahSessionMetrics.barGap),
                              const DovahSigil(
                                key: Key('session-shell-glyph'),
                                size: DovahSessionMetrics.glyphSize,
                              ),
                              const SizedBox(
                                width: DovahSessionMetrics.identityGap,
                              ),
                              Expanded(
                                child: Column(
                                  mainAxisAlignment: MainAxisAlignment.center,
                                  crossAxisAlignment: CrossAxisAlignment.start,
                                  children: [
                                    Text(
                                      shownHostName,
                                      key: const Key('session-shell-host-name'),
                                      maxLines: 1,
                                      overflow: TextOverflow.ellipsis,
                                      style: TextStyle(
                                        color: tokens.textPrimary,
                                        fontWeight: FontWeight.w800,
                                        fontSize:
                                            DovahSessionMetrics.nameFontSize,
                                      ),
                                    ),
                                  ],
                                ),
                              ),
                              const SizedBox(width: DovahSessionMetrics.barGap),
                              Container(
                                key: const Key('session-shell-status-dot'),
                                width: DovahSessionMetrics.statusDotSize,
                                height: DovahSessionMetrics.statusDotSize,
                                decoration: BoxDecoration(
                                  shape: BoxShape.circle,
                                  color: statusColor,
                                ),
                              ),
                              const SizedBox(
                                width: DovahSessionMetrics.statusGap,
                              ),
                              Text(
                                status.label,
                                key: const Key('session-shell-status'),
                                style: TextStyle(
                                  color: statusColor,
                                  fontSize: DovahSessionMetrics.statusFontSize,
                                  fontWeight: FontWeight.w700,
                                ),
                              ),
                              const SizedBox(
                                width: DovahSessionMetrics.actionsLeadingGap,
                              ),
                              Row(
                                mainAxisSize: MainAxisSize.min,
                                children: [
                                  if (metrics.showFirstAction) ...[
                                    // TODO: Notifications is prototype-only; implement its
                                    // surface and behavior in a future feature phase.
                                    DovahIconButton(
                                      icon: Icons.notifications_none_outlined,
                                      label: 'Notifications',
                                      size:
                                          DovahSessionMetrics.actionButtonSize,
                                      onPressed: () {},
                                    ),
                                    const SizedBox(
                                      width: DovahSessionMetrics.actionsGap,
                                    ),
                                  ],
                                  DovahIconButton(
                                    icon: Icons.settings_outlined,
                                    label: 'Appearance settings',
                                    size: DovahSessionMetrics.actionButtonSize,
                                    onPressed: () => DovahDialog.show<void>(
                                      context,
                                      title: 'Appearance',
                                      child: const AppearanceSection(),
                                    ),
                                  ),
                                ],
                              ),
                            ],
                          ),
                        ),
                      ),
                    ),
                  ),
                ),
              ),
              const Expanded(
                child: SizedBox.expand(key: Key('session-shell-empty-content')),
              ),
            ],
          ),
        ),
      );
    },
  );
}
