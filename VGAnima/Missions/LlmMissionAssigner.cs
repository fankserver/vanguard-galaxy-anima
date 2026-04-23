using System;
using System.Collections.Generic;
using Source.Galaxy.POI;
using Source.MissionSystem;
using VGAnima.Llm;
using VGAnima.Persistence;
// Alias to avoid confusion with Source.MissionSystem.Rewards.StoryMission.
using StoryMissionRegistry = Source.MissionSystem.StoryMission;

namespace VGAnima.Missions;

/// <summary>Builds + registers an LLM-authored mission at broker-injection
/// time, and — when a <see cref="PersistedBrokerRegistry"/> is wired in —
/// pushes a <see cref="PersistedEntry"/> describing the broker+mission so
/// it survives session restarts.
///
/// <para>Spec §7: calls <see cref="MissionFactoryFromJson.Build"/> to
/// materialize the Mission, wraps it in a <see cref="StoryMissionRegistry"/>
/// whose factory delegate returns the pre-built instance, and registers via
/// <c>StoryMission.Add</c>. Returns the minted storyId.</para>
///
/// <para>When the (registry, clock) ctor is used, the 5-arg
/// <see cref="Assign(LlmMissionBlock,int,SpaceStation,string,LlmStory)"/>
/// also writes an "offered"-state <see cref="PersistedEntry"/> carrying the
/// broker's seed + stationId + <see cref="LlmStory"/>. That's the minimum
/// needed for cross-session rehydration: the salesman's name / description
/// / gender all regenerate from the seed via vanilla, so nothing else
/// about the broker's cosmetic identity needs persisting.</para>
///
/// <para>Save/load caveat (spec §7): registered factories live in the
/// vanilla <c>StoryMission.allMissions</c> dict for the session's
/// remainder. A save mid-mission reloaded in the same session will
/// rehydrate; across sessions the factory is gone →
/// <see cref="System.Collections.Generic.KeyNotFoundException"/>. The
/// PersistedBrokerRegistry + sidecar write-path (this task onward) closes
/// that gap.</para></summary>
internal sealed class LlmMissionAssigner
{
    private readonly Action<StoryMissionRegistry> _register;
    private readonly PersistedBrokerRegistry? _registry;
    private readonly IClock? _clock;

    /// <summary>Production ctor — registers into the real vanilla registry
    /// via <c>StoryMission.Add</c>. No registry/clock yet; T15 wires them
    /// from <c>Plugin.Awake</c>.</summary>
    public LlmMissionAssigner()
        : this(StoryMissionRegistry.Add, registry: null, clock: null)
    { }

    /// <summary>Test ctor — injects the registration action so tests can
    /// observe without touching the real Unity-bound static dict. Omits
    /// registry/clock for back-compat with pre-persistence tests.</summary>
    public LlmMissionAssigner(Action<StoryMissionRegistry> register)
        : this(register, registry: null, clock: null)
    { }

    /// <summary>Persistence-aware ctor — on the 5-arg
    /// <see cref="Assign(LlmMissionBlock,int,SpaceStation,string,LlmStory)"/>
    /// also writes an "offered" <see cref="PersistedEntry"/> to
    /// <paramref name="registry"/>. Both <paramref name="registry"/> and
    /// <paramref name="clock"/> must be non-null for the push to occur.</summary>
    public LlmMissionAssigner(
        Action<StoryMissionRegistry> register,
        PersistedBrokerRegistry? registry,
        IClock? clock)
    {
        _register = register;
        _registry = registry;
        _clock    = clock;
    }

    /// <summary>Legacy 4-arg overload — back-compat for existing tests and
    /// any call site that hasn't yet switched to the 5-arg form. Does NOT
    /// push to the registry (no broker story available).</summary>
    public string Assign(
        LlmMissionBlock block,
        int missionLevel,
        SpaceStation? brokerStation,
        string brokerSeed)
    {
        return AssignCore(block, missionLevel, brokerStation, brokerSeed, brokerStory: null);
    }

    /// <summary>Full form — pushes an "offered" <see cref="PersistedEntry"/>
    /// when a registry + clock are wired. Broker story is captured so
    /// rehydration after load can restore the dialogue lines without a
    /// fresh LLM call; everything else (name, description, gender,
    /// portrait) is derivable from the salesman seed at rehydration time.</summary>
    public string Assign(
        LlmMissionBlock block,
        int missionLevel,
        SpaceStation? brokerStation,
        string brokerSeed,
        LlmStory brokerStory,
        // Display-string snapshots saved onto the PersistedBroker so the
        // journal can build human-readable "last mission" references after
        // the broker is gone from the world. Nullable for back-compat
        // with callers that don't have the names handy (tests).
        string? brokerName  = null,
        string? systemName  = null,
        // Required whenever the block contains a deliver_to_station or
        // haul_goods intent — factory resolves each DestinationShortId →
        // the station's live GUID via this list.
        IReadOnlyList<AccessibleDestination>? accessibleDestinations = null)
    {
        return AssignCore(block, missionLevel, brokerStation, brokerSeed, brokerStory,
                          brokerName, systemName, accessibleDestinations);
    }

    private string AssignCore(
        LlmMissionBlock block,
        int missionLevel,
        SpaceStation? brokerStation,
        string brokerSeed,
        LlmStory? brokerStory,
        string? brokerName = null,
        string? systemName = null,
        IReadOnlyList<AccessibleDestination>? accessibleDestinations = null)
    {
        var mission = MissionFactoryFromJson.Build(
            block, missionLevel, brokerStation, brokerSeed, accessibleDestinations);

        var storyId = mission.storyId;
        var entry = new StoryMissionRegistry(
            storyId,
            _ => mission,           // factory always returns the pre-built instance
            available: null,        // always available
            pickupHint: "VGAnima Broker");

        _register(entry);

        if (_registry is not null && _clock is not null && brokerStory is not null)
        {
            var gameSec = _clock.GameSeconds;
            var utcIso  = _clock.UtcNow.ToString("yyyy-MM-ddTHH:mm:ssZ");
            var stationId = brokerStation?.guid ?? string.Empty;

            _registry.Add(new PersistedEntry(
                StoryId: storyId,
                State: PersistedEntryStates.Offered,
                MissionBlock: block,
                Broker: new PersistedBroker(
                    Seed: brokerSeed,
                    StationId: stationId,
                    Story: brokerStory,
                    NameSnapshot:        brokerName,
                    StationNameSnapshot: brokerStation?.name,
                    SystemNameSnapshot:  systemName ?? brokerStation?.system?.name),
                Timestamps: new PersistedTimestamps(
                    CreatedGameSeconds:  gameSec,
                    CreatedRealUtc:      utcIso,
                    LastSeenGameSeconds: gameSec,
                    LastSeenRealUtc:     utcIso)));
        }

        return storyId;
    }
}
