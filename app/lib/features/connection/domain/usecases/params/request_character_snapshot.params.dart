import 'package:equatable/equatable.dart';
import 'package:meta/meta.dart';

/// Parameters for requesting a character snapshot.
@immutable
class RequestCharacterSnapshotParams extends Equatable {
  /// Last accepted revision, when recovery is incremental.
  final int? knownRevision;

  /// Creates snapshot-request parameters.
  const RequestCharacterSnapshotParams({this.knownRevision});

  @override
  List<Object?> get props => [knownRevision];
}
