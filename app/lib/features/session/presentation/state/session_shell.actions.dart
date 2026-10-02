import 'package:equatable/equatable.dart';

/// Requests leaving the Session Shell for Connections without disconnecting the Host.
class SessionShellBackRequestedAction extends Equatable {
  /// Creates a Session Shell Back request.
  const SessionShellBackRequestedAction();

  /// See [Equatable.props].
  @override
  List<Object?> get props => const <Object?>[];
}
