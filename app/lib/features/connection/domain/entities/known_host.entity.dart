import 'package:equatable/equatable.dart';

import 'package:dovahlink_client/features/connection/domain/entities/host.entity.dart';
import 'package:dovahlink_client/shared/constants/enums.dart';

/// A durable Known Host and its SDK-reported runtime availability.
final class KnownHost extends Equatable {
  /// The app-owned Host metadata and endpoint.
  final Host host;

  /// The SDK's app-mapped reachability evidence.
  final HostAvailability availability;

  /// Creates a Known Host runtime projection.
  /// @param host The durable Host metadata.
  /// @param availability The current runtime reachability evidence.
  const KnownHost({required this.host, required this.availability});

  /// See [Equatable.props].
  @override
  List<Object?> get props => [host, availability];
}
