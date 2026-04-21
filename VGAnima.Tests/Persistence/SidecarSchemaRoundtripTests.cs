using System.Collections.Generic;
using Newtonsoft.Json;
using VGAnima.Llm;
using VGAnima.Persistence;
using Xunit;

namespace VGAnima.Tests.Persistence;

public class SidecarSchemaRoundtripTests
{
    [Fact]
    public void Serializes_AndRoundtripsEntryShape()
    {
        var schema = new SidecarSchema(
            Version: 1,
            Entries: new[]
            {
                new PersistedEntry(
                    StoryId: "vganima_llm_station-42_broker-abc_nonce-xyz",
                    State: "offered",
                    MissionBlock: FixtureBlock(),
                    Broker: new PersistedBroker(
                        Seed: "vganima-broker-abc-0",
                        StationId: "station-42",
                        Story: FixtureStory()),
                    Timestamps: new PersistedTimestamps(
                        CreatedGameSeconds: 18420.5,
                        CreatedRealUtc: "2026-04-21T10:15:30Z",
                        LastSeenGameSeconds: 19800.0,
                        LastSeenRealUtc: "2026-04-21T10:38:00Z")),
            });

        // Use the schema's own serializer settings — TypeNameHandling.Auto
        // so LlmObjective / LlmReward polymorphism roundtrips correctly.
        var json = JsonConvert.SerializeObject(schema, SidecarSchema.SerializerSettings);
        var parsed = JsonConvert.DeserializeObject<SidecarSchema>(
            json, SidecarSchema.SerializerSettings)!;

        Assert.Equal(1, parsed.Version);
        Assert.Single(parsed.Entries);
        var entry = parsed.Entries[0];
        Assert.Equal("vganima_llm_station-42_broker-abc_nonce-xyz", entry.StoryId);
        Assert.Equal("offered", entry.State);
        Assert.Equal("vganima-broker-abc-0", entry.Broker.Seed);
        Assert.Equal("station-42", entry.Broker.StationId);
        Assert.Equal(18420.5, entry.Timestamps.CreatedGameSeconds);
        Assert.Equal("2026-04-21T10:15:30Z", entry.Timestamps.CreatedRealUtc);

        // Prove concrete subtypes roundtrip — the whole point of
        // TypeNameHandling.Auto. Without a $type discriminator on the wire,
        // these abstract-base fields would fail to reinstantiate as their
        // concrete subtype on read. Load-bearing guard: if someone later
        // drops TypeNameHandling.Auto, these assertions fail loudly.
        var objective = parsed.Entries[0].MissionBlock.Steps[0].Objectives[0];
        var trigger = Assert.IsType<LlmTriggerObjective>(objective);
        Assert.Equal("DockedWithSpaceStation", trigger.Trigger);
        Assert.Equal(1, trigger.RequiredAmount);
        Assert.Equal("Dock.", trigger.Description);

        var reward = parsed.Entries[0].MissionBlock.Rewards[0];
        var credits = Assert.IsType<LlmCreditsReward>(reward);
        Assert.Equal(50, credits.BaseValue);

        // Prove deeper fields survive — not just top-level keys.
        Assert.Equal("TradingGuild", parsed.Entries[0].MissionBlock.SourceFaction);
        Assert.NotEmpty(parsed.Entries[0].Broker.Story.Pitch);
    }

    [Fact]
    public void Serializes_TopLevelKeysUsePlainNames()
    {
        var schema = new SidecarSchema(Version: 1, Entries: System.Array.Empty<PersistedEntry>());
        var json = JsonConvert.SerializeObject(schema, SidecarSchema.SerializerSettings);
        Assert.Contains("\"version\":1", json);
        Assert.Contains("\"entries\":[]", json);
    }

    // Regression for a real bug hit during live E2E: LlmMissionBlocks that
    // came through JSON deserialization (the production path — vanilla LLM
    // response → JsonConvert.DeserializeObject<LlmMissionBlock>) populate
    // their IReadOnlyList<T> fields as List<T>, not T[]. The original
    // allowlist only covered T[], so the binder rejected its own output on
    // round-trip and every sidecar quarantined itself on load.
    [Fact]
    public void Roundtrip_ProductionShapeWithListCollectionsSucceeds()
    {
        var schema = new SidecarSchema(
            Version: 1,
            Entries: new[]
            {
                new PersistedEntry(
                    StoryId: "vganima_llm_station-x_broker-y_nonce-z",
                    State: "offered",
                    MissionBlock: FixtureBlockWithListCollections(),
                    Broker: new PersistedBroker(
                        Seed: "vganima-broker-x-0",
                        StationId: "station-x",
                        Story: FixtureStoryWithListCollections()),
                    Timestamps: new PersistedTimestamps(0, "2026-04-21T00:00:00Z", 0, "2026-04-21T00:00:00Z")),
            });

        var json = JsonConvert.SerializeObject(schema, SidecarSchema.SerializerSettings);
        var parsed = JsonConvert.DeserializeObject<SidecarSchema>(
            json, SidecarSchema.SerializerSettings)!;

        // If the binder rejects List<T>, DeserializeObject throws JsonSerializationException.
        // Reaching here means the binder accepted the List<T> shapes.
        Assert.Single(parsed.Entries);
        Assert.IsType<LlmCollectItemTypes>(parsed.Entries[0].MissionBlock.Steps[0].Objectives[0]);
        Assert.IsType<LlmCreditsReward>(parsed.Entries[0].MissionBlock.Rewards[0]);
        Assert.NotEmpty(parsed.Entries[0].Broker.Story.Pitch);
    }

    // Mirrors the private `Block()` helper in
    // VGAnima.Tests/Missions/LlmMissionAssignerTests.cs — keeps the two
    // fixtures shape-identical so reviewers can cross-check.
    private static LlmMissionBlock FixtureBlock() => new(
        Name: "Test", Description: "d", CompletionText: "c", SourceFaction: "TradingGuild",
        Steps: new[]
        {
            new LlmMissionStep(new LlmObjective[]
            {
                new LlmTriggerObjective("DockedWithSpaceStation", 1, "Dock."),
            }),
        },
        Rewards: new LlmReward[] { new LlmCreditsReward(50) });

    // Mirrors the private `Story()` helper in
    // VGAnima.Tests/Pitch/LlmPitchProviderTests.cs.
    private static LlmStory FixtureStory() => new(
        Pitch:   new[] { "pitch-1", "pitch-2", "pitch-3" },
        CheckIn: new[] { "checkin-1" },
        Payout:  new[] { "payout-1", "payout-2", "payout-3" });

    // Production-shape fixture: IReadOnlyList<T> fields populated as
    // List<T>, the way JsonConvert.DeserializeObject<LlmMissionBlock>
    // returns for JSON arrays. Mirrors what a real LLM response looks
    // like after validation.
    private static LlmMissionBlock FixtureBlockWithListCollections() => new(
        Name: "Production-Shape Mission",
        Description: "d", CompletionText: "c", SourceFaction: "TradingGuild",
        Steps: new List<LlmMissionStep>
        {
            new(new List<LlmObjective>
            {
                new LlmCollectItemTypes(ItemCategory: "RefinedProduct", RequiredAmount: 10, Description: "collect"),
            }),
        },
        Rewards: new List<LlmReward>
        {
            new LlmCreditsReward(BaseValue: 40),
            new LlmExperienceReward(BaseValue: 60),
            new LlmReputationReward(Faction: "TradingGuild", Amount: 300),
        });

    private static LlmStory FixtureStoryWithListCollections() => new(
        Pitch:   new List<string> { "p1", "p2", "p3" },
        CheckIn: new List<string> { "c1" },
        Payout:  new List<string> { "r1", "r2", "r3" });
}
