import 'package:equatable/equatable.dart';

import 'package:dovahlink_client/features/connection/domain/entities/host.entity.dart';
import 'package:dovahlink_client/shared/constants/enums.dart';

/// The display data one Host card shows, derived from a [HostEntity] by
/// `ConnectionSelectors.hostCardsSelector`. It carries the [host] a tap selects, so presentation
/// wording and status never enter the domain entity.
class HostCardViewModel extends Equatable {
  /// Creates a Host card's display data.
  const HostCardViewModel({
    required this.host,
    required this.title,
    required this.subtitle,
    required this.detail,
    required this.state,
  });

  /// The Host this card represents and selects.
  final HostEntity host;

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
