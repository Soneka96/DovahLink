import 'package:equatable/equatable.dart';

import 'package:dovahlink_client/features/connection/domain/entities/host.entity.dart';

/// Requests navigating to pairing for the selected Host.
class ConnectionHostSelectedAction extends Equatable {
  /// Creates a Host-selection action.
  const ConnectionHostSelectedAction(this.host);

  /// The Host the user selected.
  final Host host;

  /// See [Equatable.props].
  @override
  List<Object?> get props => [host];
}
