import 'package:equatable/equatable.dart';

import 'package:dovahlink_client/shared/constants/enums.dart';

/// The app-owned result of asking Skyrim to redisplay the active pairing code.
final class PairingRenotifyResult extends Equatable {
  /// Whether the code was redisplayed, cooled down, or no challenge was owned.
  final PairingRenotifyOutcome outcome;

  /// Host-reported seconds after a redisplay or until another request is accepted.
  final int? retryAfterSeconds;

  /// Creates an app-owned redisplay result.
  /// @param outcome The typed Host response.
  /// @param retryAfterSeconds Host-reported retry interval, when provided.
  const PairingRenotifyResult({required this.outcome, this.retryAfterSeconds});

  /// See [Equatable.props].
  @override
  List<Object?> get props => [outcome, retryAfterSeconds];
}
