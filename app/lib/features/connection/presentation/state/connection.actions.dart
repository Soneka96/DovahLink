import 'package:equatable/equatable.dart';

import 'package:dovahlink_client/features/connection/domain/entities/host.entity.dart';

/// Records the Host the user selected to pair or connect with.
class ConnectionHostSelectedAction extends Equatable {
  /// The Host the user selected.
  final Host host;

  /// Creates a Host-selection action.
  const ConnectionHostSelectedAction(this.host);

  /// See [Equatable.props].
  @override
  List<Object?> get props => [host];
}
