import 'package:dovahlink_client_sdk/src/internal/state/state_domain_definition.dart';
import 'package:dovahlink_client_sdk/src/internal/state/state_revision_tracker.dart';
import 'package:dovahlink_client_sdk/src/protocol/json_map.dart';
import 'package:dovahlink_client_sdk/src/shared/current_value_stream.dart';
import 'package:dovahlink_client_sdk/src/state/state_synchronization.dart';

/// Owns one independently synchronized state domain: its tracker, typed stream, and definition.
///
/// Use this for a domain that stands alone. Related domains that share a public view keep a
/// grouping module, such as `CharacterStateModule`.
class SingleDomainStateModule<T> {
  /// The registration passed to the shared state-message handler and recovery composition.
  final StateDomainDefinition<T> domain;

  /// Creates the replayable tracker and the one definition that owns it.
  /// @param stateArea The canonical area name carried by the protocol payload.
  /// @param decode Converts area data into its typed state model.
  /// @param isUnavailable Identifies an explicit unavailable state value.
  /// @param supportsEvents Whether this area accepts protocol Events and so requires recovery.
  SingleDomainStateModule({
    required String stateArea,
    required T Function(JsonMap data) decode,
    required bool Function(T value) isUnavailable,
    bool supportsEvents = false,
  }) : domain = StateDomainDefinition<T>(
         stateArea: stateArea,
         decode: decode,
         tracker: StateRevisionTracker<T>(
           state: CurrentValueStream<StateSynchronization<T>>(
             StateSynchronization<T>.notSubscribed(),
           ),
         ),
         isUnavailable: isUnavailable,
         supportsEvents: supportsEvents,
       );

  /// The stream of typed values and synchronization status.
  Stream<StateSynchronization<T>> get changes => domain.tracker.changes;
}
