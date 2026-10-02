import 'package:equatable/equatable.dart';

import 'package:dovahlink_client/features/connection/domain/entities/host.entity.dart';
import 'package:dovahlink_client/shared/constants/enums.dart';

/// Immutable view data for one Host card, including the [Host] it selects.
class HostCardViewData extends Equatable {
  /// The Host this card represents and selects.
  final Host host;

  /// Whether selecting this card means using a discovery candidate or Known Host.
  final ConnectionHostSelectionSource source;

  /// The card's primary line, the Host's name.
  final String title;

  /// The card's secondary line, describing what kind of peer the Host is.
  final String subtitle;

  /// The card's trailing detail, where the Host is reached.
  final String detail;

  /// The card's visual state.
  final DovahConnectionCardState state;

  /// Whether the Known Host's saved SDK hint says pairing is required.
  final bool pairingRequired;

  /// Creates Host card view data.
  const HostCardViewData({
    /// The Host this card represents and selects.
    required this.host,

    /// Whether selecting this card means using a discovery candidate or Known Host.
    required this.source,

    /// The card's primary line.
    required this.title,

    /// The card's secondary line.
    required this.subtitle,

    /// The card's trailing detail.
    required this.detail,

    /// The card's visual state.
    required this.state,

    /// Whether the Known Host's saved SDK hint says pairing is required.
    this.pairingRequired = false,
  });

  /// See [Equatable.props].
  @override
  List<Object?> get props => [
    host,
    source,
    title,
    subtitle,
    detail,
    state,
    pairingRequired,
  ];
}
