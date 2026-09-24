import 'package:equatable/equatable.dart';

import 'package:dovahlink_client/features/connection/domain/entities/host.entity.dart';
import 'package:dovahlink_client/shared/constants/enums.dart';

/// Immutable view data for one Host card, including the [Host] it selects.
class HostCardViewData extends Equatable {
  /// Creates Host card view data.
  const HostCardViewData({
    /// The Host this card represents and selects.
    required this.host,

    /// The card's primary line.
    required this.title,

    /// The card's secondary line.
    required this.subtitle,

    /// The card's trailing detail.
    required this.detail,

    /// The card's visual state.
    required this.state,
  });

  /// The Host this card represents and selects.
  final Host host;

  /// The card's primary line, the Host's name.
  final String title;

  /// The card's secondary line, describing what kind of peer the Host is.
  final String subtitle;

  /// The card's trailing detail, where the Host is reached.
  final String detail;

  /// The card's visual state.
  final DovahConnectionCardState state;

  /// See [Equatable.props].
  @override
  List<Object?> get props => [host, title, subtitle, detail, state];
}
