using DovahLink.Host.State;

namespace DovahLink.Host.Tests.TestDoubles;

/// <summary>One complete set of typed values for every production state area, as the Adapter would capture them.</summary>
/// <param name="Vitals">The <c>character_vitals</c> value.</param>
/// <param name="Xp">The <c>character_xp</c> value.</param>
/// <param name="Identity">The <c>character_identity</c> value.</param>
/// <param name="Traits">The <c>character_supernatural_traits</c> value.</param>
/// <param name="Location">The <c>player_location</c> value.</param>
/// <param name="GameTime">The <c>game_time</c> value.</param>
/// <param name="Quests">The <c>tracked_quests</c> value.</param>
/// <param name="Level">The <c>character_level</c> value.</param>
public sealed record ProductionStateValues(
    CharacterVitals? Vitals,
    float? Xp,
    CharacterIdentity? Identity,
    CharacterSupernaturalTraits? Traits,
    PlayerLocation? Location,
    GameTime? GameTime,
    TrackedQuests? Quests,
    ushort? Level);
