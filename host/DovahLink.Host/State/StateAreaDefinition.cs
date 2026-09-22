namespace DovahLink.Host.State;

/// <summary>One <see cref="LiveStateCatalog"/>-defined authoritative state area and its canonical delivery mode.</summary>
/// <param name="Id">The state area's wire <c>stateArea</c> identifier.</param>
/// <param name="UpdateMode">The canonical live-delivery mode this area always uses.</param>
public sealed record StateAreaDefinition(StateAreaId Id, UpdateMode UpdateMode);
