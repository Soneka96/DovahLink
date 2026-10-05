import 'package:equatable/equatable.dart';

import 'package:dovahlink_client_sdk/dovahlink_client.dart'
    show
        CharacterVital,
        CharacterVitalsState,
        DovahLinkStateStatus,
        StateSynchronization;

/// Safe display ratios and synchronization standing for the Overview vitals.
class SessionOverviewVitalsViewData extends Equatable {
  /// The Vitals synchronization standing retained for visual state treatment.
  final DovahLinkStateStatus status;

  /// The clamped Health ratio, or `null` when it has no usable maximum.
  final double? healthRatio;

  /// The clamped Magicka ratio, or `null` when it has no usable maximum.
  final double? magickaRatio;

  /// The clamped Stamina ratio, or `null` when it has no usable maximum.
  final double? staminaRatio;

  /// Creates presentation ratios from one SDK Vitals synchronization value.
  /// @param status The synchronization standing for the complete Vitals group.
  /// @param healthRatio The safe Health ratio, if available.
  /// @param magickaRatio The safe Magicka ratio, if available.
  /// @param staminaRatio The safe Stamina ratio, if available.
  const SessionOverviewVitalsViewData({
    required this.status,
    required this.healthRatio,
    required this.magickaRatio,
    required this.staminaRatio,
  });

  /// Derives safe ratios without replacing the SDK synchronization value.
  /// @param synchronization The SDK Vitals synchronization value.
  /// @return Presentation ratios and the original synchronization status.
  factory SessionOverviewVitalsViewData.fromSynchronization(
    StateSynchronization<CharacterVitalsState> synchronization,
  ) => SessionOverviewVitalsViewData(
    status: synchronization.status,
    healthRatio: _progress(synchronization.value?.health),
    magickaRatio: _progress(synchronization.value?.magicka),
    staminaRatio: _progress(synchronization.value?.stamina),
  );

  /// See [Equatable.props].
  @override
  List<Object?> get props => [status, healthRatio, magickaRatio, staminaRatio];
}

/// Returns a clamped current-to-maximum ratio for a usable vital.
double? _progress(CharacterVital? vital) {
  if (vital == null ||
      !vital.current.isFinite ||
      !vital.max.isFinite ||
      vital.max <= 0) {
    return null;
  }
  return (vital.current / vital.max).clamp(0.0, 1.0).toDouble();
}
