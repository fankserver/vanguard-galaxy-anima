# VGAnima v2-mission — LLM-Authored Missions — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Let the LLM author the entire broker mission — name, description, 1-3 steps of whitelisted objectives, clamped rewards — fused with the dialogue it already writes. Replaces the plugin-authored `Jobsite Survey` test mission. Every broker ships a unique mission whose narrative and mechanics were generated in one coherent pass.

**Architecture:** The v1 pipeline stays intact end-to-end (bar injection → async context gather → HTTP → validate → cache → main-thread dispatch → dialogue replay). Only the output schema and post-validation wiring change. Schema rises to `vganima/mission/v1`, carrying the same 3 dialogue arrays plus a `mission` block. `ResponseValidator` grows a schema-dispatching branch that calls a new `MissionBlockValidator` after the dialogue block passes. Validated JSON becomes a live `Mission` via `MissionFactoryFromJson`, registered into vanilla's `StoryMission` registry per-broker at injection time with a fresh `storyId`. Any validation failure or factory error skips the broker, same as v1.

**Tech Stack:** Unchanged from v1 — BepInEx 5.4.23.2 + HarmonyX 2.10, netstandard2.1, Newtonsoft.Json (game-provided), `HttpClient`, xUnit cross-TFM. No new package references.

**Spec:** `docs/superpowers/specs/2026-04-21-llm-mission-gen-v2-mission.md`
**Notes:** `docs/superpowers/notes/2026-04-20-vganima-llm-authored-missions-future.md`

---

## Decomp-confirmed ground truth

Before planning, the vanilla types were re-verified against `/tmp/decomp/`:

- **`KillEnemies`** (`Source.MissionSystem.Objectives/KillEnemies.cs`): public fields `shipType` (string, optional), `enemyFaction` (Faction, required — `statusText` dereferences `enemyFaction.name` unconditionally when `shipType == null`), `requiredAmount` (int), plus protected setter `currentAmount`. No `description` field — `statusText` is auto-composed from a translation key. **Plan maps `description` from JSON into the mission's own description or ignores it (keeping it in the JSON for the user-facing mission.description only).**
- **`ProtectUnit`** (`Source.MissionSystem.Objectives/ProtectUnit.cs`): inherits from `TriggerObjective`; public fields `enemyFaction`, `protectText`, `hostileNoRepLoss`. Its `triggeredBy` is `MissionTrigger.UnitProtected`. Plan sets `protectText` only (spec §3 matches).
- **`TriggerObjective`** (`Source.MissionSystem.Objectives/TriggerObjective.cs`): `trigger` (MissionTrigger), `description` (string), `requiredAmount` (int, default 1). Matches spec §3 cleanly.
- **`CollectItemTypes`** (`Source.MissionSystem.Objectives/CollectItemTypes.cs`): field `itemCategory` is `ItemCategory?` (nullable **enum**), NOT a list of item identifiers. The objective counts DISTINCT item types collected within the category (progress is `currentTypes.Count`), not total quantity of one specific item. **This means the spec's `item_types: ["IronOre", ...]` notion cannot map 1:1 to `CollectItemTypes`.** The plan reinterprets the v1 curated "item-type" whitelist as the `ItemCategory` enum names vanilla actually uses in decomp (Ore, Salvage, RefinedProduct, TradeGoods, Junk). Whitelist renamed `ItemCategoryWhitelist` + JSON field renamed `item_category` to match the real domain. Spec §3 curated string list (IronOre, TitaniumOre, …) is thereby **shelved to v2-mission v1.1**; the plan adds a note in the Mission prompt that the LLM emits one `ItemCategory` value per `CollectItemTypes` objective.
- **`Credits`** / **`Experience`**: single public `amount` (int). Match spec §4.
- **`Reputation`** (`Source.MissionSystem.Rewards/Reputation.cs`): `amount` (int), `faction` (Faction, nullable — falls back to `mission.sourceFaction` on complete if null). Match spec §4.
- **`Mission`** (`Source.MissionSystem/Mission.cs`): public fields `name`, `description`, `completionText`, `sourcePoi`, `turnIn`, `sourceFaction`, `iconName`, `storyId`, `dynamicLevel`, `trackedOnHud`, `canBeIdled`, `difficulty`; `steps` and `rewards` are `List<T>` with private setter but public getter + `.Add(...)`.
- **`StoryMission`** (`Source.MissionSystem/StoryMission.cs`): `StoryMission.Add(StoryMission m)` is the registration entry; `StoryMission.Get(player, id)` calls the factory delegate AND sets `mission.storyId = id` after. Setting `storyId` in the factory is redundant but harmless. **Factory delegate signature: `delegate Mission CreateMission(GamePlayer owner)`.**
- **`Faction`** (`Source.Galaxy/Faction.cs`): `Faction.Get(string id)` is public and creates the faction by reflection if missing. `Faction.all` returns `IEnumerable<Faction>`. Valid identifier strings (class names) include `Marauders`, `PoliceGuild`, `BountyGuild`, `TradingGuild`, `MiningGuild`, `IndustrialGuild`, `SalvageGuild`, `Stranded`, `MercenaryGuild`, `Smugglers`, `Darkspacers`, `Puppeteers`, `Fanatics`, `HolyRadicals`, `Amalgam` + corporations `Red`/`Blue`/`Gold`. `Player` excluded (reserved). Spec §3 lowerCamelCase variants (`marauders`, `policeGuild`) **do not match** — `Faction.Get` keys off class name (PascalCase). **Plan uses PascalCase identifiers throughout** (matches what `GameStateView.Reputation` already emits into the context).
- **`MissionTrigger`** enum: only 3 are whitelisted at launch: `DockedWithSpaceStation`, `ArrivedAtSpaceStation`, and `MoveToArea` (spec §3 says `TravelToPOI` but that is NOT a `MissionTrigger` enum value — the enum lists `MoveToArea` as the nearest generic "go somewhere" trigger). **Plan swaps `TravelToPOI` → `MoveToArea`** in the whitelist.
- **`ItemCategory`** enum (`Source.Item/ItemCategory.cs`): `Empty, Ore, Ammo, Turret, Module, Booster, Junk, UnusedMissionItem, Drone, RefinedProduct, Torpedo, JumpgatePass, TradeGoods, Usable, DefensiveTurret, Salvage, Currency, Crystal`.

All other spec details carry through without change.

---

## File layout

| File | Change |
|---|---|
| `VGAnima/Llm/FactionWhitelist.cs` | **Create** — static whitelist + `Contains` |
| `VGAnima/Llm/TriggerWhitelist.cs` | **Create** — static whitelist + `Contains` |
| `VGAnima/Llm/ItemCategoryWhitelist.cs` | **Create** — static whitelist + `Contains` |
| `VGAnima/Llm/ObjectiveTypeWhitelist.cs` | **Create** — static whitelist + `Contains` |
| `VGAnima/Llm/RewardTypeWhitelist.cs` | **Create** — static whitelist + `Contains` |
| `VGAnima.Tests/Llm/WhitelistsTests.cs` | **Create** — membership + count assertions |
| `VGAnima/Llm/LlmMissionBlock.cs` | **Create** — record types mirroring spec §2 |
| `VGAnima/Llm/MissionBlockValidator.cs` | **Create** — spec §5 rules |
| `VGAnima.Tests/Llm/MissionBlockValidatorTests.cs` | **Create** — ~40 cases |
| `VGAnima/Llm/LlmStory.cs` | **Modify** — add optional `Mission` field |
| `VGAnima/Llm/ResponseValidator.cs` | **Modify** — schema dispatch |
| `VGAnima.Tests/Llm/ResponseValidatorTests.cs` | **Modify** — add schema dispatch tests |
| `VGAnima/Missions/MissionFactoryFromJson.cs` | **Create** — JSON → live `Mission` |
| `VGAnima.Tests/Missions/MissionFactoryFromJsonTests.cs` | **Create** — field-by-field assertions |
| `VGAnima/Missions/LlmMissionAssigner.cs` | **Create** — builds + registers per-broker |
| `VGAnima.Tests/Missions/LlmMissionAssignerTests.cs` | **Create** — returns fresh storyId per call |
| `VGAnima/Cache/ConversionRecord.cs` | **Modify** — extend `LlmStory` to carry Mission block |
| `VGAnima/Missions/TestMissionAssigner.cs` | **Modify** — mark `[Obsolete]` |
| `VGAnima/Missions/TestStoryMissions.cs` | **Modify** — mark `[Obsolete]`, keep `Register()` |
| `VGAnima/Patches/BarRefreshPatches.cs` | **Modify** — v2 prompt, post-validation factory call, per-broker `StoryMission.Add` |
| `VGAnima/Plugin.cs` | **Modify** — swap `TestMissionAssigner` → `LlmMissionAssigner`, keep `TestStoryMissions.Register()` |
| `VGAnima.Tests/Llm/LlmContextJsonTests.cs` | **Modify** — add mission-block round-trip sentinel (optional safety net) |

---

## Task 1: Whitelist modules (TDD)

**Files:**
- Create: `VGAnima/Llm/FactionWhitelist.cs`
- Create: `VGAnima/Llm/TriggerWhitelist.cs`
- Create: `VGAnima/Llm/ItemCategoryWhitelist.cs`
- Create: `VGAnima/Llm/ObjectiveTypeWhitelist.cs`
- Create: `VGAnima/Llm/RewardTypeWhitelist.cs`
- Create: `VGAnima.Tests/Llm/WhitelistsTests.cs`

Whitelists are curated string sets — no reflection, no decomp types touched. They pair with the validator (Task 2) for string-membership checks. netstandard2.1 has no `IReadOnlySet<T>`, so the public surface is `IReadOnlyCollection<string>` + a `Contains(string)` method (fast-path via a private `HashSet<string>`).

- [ ] **Step 1: Write failing tests**

Create `VGAnima.Tests/Llm/WhitelistsTests.cs`:

```csharp
using VGAnima.Llm;
using Xunit;

namespace VGAnima.Tests.Llm;

public class WhitelistsTests
{
    // -------- Faction --------
    [Fact]
    public void FactionWhitelist_HasExpectedCount()
    {
        // 16 identifiers: all vanilla factions except Player.
        Assert.Equal(16, FactionWhitelist.All.Count);
    }

    [Theory]
    [InlineData("Marauders")]
    [InlineData("PoliceGuild")]
    [InlineData("BountyGuild")]
    [InlineData("TradingGuild")]
    [InlineData("MiningGuild")]
    [InlineData("IndustrialGuild")]
    [InlineData("SalvageGuild")]
    [InlineData("Stranded")]
    [InlineData("MercenaryGuild")]
    [InlineData("Smugglers")]
    [InlineData("Darkspacers")]
    [InlineData("Puppeteers")]
    [InlineData("Fanatics")]
    [InlineData("HolyRadicals")]
    [InlineData("Amalgam")]
    [InlineData("Gold")]
    public void FactionWhitelist_Contains_AllowedIdentifier(string id)
    {
        Assert.True(FactionWhitelist.Contains(id));
    }

    [Theory]
    [InlineData("Player")]           // deliberately excluded
    [InlineData("marauders")]        // lowercase — not a valid identifier
    [InlineData("")]
    [InlineData("NotAFaction")]
    public void FactionWhitelist_Rejects_Unknown(string id)
    {
        Assert.False(FactionWhitelist.Contains(id));
    }

    // -------- Trigger --------
    [Fact]
    public void TriggerWhitelist_HasExactlyThreeEntries()
    {
        Assert.Equal(3, TriggerWhitelist.All.Count);
    }

    [Theory]
    [InlineData("DockedWithSpaceStation")]
    [InlineData("ArrivedAtSpaceStation")]
    [InlineData("MoveToArea")]
    public void TriggerWhitelist_Contains_Allowed(string trigger)
    {
        Assert.True(TriggerWhitelist.Contains(trigger));
    }

    [Theory]
    [InlineData("UnitDestroyed")]
    [InlineData("TravelToPOI")]      // spec typo: real enum is MoveToArea
    [InlineData("dockedWithSpaceStation")]  // case-sensitive
    [InlineData("")]
    public void TriggerWhitelist_Rejects_Others(string trigger)
    {
        Assert.False(TriggerWhitelist.Contains(trigger));
    }

    // -------- ItemCategory --------
    [Fact]
    public void ItemCategoryWhitelist_HasExactlyFiveEntries()
    {
        Assert.Equal(5, ItemCategoryWhitelist.All.Count);
    }

    [Theory]
    [InlineData("Ore")]
    [InlineData("Salvage")]
    [InlineData("RefinedProduct")]
    [InlineData("TradeGoods")]
    [InlineData("Junk")]
    public void ItemCategoryWhitelist_Contains_Allowed(string cat)
    {
        Assert.True(ItemCategoryWhitelist.Contains(cat));
    }

    [Theory]
    [InlineData("Ammo")]
    [InlineData("Crystal")]
    [InlineData("Empty")]
    [InlineData("IronOre")]   // item identifier, not a category
    [InlineData("")]
    public void ItemCategoryWhitelist_Rejects_Others(string cat)
    {
        Assert.False(ItemCategoryWhitelist.Contains(cat));
    }

    // -------- ObjectiveType --------
    [Fact]
    public void ObjectiveTypeWhitelist_HasExactlyFourEntries()
    {
        Assert.Equal(4, ObjectiveTypeWhitelist.All.Count);
    }

    [Theory]
    [InlineData("KillEnemies")]
    [InlineData("ProtectUnit")]
    [InlineData("TriggerObjective")]
    [InlineData("CollectItemTypes")]
    public void ObjectiveTypeWhitelist_Contains_Allowed(string type)
    {
        Assert.True(ObjectiveTypeWhitelist.Contains(type));
    }

    [Theory]
    [InlineData("TradeOffer")]
    [InlineData("Mining")]
    [InlineData("Salvage")]
    [InlineData("kill_enemies")]
    [InlineData("")]
    public void ObjectiveTypeWhitelist_Rejects_Others(string type)
    {
        Assert.False(ObjectiveTypeWhitelist.Contains(type));
    }

    // -------- RewardType --------
    [Fact]
    public void RewardTypeWhitelist_HasExactlyThreeEntries()
    {
        Assert.Equal(3, RewardTypeWhitelist.All.Count);
    }

    [Theory]
    [InlineData("Credits")]
    [InlineData("Experience")]
    [InlineData("Reputation")]
    public void RewardTypeWhitelist_Contains_Allowed(string type)
    {
        Assert.True(RewardTypeWhitelist.Contains(type));
    }

    [Theory]
    [InlineData("Item")]
    [InlineData("Skillpoint")]
    [InlineData("StoryMission")]
    [InlineData("")]
    public void RewardTypeWhitelist_Rejects_Others(string type)
    {
        Assert.False(RewardTypeWhitelist.Contains(type));
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run:
```
make test
```

Expected: compile error — `FactionWhitelist`, `TriggerWhitelist`, `ItemCategoryWhitelist`, `ObjectiveTypeWhitelist`, `RewardTypeWhitelist` don't exist.

- [ ] **Step 3: Create the five whitelist modules**

Create `VGAnima/Llm/FactionWhitelist.cs`:

```csharp
using System.Collections.Generic;

namespace VGAnima.Llm;

/// <summary>Curated whitelist of faction identifier strings the LLM may emit
/// in <c>source_faction</c> / <c>enemy_faction</c> / reputation <c>faction</c>
/// fields. Membership is checked via <see cref="Contains"/>; the value is the
/// literal <c>Faction.identifier</c> (PascalCase class name, confirmed in
/// <c>Source.Galaxy/Faction.cs</c> static fields) so callers can resolve to
/// a live <c>Source.Galaxy.Faction</c> via <c>Faction.Get(id)</c>.
///
/// Excludes <c>Player</c> (reserved for the commanding side) and does not
/// include corporations by their short names to avoid ambiguity — only
/// <c>Gold</c> is kept for test continuity; others can be added once the
/// LLM's usage is observed. Spec §3 / §4.</summary>
internal static class FactionWhitelist
{
    private static readonly HashSet<string> Ids = new()
    {
        "Marauders",
        "PoliceGuild",
        "BountyGuild",
        "TradingGuild",
        "MiningGuild",
        "IndustrialGuild",
        "SalvageGuild",
        "Stranded",
        "MercenaryGuild",
        "Smugglers",
        "Darkspacers",
        "Puppeteers",
        "Fanatics",
        "HolyRadicals",
        "Amalgam",
        "Gold",
    };

    public static IReadOnlyCollection<string> All => Ids;

    public static bool Contains(string id) => id != null && Ids.Contains(id);
}
```

Create `VGAnima/Llm/TriggerWhitelist.cs`:

```csharp
using System.Collections.Generic;

namespace VGAnima.Llm;

/// <summary>Whitelisted <see cref="Source.MissionSystem.MissionTrigger"/>
/// values for <c>TriggerObjective</c>. Only the three station-travel triggers
/// that fire during plain gameplay are allowed at launch. Spec §3.
///
/// Note: spec lists <c>TravelToPOI</c> — that is NOT a real enum value. The
/// closest vanilla equivalent for "go somewhere" is <c>MoveToArea</c>. Plan
/// uses <c>MoveToArea</c>.</summary>
internal static class TriggerWhitelist
{
    private static readonly HashSet<string> Names = new()
    {
        "DockedWithSpaceStation",
        "ArrivedAtSpaceStation",
        "MoveToArea",
    };

    public static IReadOnlyCollection<string> All => Names;

    public static bool Contains(string name) => name != null && Names.Contains(name);
}
```

Create `VGAnima/Llm/ItemCategoryWhitelist.cs`:

```csharp
using System.Collections.Generic;

namespace VGAnima.Llm;

/// <summary>Whitelisted <see cref="Source.Item.ItemCategory"/> enum names for
/// <c>CollectItemTypes.item_category</c>. Vanilla's <c>CollectItemTypes</c>
/// takes an <c>ItemCategory?</c> field, NOT a list of specific item
/// identifiers, so the LLM emits one enum-name per objective. Spec §3.
///
/// Curated set at launch — extend once we see what the LLM actually produces.
/// Excludes technical categories (Empty, Ammo, Turret, Module, Booster,
/// UnusedMissionItem, Drone, Torpedo, JumpgatePass, Usable, DefensiveTurret,
/// Currency, Crystal) that don't make sense as broker-job targets.</summary>
internal static class ItemCategoryWhitelist
{
    private static readonly HashSet<string> Names = new()
    {
        "Ore",
        "Salvage",
        "RefinedProduct",
        "TradeGoods",
        "Junk",
    };

    public static IReadOnlyCollection<string> All => Names;

    public static bool Contains(string name) => name != null && Names.Contains(name);
}
```

Create `VGAnima/Llm/ObjectiveTypeWhitelist.cs`:

```csharp
using System.Collections.Generic;

namespace VGAnima.Llm;

/// <summary>Whitelisted objective <c>type</c> discriminator values. Each one
/// maps to a real <c>Source.MissionSystem.Objectives.*</c> class in
/// <see cref="VGAnima.Missions.MissionFactoryFromJson"/>. Spec §3.</summary>
internal static class ObjectiveTypeWhitelist
{
    private static readonly HashSet<string> Names = new()
    {
        "KillEnemies",
        "ProtectUnit",
        "TriggerObjective",
        "CollectItemTypes",
    };

    public static IReadOnlyCollection<string> All => Names;

    public static bool Contains(string name) => name != null && Names.Contains(name);
}
```

Create `VGAnima/Llm/RewardTypeWhitelist.cs`:

```csharp
using System.Collections.Generic;

namespace VGAnima.Llm;

/// <summary>Whitelisted reward <c>type</c> discriminator values. Spec §4.</summary>
internal static class RewardTypeWhitelist
{
    private static readonly HashSet<string> Names = new()
    {
        "Credits",
        "Experience",
        "Reputation",
    };

    public static IReadOnlyCollection<string> All => Names;

    public static bool Contains(string name) => name != null && Names.Contains(name);
}
```

- [ ] **Step 4: Run tests to verify they pass**

Run:
```
make test
```

Expected: all 5 whitelist test groups pass. Existing tests still pass.

- [ ] **Step 5: Commit**

```bash
git add VGAnima/Llm/FactionWhitelist.cs VGAnima/Llm/TriggerWhitelist.cs \
  VGAnima/Llm/ItemCategoryWhitelist.cs VGAnima/Llm/ObjectiveTypeWhitelist.cs \
  VGAnima/Llm/RewardTypeWhitelist.cs \
  VGAnima.Tests/Llm/WhitelistsTests.cs
git -c user.name="Florian Kinder" -c user.email="florian.kinder@fankserver.com" commit -m "$(cat <<'EOF'
feat: curated whitelists for v2-mission schema

Five static whitelist modules (factions, triggers, item categories,
objective types, reward types) that the MissionBlockValidator dispatches
against. netstandard2.1-safe IReadOnlyCollection<string> + Contains
(IReadOnlySet not available pre-net5). Tests assert exact counts and
sample membership / rejection per spec §3-§4.
EOF
)"
```

---

## Task 2: `MissionBlockValidator` (TDD, ~40 cases)

**Files:**
- Create: `VGAnima/Llm/LlmMissionBlock.cs`
- Create: `VGAnima/Llm/MissionBlockValidator.cs`
- Create: `VGAnima.Tests/Llm/MissionBlockValidatorTests.cs`

The validator is a stateless class that consumes a `JObject` (the `mission` sub-object pulled out of the LLM response) and the optional cross-reference context (AtWar + Reputation maps) from `LlmContext`, returning a materialized `LlmMissionBlock` POCO on success. First failure raises `LlmValidationException` with a field path.

Rule order mirrors spec §5 exactly. Every "happy" test builds a known-good block; every "reject" test is one field/rule violation.

- [ ] **Step 1: Write failing tests**

Create `VGAnima.Tests/Llm/MissionBlockValidatorTests.cs`:

```csharp
using System.Collections.Generic;
using Newtonsoft.Json.Linq;
using VGAnima.Llm;
using Xunit;

namespace VGAnima.Tests.Llm;

public class MissionBlockValidatorTests
{
    /// <summary>Context subset used for cross-checks (enemy_faction hostility).
    /// Plain record, not a seam over IGameStateView — the validator only needs
    /// AtWar + Reputation lookups, both supplied at call time.</summary>
    private static readonly IReadOnlyList<string> Hostiles =
        new[] { "Marauders", "Darkspacers" };

    private static readonly IReadOnlyDictionary<string, int> NeutralAndHostileRep =
        new Dictionary<string, int>
        {
            { "Marauders",       -10 },
            { "Darkspacers",     -50 },
            { "TradingGuild",    100 },
            { "PoliceGuild",      50 },
            { "BountyGuild",      20 },
            { "Stranded",          0 },
            { "MiningGuild",       0 },
        };

    private static MissionBlockValidator Validator() => new();

    // ---------- Rule 1: mission must be a JSON object ----------

    [Fact]
    public void Parse_WhenMissionIsArray_Rejects()
    {
        var mission = JToken.Parse("[]");
        var ex = Assert.Throws<LlmValidationException>(
            () => Validator().Parse(mission, Hostiles, NeutralAndHostileRep));
        Assert.Contains("object", ex.Message);
    }

    [Fact]
    public void Parse_WhenMissionIsString_Rejects()
    {
        var mission = JToken.Parse("\"oops\"");
        Assert.Throws<LlmValidationException>(
            () => Validator().Parse(mission, Hostiles, NeutralAndHostileRep));
    }

    [Fact]
    public void Parse_WhenMissionIsNull_Rejects()
    {
        var mission = JToken.Parse("null");
        Assert.Throws<LlmValidationException>(
            () => Validator().Parse(mission, Hostiles, NeutralAndHostileRep));
    }

    // ---------- Rule 2: strict field set ----------

    [Fact]
    public void Parse_MissingName_Rejects()
    {
        var mission = BuildWithoutField("name");
        var ex = Assert.Throws<LlmValidationException>(
            () => Validator().Parse(mission, Hostiles, NeutralAndHostileRep));
        Assert.Contains("name", ex.Message);
    }

    [Fact]
    public void Parse_MissingDescription_Rejects()
    {
        var mission = BuildWithoutField("description");
        Assert.Throws<LlmValidationException>(
            () => Validator().Parse(mission, Hostiles, NeutralAndHostileRep));
    }

    [Fact]
    public void Parse_MissingCompletionText_Rejects()
    {
        var mission = BuildWithoutField("completion_text");
        Assert.Throws<LlmValidationException>(
            () => Validator().Parse(mission, Hostiles, NeutralAndHostileRep));
    }

    [Fact]
    public void Parse_MissingSourceFaction_Rejects()
    {
        var mission = BuildWithoutField("source_faction");
        Assert.Throws<LlmValidationException>(
            () => Validator().Parse(mission, Hostiles, NeutralAndHostileRep));
    }

    [Fact]
    public void Parse_MissingSteps_Rejects()
    {
        var mission = BuildWithoutField("steps");
        Assert.Throws<LlmValidationException>(
            () => Validator().Parse(mission, Hostiles, NeutralAndHostileRep));
    }

    [Fact]
    public void Parse_MissingRewards_Rejects()
    {
        var mission = BuildWithoutField("rewards");
        Assert.Throws<LlmValidationException>(
            () => Validator().Parse(mission, Hostiles, NeutralAndHostileRep));
    }

    [Fact]
    public void Parse_ExtraTopLevelField_Rejects()
    {
        var mission = BuildGood();
        mission["extra_key"] = "stuff";
        var ex = Assert.Throws<LlmValidationException>(
            () => Validator().Parse(mission, Hostiles, NeutralAndHostileRep));
        Assert.Contains("extra_key", ex.Message);
    }

    // ---------- Rule 3: string caps + ASCII ----------

    [Fact]
    public void Parse_NameTooLong_Rejects()
    {
        var mission = BuildGood();
        mission["name"] = new string('x', 61);
        Assert.Throws<LlmValidationException>(
            () => Validator().Parse(mission, Hostiles, NeutralAndHostileRep));
    }

    [Fact]
    public void Parse_DescriptionTooLong_Rejects()
    {
        var mission = BuildGood();
        mission["description"] = new string('x', 501);
        Assert.Throws<LlmValidationException>(
            () => Validator().Parse(mission, Hostiles, NeutralAndHostileRep));
    }

    [Fact]
    public void Parse_CompletionTextTooLong_Rejects()
    {
        var mission = BuildGood();
        mission["completion_text"] = new string('x', 201);
        Assert.Throws<LlmValidationException>(
            () => Validator().Parse(mission, Hostiles, NeutralAndHostileRep));
    }

    [Fact]
    public void Parse_NameEmpty_Rejects()
    {
        var mission = BuildGood();
        mission["name"] = "   ";
        Assert.Throws<LlmValidationException>(
            () => Validator().Parse(mission, Hostiles, NeutralAndHostileRep));
    }

    [Fact]
    public void Parse_NonAsciiInName_Rejects()
    {
        var mission = BuildGood();
        mission["name"] = "Name \u2014 em-dash";
        var ex = Assert.Throws<LlmValidationException>(
            () => Validator().Parse(mission, Hostiles, NeutralAndHostileRep));
        Assert.Contains("ascii", ex.Message, System.StringComparison.OrdinalIgnoreCase);
    }

    // ---------- Rule 4: source_faction whitelist ----------

    [Fact]
    public void Parse_SourceFactionUnknown_Rejects()
    {
        var mission = BuildGood();
        mission["source_faction"] = "NotAFaction";
        var ex = Assert.Throws<LlmValidationException>(
            () => Validator().Parse(mission, Hostiles, NeutralAndHostileRep));
        Assert.Contains("source_faction", ex.Message);
    }

    [Fact]
    public void Parse_SourceFactionPlayer_Rejects()
    {
        var mission = BuildGood();
        mission["source_faction"] = "Player";
        Assert.Throws<LlmValidationException>(
            () => Validator().Parse(mission, Hostiles, NeutralAndHostileRep));
    }

    // ---------- Rule 5: steps array size ----------

    [Fact]
    public void Parse_StepsEmpty_Rejects()
    {
        var mission = BuildGood();
        mission["steps"] = new JArray();
        Assert.Throws<LlmValidationException>(
            () => Validator().Parse(mission, Hostiles, NeutralAndHostileRep));
    }

    [Fact]
    public void Parse_StepsTooMany_Rejects()
    {
        var mission = BuildGood();
        mission["steps"] = new JArray(BuildStep(), BuildStep(), BuildStep(), BuildStep());
        Assert.Throws<LlmValidationException>(
            () => Validator().Parse(mission, Hostiles, NeutralAndHostileRep));
    }

    [Fact]
    public void Parse_StepNotObject_Rejects()
    {
        var mission = BuildGood();
        mission["steps"] = new JArray("not-a-step");
        Assert.Throws<LlmValidationException>(
            () => Validator().Parse(mission, Hostiles, NeutralAndHostileRep));
    }

    [Fact]
    public void Parse_StepObjectivesEmpty_Rejects()
    {
        var mission = BuildGood();
        var step = new JObject { ["objectives"] = new JArray() };
        mission["steps"] = new JArray(step);
        Assert.Throws<LlmValidationException>(
            () => Validator().Parse(mission, Hostiles, NeutralAndHostileRep));
    }

    [Fact]
    public void Parse_StepObjectivesTooMany_Rejects()
    {
        var mission = BuildGood();
        var step = new JObject
        {
            ["objectives"] = new JArray(
                BuildTriggerObj(), BuildTriggerObj(), BuildTriggerObj()),
        };
        mission["steps"] = new JArray(step);
        Assert.Throws<LlmValidationException>(
            () => Validator().Parse(mission, Hostiles, NeutralAndHostileRep));
    }

    // ---------- Rule 7: per-objective type dispatch ----------

    [Fact]
    public void Parse_ObjectiveUnknownType_Rejects()
    {
        var mission = BuildGood();
        var bad = new JObject { ["type"] = "TradeOffer" };
        mission["steps"] = new JArray(new JObject
        {
            ["objectives"] = new JArray(bad),
        });
        var ex = Assert.Throws<LlmValidationException>(
            () => Validator().Parse(mission, Hostiles, NeutralAndHostileRep));
        Assert.Contains("TradeOffer", ex.Message);
    }

    // ---------- KillEnemies per-type rules ----------

    [Fact]
    public void Parse_KillEnemies_HappyPath()
    {
        var mission = BuildGood();
        var kill = new JObject
        {
            ["type"]            = "KillEnemies",
            ["enemy_faction"]   = "Marauders",
            ["required_amount"] = 3,
            ["description"]     = "Kill three Marauders.",
        };
        mission["steps"] = new JArray(new JObject
        {
            ["objectives"] = new JArray(kill),
        });
        var block = Validator().Parse(mission, Hostiles, NeutralAndHostileRep);
        Assert.Single(block.Steps);
        var obj = Assert.IsType<LlmKillEnemies>(block.Steps[0].Objectives[0]);
        Assert.Equal("Marauders", obj.EnemyFaction);
        Assert.Equal(3, obj.RequiredAmount);
    }

    [Fact]
    public void Parse_KillEnemies_RequiredAmountZero_Rejects()
    {
        var mission = BuildGood();
        var kill = new JObject
        {
            ["type"]            = "KillEnemies",
            ["enemy_faction"]   = "Marauders",
            ["required_amount"] = 0,
            ["description"]     = "Kill none.",
        };
        mission["steps"] = new JArray(new JObject
        {
            ["objectives"] = new JArray(kill),
        });
        Assert.Throws<LlmValidationException>(
            () => Validator().Parse(mission, Hostiles, NeutralAndHostileRep));
    }

    [Fact]
    public void Parse_KillEnemies_RequiredAmountTooBig_Rejects()
    {
        var mission = BuildGood();
        var kill = new JObject
        {
            ["type"]            = "KillEnemies",
            ["enemy_faction"]   = "Marauders",
            ["required_amount"] = 6,
            ["description"]     = "Kill six.",
        };
        mission["steps"] = new JArray(new JObject
        {
            ["objectives"] = new JArray(kill),
        });
        Assert.Throws<LlmValidationException>(
            () => Validator().Parse(mission, Hostiles, NeutralAndHostileRep));
    }

    [Fact]
    public void Parse_KillEnemies_EnemyFactionFriendly_Rejects()
    {
        var mission = BuildGood();
        var kill = new JObject
        {
            ["type"]            = "KillEnemies",
            ["enemy_faction"]   = "TradingGuild",  // +100 rep, not in Hostiles
            ["required_amount"] = 2,
            ["description"]     = "Kill allies.",
        };
        mission["steps"] = new JArray(new JObject
        {
            ["objectives"] = new JArray(kill),
        });
        var ex = Assert.Throws<LlmValidationException>(
            () => Validator().Parse(mission, Hostiles, NeutralAndHostileRep));
        Assert.Contains("enemy_faction", ex.Message);
    }

    [Fact]
    public void Parse_KillEnemies_EnemyFactionNeutralAccepted()
    {
        // rep==0 AND at_war-empty-for-this-faction ⇒ neutral; allowed per spec §3.
        // Rule: reject only when reputation > 0 OR not at_war AND rep > 0.
        // Neutral factions pass — they can turn hostile organically.
        var mission = BuildGood();
        var kill = new JObject
        {
            ["type"]            = "KillEnemies",
            ["enemy_faction"]   = "MiningGuild",  // rep 0, not at-war
            ["required_amount"] = 2,
            ["description"]     = "Kill neutrals.",
        };
        mission["steps"] = new JArray(new JObject
        {
            ["objectives"] = new JArray(kill),
        });
        var block = Validator().Parse(mission, Hostiles, NeutralAndHostileRep);
        Assert.Single(block.Steps);
    }

    [Fact]
    public void Parse_KillEnemies_EnemyFactionUnknown_Rejects()
    {
        var mission = BuildGood();
        var kill = new JObject
        {
            ["type"]            = "KillEnemies",
            ["enemy_faction"]   = "NotAFaction",
            ["required_amount"] = 2,
            ["description"]     = "Kill.",
        };
        mission["steps"] = new JArray(new JObject
        {
            ["objectives"] = new JArray(kill),
        });
        Assert.Throws<LlmValidationException>(
            () => Validator().Parse(mission, Hostiles, NeutralAndHostileRep));
    }

    [Fact]
    public void Parse_KillEnemies_DescriptionTooLong_Rejects()
    {
        var mission = BuildGood();
        var kill = new JObject
        {
            ["type"]            = "KillEnemies",
            ["enemy_faction"]   = "Marauders",
            ["required_amount"] = 1,
            ["description"]     = new string('x', 81),
        };
        mission["steps"] = new JArray(new JObject
        {
            ["objectives"] = new JArray(kill),
        });
        Assert.Throws<LlmValidationException>(
            () => Validator().Parse(mission, Hostiles, NeutralAndHostileRep));
    }

    // ---------- ProtectUnit per-type rules ----------

    [Fact]
    public void Parse_ProtectUnit_HappyPath()
    {
        var mission = BuildGood();
        var protect = new JObject
        {
            ["type"]         = "ProtectUnit",
            ["protect_text"] = "Keep the convoy alive.",
        };
        // Must pair with a non-ProtectUnit objective to pass the degenerate-rule.
        var kill = BuildKillObj("Marauders", 1);
        mission["steps"] = new JArray(new JObject
        {
            ["objectives"] = new JArray(protect, kill),
        });
        var block = Validator().Parse(mission, Hostiles, NeutralAndHostileRep);
        Assert.IsType<LlmProtectUnit>(block.Steps[0].Objectives[0]);
    }

    [Fact]
    public void Parse_ProtectUnit_MissingProtectText_Rejects()
    {
        var mission = BuildGood();
        var protect = new JObject { ["type"] = "ProtectUnit" };
        mission["steps"] = new JArray(new JObject
        {
            ["objectives"] = new JArray(protect, BuildKillObj("Marauders", 1)),
        });
        Assert.Throws<LlmValidationException>(
            () => Validator().Parse(mission, Hostiles, NeutralAndHostileRep));
    }

    [Fact]
    public void Parse_ProtectUnit_ProtectTextTooLong_Rejects()
    {
        var mission = BuildGood();
        var protect = new JObject
        {
            ["type"]         = "ProtectUnit",
            ["protect_text"] = new string('x', 81),
        };
        mission["steps"] = new JArray(new JObject
        {
            ["objectives"] = new JArray(protect, BuildKillObj("Marauders", 1)),
        });
        Assert.Throws<LlmValidationException>(
            () => Validator().Parse(mission, Hostiles, NeutralAndHostileRep));
    }

    // ---------- TriggerObjective per-type rules ----------

    [Fact]
    public void Parse_TriggerObjective_HappyPath()
    {
        var mission = BuildGood();
        var trig = new JObject
        {
            ["type"]            = "TriggerObjective",
            ["trigger"]         = "DockedWithSpaceStation",
            ["required_amount"] = 2,
            ["description"]     = "Dock twice.",
        };
        mission["steps"] = new JArray(new JObject
        {
            ["objectives"] = new JArray(trig),
        });
        var block = Validator().Parse(mission, Hostiles, NeutralAndHostileRep);
        var obj = Assert.IsType<LlmTriggerObjective>(block.Steps[0].Objectives[0]);
        Assert.Equal("DockedWithSpaceStation", obj.Trigger);
    }

    [Fact]
    public void Parse_TriggerObjective_TriggerNotWhitelisted_Rejects()
    {
        var mission = BuildGood();
        var trig = new JObject
        {
            ["type"]            = "TriggerObjective",
            ["trigger"]         = "BountyTargetKilled",  // real enum value; not whitelisted
            ["required_amount"] = 1,
            ["description"]     = "Kill.",
        };
        mission["steps"] = new JArray(new JObject
        {
            ["objectives"] = new JArray(trig),
        });
        var ex = Assert.Throws<LlmValidationException>(
            () => Validator().Parse(mission, Hostiles, NeutralAndHostileRep));
        Assert.Contains("trigger", ex.Message);
    }

    [Fact]
    public void Parse_TriggerObjective_RequiredAmountTooBig_Rejects()
    {
        var mission = BuildGood();
        var trig = new JObject
        {
            ["type"]            = "TriggerObjective",
            ["trigger"]         = "DockedWithSpaceStation",
            ["required_amount"] = 4,
            ["description"]     = "Dock.",
        };
        mission["steps"] = new JArray(new JObject
        {
            ["objectives"] = new JArray(trig),
        });
        Assert.Throws<LlmValidationException>(
            () => Validator().Parse(mission, Hostiles, NeutralAndHostileRep));
    }

    // ---------- CollectItemTypes per-type rules ----------

    [Fact]
    public void Parse_CollectItemTypes_HappyPath()
    {
        var mission = BuildGood();
        var collect = new JObject
        {
            ["type"]            = "CollectItemTypes",
            ["item_category"]   = "Ore",
            ["required_amount"] = 5,
            ["description"]     = "Collect ore samples.",
        };
        mission["steps"] = new JArray(new JObject
        {
            ["objectives"] = new JArray(collect),
        });
        var block = Validator().Parse(mission, Hostiles, NeutralAndHostileRep);
        var obj = Assert.IsType<LlmCollectItemTypes>(block.Steps[0].Objectives[0]);
        Assert.Equal("Ore", obj.ItemCategory);
        Assert.Equal(5, obj.RequiredAmount);
    }

    [Fact]
    public void Parse_CollectItemTypes_CategoryNotWhitelisted_Rejects()
    {
        var mission = BuildGood();
        var collect = new JObject
        {
            ["type"]            = "CollectItemTypes",
            ["item_category"]   = "Crystal",
            ["required_amount"] = 1,
            ["description"]     = "Collect.",
        };
        mission["steps"] = new JArray(new JObject
        {
            ["objectives"] = new JArray(collect),
        });
        Assert.Throws<LlmValidationException>(
            () => Validator().Parse(mission, Hostiles, NeutralAndHostileRep));
    }

    [Fact]
    public void Parse_CollectItemTypes_RequiredAmountTooBig_Rejects()
    {
        var mission = BuildGood();
        var collect = new JObject
        {
            ["type"]            = "CollectItemTypes",
            ["item_category"]   = "Ore",
            ["required_amount"] = 51,
            ["description"]     = "Collect.",
        };
        mission["steps"] = new JArray(new JObject
        {
            ["objectives"] = new JArray(collect),
        });
        Assert.Throws<LlmValidationException>(
            () => Validator().Parse(mission, Hostiles, NeutralAndHostileRep));
    }

    // ---------- Rewards per-type rules ----------

    [Fact]
    public void Parse_CreditsReward_HappyPath()
    {
        var mission = BuildGood();
        mission["rewards"] = new JArray(new JObject
        {
            ["type"]       = "Credits",
            ["base_value"] = 100,
        });
        var block = Validator().Parse(mission, Hostiles, NeutralAndHostileRep);
        var credits = Assert.IsType<LlmCreditsReward>(block.Rewards[0]);
        Assert.Equal(100, credits.BaseValue);
    }

    [Fact]
    public void Parse_CreditsReward_BaseValueTooLow_Rejects()
    {
        var mission = BuildGood();
        mission["rewards"] = new JArray(new JObject
        {
            ["type"]       = "Credits",
            ["base_value"] = 5,
        });
        Assert.Throws<LlmValidationException>(
            () => Validator().Parse(mission, Hostiles, NeutralAndHostileRep));
    }

    [Fact]
    public void Parse_CreditsReward_BaseValueTooHigh_Rejects()
    {
        var mission = BuildGood();
        mission["rewards"] = new JArray(new JObject
        {
            ["type"]       = "Credits",
            ["base_value"] = 201,
        });
        Assert.Throws<LlmValidationException>(
            () => Validator().Parse(mission, Hostiles, NeutralAndHostileRep));
    }

    [Fact]
    public void Parse_ExperienceReward_BaseValueTooHigh_Rejects()
    {
        var mission = BuildGood();
        mission["rewards"] = new JArray(new JObject
        {
            ["type"]       = "Experience",
            ["base_value"] = 151,
        });
        Assert.Throws<LlmValidationException>(
            () => Validator().Parse(mission, Hostiles, NeutralAndHostileRep));
    }

    [Fact]
    public void Parse_ReputationReward_HappyPath_Negative()
    {
        var mission = BuildGood();
        mission["rewards"] = new JArray(new JObject
        {
            ["type"]    = "Reputation",
            ["faction"] = "Marauders",
            ["amount"]  = -200,
        });
        var block = Validator().Parse(mission, Hostiles, NeutralAndHostileRep);
        var rep = Assert.IsType<LlmReputationReward>(block.Rewards[0]);
        Assert.Equal(-200, rep.Amount);
    }

    [Fact]
    public void Parse_ReputationReward_AmountOutOfRange_Rejects()
    {
        var mission = BuildGood();
        mission["rewards"] = new JArray(new JObject
        {
            ["type"]    = "Reputation",
            ["faction"] = "TradingGuild",
            ["amount"]  = 501,
        });
        Assert.Throws<LlmValidationException>(
            () => Validator().Parse(mission, Hostiles, NeutralAndHostileRep));
    }

    [Fact]
    public void Parse_ReputationReward_FactionUnknown_Rejects()
    {
        var mission = BuildGood();
        mission["rewards"] = new JArray(new JObject
        {
            ["type"]    = "Reputation",
            ["faction"] = "NotAFaction",
            ["amount"]  = 50,
        });
        Assert.Throws<LlmValidationException>(
            () => Validator().Parse(mission, Hostiles, NeutralAndHostileRep));
    }

    [Fact]
    public void Parse_RewardsEmpty_Rejects()
    {
        var mission = BuildGood();
        mission["rewards"] = new JArray();
        Assert.Throws<LlmValidationException>(
            () => Validator().Parse(mission, Hostiles, NeutralAndHostileRep));
    }

    [Fact]
    public void Parse_RewardsTooMany_Rejects()
    {
        var mission = BuildGood();
        var six = new JArray();
        for (var i = 0; i < 6; i++)
            six.Add(new JObject { ["type"] = "Credits", ["base_value"] = 50 });
        mission["rewards"] = six;
        Assert.Throws<LlmValidationException>(
            () => Validator().Parse(mission, Hostiles, NeutralAndHostileRep));
    }

    [Fact]
    public void Parse_RewardUnknownType_Rejects()
    {
        var mission = BuildGood();
        mission["rewards"] = new JArray(new JObject
        {
            ["type"]   = "Item",
            ["amount"] = 1,
        });
        Assert.Throws<LlmValidationException>(
            () => Validator().Parse(mission, Hostiles, NeutralAndHostileRep));
    }

    // ---------- Global coherence: degenerate mission ----------

    [Fact]
    public void Parse_OnlyProtectUnitAcrossAllSteps_Rejects()
    {
        var mission = BuildGood();
        var protect1 = new JObject { ["type"] = "ProtectUnit", ["protect_text"] = "a" };
        var protect2 = new JObject { ["type"] = "ProtectUnit", ["protect_text"] = "b" };
        mission["steps"] = new JArray(
            new JObject { ["objectives"] = new JArray(protect1) },
            new JObject { ["objectives"] = new JArray(protect2) });
        var ex = Assert.Throws<LlmValidationException>(
            () => Validator().Parse(mission, Hostiles, NeutralAndHostileRep));
        Assert.Contains("degenerate", ex.Message, System.StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Parse_MixedProtectAndKill_Accepted()
    {
        var mission = BuildGood();
        var protect = new JObject { ["type"] = "ProtectUnit", ["protect_text"] = "guard" };
        var kill    = BuildKillObj("Marauders", 2);
        mission["steps"] = new JArray(
            new JObject { ["objectives"] = new JArray(protect) },
            new JObject { ["objectives"] = new JArray(kill) });
        var block = Validator().Parse(mission, Hostiles, NeutralAndHostileRep);
        Assert.Equal(2, block.Steps.Count);
    }

    // ---------- Helpers ----------

    /// <summary>Builds a minimal valid mission block (one KillEnemies step,
    /// one Credits reward) that every "modify-and-reject" test starts from.</summary>
    private static JObject BuildGood()
    {
        return new JObject
        {
            ["name"]            = "Test Mission",
            ["description"]     = "A test.",
            ["completion_text"] = "Nice work.",
            ["source_faction"]  = "TradingGuild",
            ["steps"]           = new JArray(BuildStep()),
            ["rewards"]         = new JArray(new JObject
            {
                ["type"]       = "Credits",
                ["base_value"] = 50,
            }),
        };
    }

    private static JObject BuildStep()
    {
        return new JObject
        {
            ["objectives"] = new JArray(BuildKillObj("Marauders", 1)),
        };
    }

    private static JObject BuildKillObj(string faction, int amount) => new()
    {
        ["type"]            = "KillEnemies",
        ["enemy_faction"]   = faction,
        ["required_amount"] = amount,
        ["description"]     = "Take them out.",
    };

    private static JObject BuildTriggerObj() => new()
    {
        ["type"]            = "TriggerObjective",
        ["trigger"]         = "DockedWithSpaceStation",
        ["required_amount"] = 1,
        ["description"]     = "Dock.",
    };

    private static JObject BuildWithoutField(string field)
    {
        var m = BuildGood();
        m.Remove(field);
        return m;
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run:
```
make test
```

Expected: compile errors — `LlmMissionBlock`, `LlmKillEnemies`, `LlmProtectUnit`, `LlmTriggerObjective`, `LlmCollectItemTypes`, `LlmCreditsReward`, `LlmExperienceReward`, `LlmReputationReward`, `MissionBlockValidator` don't exist.

- [ ] **Step 3: Create `LlmMissionBlock.cs`**

Create `VGAnima/Llm/LlmMissionBlock.cs`:

```csharp
using System.Collections.Generic;

namespace VGAnima.Llm;

/// <summary>Validated mission block (spec §2). Produced by
/// <see cref="MissionBlockValidator.Parse"/>; consumed by
/// <see cref="VGAnima.Missions.MissionFactoryFromJson"/>.
///
/// Everything is immutable — downstream code treats it as read-only
/// blueprint data.</summary>
internal sealed record LlmMissionBlock(
    string Name,
    string Description,
    string CompletionText,
    string SourceFaction,
    IReadOnlyList<LlmMissionStep> Steps,
    IReadOnlyList<LlmReward> Rewards);

internal sealed record LlmMissionStep(
    IReadOnlyList<LlmObjective> Objectives);

/// <summary>Base type for validated objectives. Instances are one of the
/// concrete subtypes below — callers pattern-match by type.</summary>
internal abstract record LlmObjective;

internal sealed record LlmKillEnemies(
    string EnemyFaction,
    int RequiredAmount,
    string Description) : LlmObjective;

internal sealed record LlmProtectUnit(
    string ProtectText) : LlmObjective;

internal sealed record LlmTriggerObjective(
    string Trigger,
    int RequiredAmount,
    string Description) : LlmObjective;

internal sealed record LlmCollectItemTypes(
    string ItemCategory,
    int RequiredAmount,
    string Description) : LlmObjective;

internal abstract record LlmReward;

internal sealed record LlmCreditsReward(int BaseValue) : LlmReward;

internal sealed record LlmExperienceReward(int BaseValue) : LlmReward;

internal sealed record LlmReputationReward(string Faction, int Amount) : LlmReward;
```

- [ ] **Step 4: Create `MissionBlockValidator.cs`**

Create `VGAnima/Llm/MissionBlockValidator.cs`:

```csharp
using System.Collections.Generic;
using Newtonsoft.Json.Linq;

namespace VGAnima.Llm;

/// <summary>Validates the <c>mission</c> sub-object of a v2-mission LLM
/// response. Called by <see cref="ResponseValidator"/> after the dialogue
/// block passes. Rule ordering mirrors spec §5 exactly — first failure
/// raises <see cref="LlmValidationException"/>.
///
/// Cross-context inputs (<paramref name="atWar"/> and
/// <paramref name="reputation"/>) drive the enemy_faction coherence rule
/// (spec §3 — reject KillEnemies against a currently-friendly faction).
/// Callers (the orchestrator in Task 6) thread them through from the same
/// <see cref="LlmContext"/> that was sent to the LLM.</summary>
internal sealed class MissionBlockValidator
{
    private const int NameMaxLen           = 60;
    private const int DescriptionMaxLen    = 500;
    private const int CompletionTextMaxLen = 200;
    private const int ObjDescriptionMaxLen = 80;
    private const int ProtectTextMaxLen    = 80;

    private const int KillRequiredMin      = 1;
    private const int KillRequiredMax      = 5;
    private const int TriggerRequiredMin   = 1;
    private const int TriggerRequiredMax   = 3;
    private const int CollectRequiredMin   = 1;
    private const int CollectRequiredMax   = 50;

    private const int CreditsBaseMin       = 10;
    private const int CreditsBaseMax       = 200;
    private const int ExperienceBaseMin    = 10;
    private const int ExperienceBaseMax    = 150;
    private const int ReputationMin        = -500;
    private const int ReputationMax        =  500;

    private static readonly HashSet<string> MissionKeys = new()
    {
        "name", "description", "completion_text",
        "source_faction", "steps", "rewards",
    };

    public LlmMissionBlock Parse(
        JToken mission,
        IReadOnlyList<string> atWar,
        IReadOnlyDictionary<string, int> reputation)
    {
        // Rule 1.
        if (mission == null || mission.Type != JTokenType.Object)
            throw new LlmValidationException(
                $"field `mission` must be a json object, got {(mission == null ? "null" : mission.Type.ToString())}");

        var obj = (JObject)mission;

        // Rule 2: strict field set.
        foreach (var prop in obj.Properties())
            if (!MissionKeys.Contains(prop.Name))
                throw new LlmValidationException(
                    $"unexpected field `mission.{prop.Name}` (v2-mission schema is strict)");
        foreach (var required in MissionKeys)
            if (!obj.ContainsKey(required))
                throw new LlmValidationException($"missing field `mission.{required}`");

        // Rule 3: string caps + ASCII.
        var name           = ReadString(obj, "mission.name",            NameMaxLen);
        var description    = ReadString(obj, "mission.description",     DescriptionMaxLen);
        var completionText = ReadString(obj, "mission.completion_text", CompletionTextMaxLen);

        // Rule 4: source_faction whitelist.
        var sourceFactionTok = obj["source_faction"]!;
        if (sourceFactionTok.Type != JTokenType.String)
            throw new LlmValidationException("field `mission.source_faction` must be a string");
        var sourceFaction = sourceFactionTok.Value<string>() ?? string.Empty;
        if (!FactionWhitelist.Contains(sourceFaction))
            throw new LlmValidationException(
                $"field `mission.source_faction` must be a whitelisted faction, got \"{sourceFaction}\"");

        // Rule 5: steps array, 1..3.
        var steps = ReadSteps(obj, atWar, reputation);

        // Rule 8: rewards array, 1..5.
        var rewards = ReadRewards(obj);

        // Rule 9: global coherence — at least one non-ProtectUnit objective.
        if (!AnyNonProtectObjective(steps))
            throw new LlmValidationException(
                "mission is degenerate: every objective is ProtectUnit (need at least one "
                + "KillEnemies / TriggerObjective / CollectItemTypes)");

        return new LlmMissionBlock(
            Name:           name,
            Description:    description,
            CompletionText: completionText,
            SourceFaction:  sourceFaction,
            Steps:          steps,
            Rewards:        rewards);
    }

    private static string ReadString(JObject obj, string path, int maxLen)
    {
        var tok = obj[PathTail(path)]!;
        if (tok.Type != JTokenType.String)
            throw new LlmValidationException($"field `{path}` must be a string, got {tok.Type}");
        var s = tok.Value<string>() ?? string.Empty;
        if (s.Trim().Length == 0)
            throw new LlmValidationException($"field `{path}` must be non-empty after trim");
        if (s.Length > maxLen)
            throw new LlmValidationException($"field `{path}` exceeds max length {maxLen} (got {s.Length})");
        for (var i = 0; i < s.Length; i++)
            if (s[i] >= 128)
                throw new LlmValidationException(
                    $"field `{path}` has non-ascii char U+{(int)s[i]:X4} at offset {i}");
        return s;
    }

    private static string PathTail(string path)
    {
        var dot = path.LastIndexOf('.');
        return dot < 0 ? path : path.Substring(dot + 1);
    }

    private IReadOnlyList<LlmMissionStep> ReadSteps(
        JObject obj,
        IReadOnlyList<string> atWar,
        IReadOnlyDictionary<string, int> reputation)
    {
        var stepsTok = obj["steps"]!;
        if (stepsTok.Type != JTokenType.Array)
            throw new LlmValidationException("field `mission.steps` must be an array");
        var stepsArr = (JArray)stepsTok;
        if (stepsArr.Count < 1 || stepsArr.Count > 3)
            throw new LlmValidationException(
                $"field `mission.steps` must have 1..3 elements, got {stepsArr.Count}");

        var steps = new List<LlmMissionStep>(stepsArr.Count);
        for (var i = 0; i < stepsArr.Count; i++)
        {
            var step = stepsArr[i];
            if (step.Type != JTokenType.Object)
                throw new LlmValidationException(
                    $"field `mission.steps[{i}]` must be an object");
            var stepObj = (JObject)step;
            foreach (var prop in stepObj.Properties())
                if (prop.Name != "objectives")
                    throw new LlmValidationException(
                        $"unexpected field `mission.steps[{i}].{prop.Name}`");
            if (!stepObj.ContainsKey("objectives"))
                throw new LlmValidationException(
                    $"missing field `mission.steps[{i}].objectives`");

            var objsTok = stepObj["objectives"]!;
            if (objsTok.Type != JTokenType.Array)
                throw new LlmValidationException(
                    $"field `mission.steps[{i}].objectives` must be an array");
            var objsArr = (JArray)objsTok;
            if (objsArr.Count < 1 || objsArr.Count > 2)
                throw new LlmValidationException(
                    $"field `mission.steps[{i}].objectives` must have 1..2 elements, got {objsArr.Count}");

            var objectives = new List<LlmObjective>(objsArr.Count);
            for (var j = 0; j < objsArr.Count; j++)
                objectives.Add(ParseObjective(objsArr[j], i, j, atWar, reputation));
            steps.Add(new LlmMissionStep(objectives));
        }
        return steps;
    }

    private LlmObjective ParseObjective(
        JToken tok, int stepIdx, int objIdx,
        IReadOnlyList<string> atWar,
        IReadOnlyDictionary<string, int> reputation)
    {
        var path = $"mission.steps[{stepIdx}].objectives[{objIdx}]";
        if (tok.Type != JTokenType.Object)
            throw new LlmValidationException($"field `{path}` must be an object");
        var obj = (JObject)tok;

        var typeTok = obj["type"];
        if (typeTok == null || typeTok.Type != JTokenType.String)
            throw new LlmValidationException($"field `{path}.type` missing or not a string");
        var type = typeTok.Value<string>() ?? string.Empty;
        if (!ObjectiveTypeWhitelist.Contains(type))
            throw new LlmValidationException(
                $"field `{path}.type` must be a whitelisted objective type, got \"{type}\"");

        return type switch
        {
            "KillEnemies"      => ParseKillEnemies(obj, path, atWar, reputation),
            "ProtectUnit"      => ParseProtectUnit(obj, path),
            "TriggerObjective" => ParseTriggerObjective(obj, path),
            "CollectItemTypes" => ParseCollectItemTypes(obj, path),
            _                  => throw new LlmValidationException(
                                      $"unreachable: whitelist passed but switch missed \"{type}\""),
        };
    }

    private LlmObjective ParseKillEnemies(
        JObject obj, string path,
        IReadOnlyList<string> atWar,
        IReadOnlyDictionary<string, int> reputation)
    {
        RequireStrictKeys(obj, path, "type", "enemy_faction", "required_amount", "description");

        var faction = obj["enemy_faction"]!.Value<string>() ?? string.Empty;
        if (!FactionWhitelist.Contains(faction))
            throw new LlmValidationException(
                $"field `{path}.enemy_faction` must be a whitelisted faction, got \"{faction}\"");

        // Spec §3: reject if the faction is friendly (reputation > 0 AND not at war).
        var isAtWar  = atWar != null && atWar.Contains(faction);
        var hasRep   = reputation != null && reputation.TryGetValue(faction, out var rep);
        if (!isAtWar && hasRep && rep > 0)
            throw new LlmValidationException(
                $"field `{path}.enemy_faction` is friendly to player (rep={rep}); refusing kill mission");

        var required = ReadInt(obj, $"{path}.required_amount", KillRequiredMin, KillRequiredMax);
        var desc     = ReadObjectiveDescription(obj, $"{path}.description");
        return new LlmKillEnemies(faction, required, desc);
    }

    private LlmObjective ParseProtectUnit(JObject obj, string path)
    {
        RequireStrictKeys(obj, path, "type", "protect_text");
        var text = obj["protect_text"]!;
        if (text.Type != JTokenType.String)
            throw new LlmValidationException($"field `{path}.protect_text` must be a string");
        var s = text.Value<string>() ?? string.Empty;
        if (s.Trim().Length == 0)
            throw new LlmValidationException($"field `{path}.protect_text` must be non-empty");
        if (s.Length > ProtectTextMaxLen)
            throw new LlmValidationException(
                $"field `{path}.protect_text` exceeds max length {ProtectTextMaxLen}");
        for (var i = 0; i < s.Length; i++)
            if (s[i] >= 128)
                throw new LlmValidationException(
                    $"field `{path}.protect_text` has non-ascii char U+{(int)s[i]:X4}");
        return new LlmProtectUnit(s);
    }

    private LlmObjective ParseTriggerObjective(JObject obj, string path)
    {
        RequireStrictKeys(obj, path, "type", "trigger", "required_amount", "description");
        var trigTok = obj["trigger"]!;
        if (trigTok.Type != JTokenType.String)
            throw new LlmValidationException($"field `{path}.trigger` must be a string");
        var trigger = trigTok.Value<string>() ?? string.Empty;
        if (!TriggerWhitelist.Contains(trigger))
            throw new LlmValidationException(
                $"field `{path}.trigger` must be a whitelisted trigger, got \"{trigger}\"");
        var required = ReadInt(obj, $"{path}.required_amount", TriggerRequiredMin, TriggerRequiredMax);
        var desc     = ReadObjectiveDescription(obj, $"{path}.description");
        return new LlmTriggerObjective(trigger, required, desc);
    }

    private LlmObjective ParseCollectItemTypes(JObject obj, string path)
    {
        RequireStrictKeys(obj, path, "type", "item_category", "required_amount", "description");
        var catTok = obj["item_category"]!;
        if (catTok.Type != JTokenType.String)
            throw new LlmValidationException($"field `{path}.item_category` must be a string");
        var cat = catTok.Value<string>() ?? string.Empty;
        if (!ItemCategoryWhitelist.Contains(cat))
            throw new LlmValidationException(
                $"field `{path}.item_category` must be a whitelisted category, got \"{cat}\"");
        var required = ReadInt(obj, $"{path}.required_amount", CollectRequiredMin, CollectRequiredMax);
        var desc     = ReadObjectiveDescription(obj, $"{path}.description");
        return new LlmCollectItemTypes(cat, required, desc);
    }

    private IReadOnlyList<LlmReward> ReadRewards(JObject obj)
    {
        var tok = obj["rewards"]!;
        if (tok.Type != JTokenType.Array)
            throw new LlmValidationException("field `mission.rewards` must be an array");
        var arr = (JArray)tok;
        if (arr.Count < 1 || arr.Count > 5)
            throw new LlmValidationException(
                $"field `mission.rewards` must have 1..5 elements, got {arr.Count}");

        var rewards = new List<LlmReward>(arr.Count);
        for (var i = 0; i < arr.Count; i++)
            rewards.Add(ParseReward(arr[i], i));
        return rewards;
    }

    private LlmReward ParseReward(JToken tok, int idx)
    {
        var path = $"mission.rewards[{idx}]";
        if (tok.Type != JTokenType.Object)
            throw new LlmValidationException($"field `{path}` must be an object");
        var obj = (JObject)tok;

        var typeTok = obj["type"];
        if (typeTok == null || typeTok.Type != JTokenType.String)
            throw new LlmValidationException($"field `{path}.type` missing or not a string");
        var type = typeTok.Value<string>() ?? string.Empty;
        if (!RewardTypeWhitelist.Contains(type))
            throw new LlmValidationException(
                $"field `{path}.type` must be a whitelisted reward type, got \"{type}\"");

        return type switch
        {
            "Credits"    => ParseCreditsReward(obj, path),
            "Experience" => ParseExperienceReward(obj, path),
            "Reputation" => ParseReputationReward(obj, path),
            _            => throw new LlmValidationException(
                                $"unreachable: whitelist passed but switch missed \"{type}\""),
        };
    }

    private LlmReward ParseCreditsReward(JObject obj, string path)
    {
        RequireStrictKeys(obj, path, "type", "base_value");
        var v = ReadInt(obj, $"{path}.base_value", CreditsBaseMin, CreditsBaseMax);
        return new LlmCreditsReward(v);
    }

    private LlmReward ParseExperienceReward(JObject obj, string path)
    {
        RequireStrictKeys(obj, path, "type", "base_value");
        var v = ReadInt(obj, $"{path}.base_value", ExperienceBaseMin, ExperienceBaseMax);
        return new LlmExperienceReward(v);
    }

    private LlmReward ParseReputationReward(JObject obj, string path)
    {
        RequireStrictKeys(obj, path, "type", "faction", "amount");
        var factionTok = obj["faction"]!;
        if (factionTok.Type != JTokenType.String)
            throw new LlmValidationException($"field `{path}.faction` must be a string");
        var faction = factionTok.Value<string>() ?? string.Empty;
        if (!FactionWhitelist.Contains(faction))
            throw new LlmValidationException(
                $"field `{path}.faction` must be a whitelisted faction, got \"{faction}\"");
        var amount = ReadInt(obj, $"{path}.amount", ReputationMin, ReputationMax);
        return new LlmReputationReward(faction, amount);
    }

    private static string ReadObjectiveDescription(JObject obj, string path)
    {
        var tok = obj["description"]!;
        if (tok.Type != JTokenType.String)
            throw new LlmValidationException($"field `{path}` must be a string");
        var s = tok.Value<string>() ?? string.Empty;
        if (s.Trim().Length == 0)
            throw new LlmValidationException($"field `{path}` must be non-empty");
        if (s.Length > ObjDescriptionMaxLen)
            throw new LlmValidationException(
                $"field `{path}` exceeds max length {ObjDescriptionMaxLen} (got {s.Length})");
        for (var i = 0; i < s.Length; i++)
            if (s[i] >= 128)
                throw new LlmValidationException(
                    $"field `{path}` has non-ascii char U+{(int)s[i]:X4}");
        return s;
    }

    private static int ReadInt(JObject obj, string path, int min, int max)
    {
        var tail = PathTail(path);
        var tok = obj[tail]!;
        if (tok.Type != JTokenType.Integer)
            throw new LlmValidationException($"field `{path}` must be an integer");
        var v = tok.Value<int>();
        if (v < min || v > max)
            throw new LlmValidationException($"field `{path}` must be in [{min}..{max}], got {v}");
        return v;
    }

    private static void RequireStrictKeys(JObject obj, string path, params string[] allowed)
    {
        var set = new HashSet<string>(allowed);
        foreach (var prop in obj.Properties())
            if (!set.Contains(prop.Name))
                throw new LlmValidationException(
                    $"unexpected field `{path}.{prop.Name}`");
        foreach (var key in allowed)
            if (!obj.ContainsKey(key))
                throw new LlmValidationException($"missing field `{path}.{key}`");
    }

    private static bool AnyNonProtectObjective(IReadOnlyList<LlmMissionStep> steps)
    {
        foreach (var step in steps)
            foreach (var obj in step.Objectives)
                if (obj is not LlmProtectUnit)
                    return true;
        return false;
    }
}
```

- [ ] **Step 5: Run tests to verify they pass**

Run:
```
make test
```

Expected: all ~40 `MissionBlockValidatorTests` pass. Existing tests still green.

- [ ] **Step 6: Commit**

```bash
git add VGAnima/Llm/LlmMissionBlock.cs VGAnima/Llm/MissionBlockValidator.cs \
  VGAnima.Tests/Llm/MissionBlockValidatorTests.cs
git -c user.name="Florian Kinder" -c user.email="florian.kinder@fankserver.com" commit -m "$(cat <<'EOF'
feat: MissionBlockValidator for v2-mission schema

Strict validator for the mission sub-object per spec §5. Enforces field
set, string caps, ASCII-only, per-objective-type rules, reward clamps,
and the degenerate-ProtectUnit-only rule. Uses the Task 1 whitelists.
Cross-context enemy_faction hostility check accepts at_war OR negative
reputation, rejecting unambiguously-friendly factions.

Materializes the validated JSON into an LlmMissionBlock record tree
(LlmKillEnemies / LlmProtectUnit / LlmTriggerObjective /
LlmCollectItemTypes / LlmCreditsReward / LlmExperienceReward /
LlmReputationReward) ready for the factory in a later task.
EOF
)"
```

---

## Task 3: Schema dispatch in `ResponseValidator`

**Files:**
- Modify: `VGAnima/Llm/LlmStory.cs`
- Modify: `VGAnima/Llm/ResponseValidator.cs`
- Modify: `VGAnima.Tests/Llm/ResponseValidatorTests.cs`

Extend `LlmStory` with an optional `Mission` property. Teach `ResponseValidator` to dispatch on the `schema` value: `vganima/story/v1` continues to work unchanged (Mission stays null), `vganima/mission/v1` requires the `mission` field and calls `MissionBlockValidator`. Any other schema string is rejected.

The dialogue arrays keep their existing rules in both schemas. The v2 schema adds exactly one required top-level key (`mission`) to the dialogue keyset.

Since the cross-context inputs (at_war + reputation) aren't available to the validator from JSON alone, the `ResponseValidator.Parse` method gains optional parameters — null means "no cross-check" (i.e. enemy_faction passes based on whitelist membership only). The orchestrator (Task 6) threads real values in.

- [ ] **Step 1: Update `LlmStory.cs`**

Replace `VGAnima/Llm/LlmStory.cs` contents:

```csharp
using System.Collections.Generic;

namespace VGAnima.Llm;

/// <summary>Validated output from a successful LLM response. Immutable.
/// Cached inside <see cref="VGAnima.Cache.ConversionRecord"/> for the broker's
/// lifetime and replayed by <see cref="VGAnima.Pitch.LlmPitchProvider"/>.
///
/// <para>The dialogue properties (<see cref="Pitch"/> / <see cref="CheckIn"/> /
/// <see cref="Payout"/>) are always populated. The <see cref="Mission"/>
/// property is non-null only for the v2-mission schema — v1 (dialogue-only)
/// responses leave it null.</para>
///
/// Property names <c>Pitch</c> / <c>CheckIn</c> / <c>Payout</c> are stable —
/// <see cref="VGAnima.Pitch.LlmPitchProvider"/> keys on exactly these names.</summary>
internal sealed record LlmStory(
    IReadOnlyList<string> Pitch,
    IReadOnlyList<string> CheckIn,
    IReadOnlyList<string> Payout,
    LlmMissionBlock? Mission = null);
```

- [ ] **Step 2: Add failing tests in `ResponseValidatorTests.cs`**

Append the following `[Fact]` methods at the end of the `ResponseValidatorTests` class (inside the closing `}`):

```csharp
    // ---------- v2-mission schema dispatch ----------

    private const string ValidMissionPayload = @"{
        ""schema"": ""vganima/mission/v1"",
        ""pitch"":    [""Line 1."", ""Line 2."", ""Line 3.""],
        ""check_in"": [""Any luck?""],
        ""payout"":   [""Good job."", ""Here's your cut.""],
        ""mission"": {
            ""name"":            ""Test Run"",
            ""description"":     ""Go do a thing."",
            ""completion_text"": ""Thanks."",
            ""source_faction"":  ""TradingGuild"",
            ""steps"": [
                { ""objectives"": [
                    { ""type"": ""TriggerObjective"",
                      ""trigger"": ""DockedWithSpaceStation"",
                      ""required_amount"": 1,
                      ""description"": ""Dock."" } ] } ],
            ""rewards"": [
                { ""type"": ""Credits"", ""base_value"": 50 } ] } }";

    [Fact]
    public void Parse_V2Mission_HappyPath_ReturnsStoryWithMission()
    {
        var story = new ResponseValidator().Parse(ValidMissionPayload);
        Assert.NotNull(story.Mission);
        Assert.Equal("Test Run",     story.Mission!.Name);
        Assert.Equal("TradingGuild", story.Mission.SourceFaction);
        Assert.Single(story.Mission.Steps);
        Assert.Single(story.Mission.Rewards);
    }

    [Fact]
    public void Parse_V1Schema_StillWorks_MissionStaysNull()
    {
        var story = new ResponseValidator().Parse(ValidPayload);
        Assert.Null(story.Mission);
    }

    [Fact]
    public void Parse_V2Mission_MissingMissionField_Rejects()
    {
        const string payload = @"{
            ""schema"": ""vganima/mission/v1"",
            ""pitch"":    [""a"",""b"",""c""],
            ""check_in"": [""d""],
            ""payout"":   [""e"",""f""] }";
        var ex = Assert.Throws<LlmValidationException>(
            () => new ResponseValidator().Parse(payload));
        Assert.Contains("mission", ex.Message);
    }

    [Fact]
    public void Parse_V2Mission_ExtraTopLevelField_Rejects()
    {
        var payload = ValidMissionPayload.Replace(
            @"""rewards"": [",
            @"""extra"": ""stuff"", ""rewards"": [");
        // Inject an unrelated extra key at the root, not inside mission.
        var brokenRoot = ValidMissionPayload.TrimEnd('}') + @", ""junk"": ""stuff"" }";
        Assert.Throws<LlmValidationException>(
            () => new ResponseValidator().Parse(brokenRoot));
    }

    [Fact]
    public void Parse_V2Mission_DispatchesToMissionValidator_OnBadObjective()
    {
        var broken = ValidMissionPayload.Replace(
            @"""DockedWithSpaceStation""",
            @"""BountyTargetKilled""");
        var ex = Assert.Throws<LlmValidationException>(
            () => new ResponseValidator().Parse(broken));
        Assert.Contains("trigger", ex.Message);
    }

    [Fact]
    public void Parse_UnknownSchema_Rejects()
    {
        var payload = ValidPayload.Replace(
            "vganima/story/v1",
            "vganima/story/v99");
        var ex = Assert.Throws<LlmValidationException>(
            () => new ResponseValidator().Parse(payload));
        Assert.Contains("schema", ex.Message, System.StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Parse_V2Mission_WithAtWarContext_AcceptsHostileKill()
    {
        var payload = ValidMissionPayload.Replace(
            @"{ ""type"": ""TriggerObjective"",
                      ""trigger"": ""DockedWithSpaceStation"",
                      ""required_amount"": 1,
                      ""description"": ""Dock."" }",
            @"{ ""type"": ""KillEnemies"",
                      ""enemy_faction"": ""Marauders"",
                      ""required_amount"": 2,
                      ""description"": ""Kill them."" }");

        var atWar = new[] { "Marauders" };
        var rep   = new System.Collections.Generic.Dictionary<string, int>();
        var story = new ResponseValidator().Parse(payload, atWar, rep);
        Assert.NotNull(story.Mission);
        Assert.IsType<LlmKillEnemies>(story.Mission!.Steps[0].Objectives[0]);
    }

    [Fact]
    public void Parse_V2Mission_WithFriendlyContext_RejectsKillAlly()
    {
        var payload = ValidMissionPayload.Replace(
            @"{ ""type"": ""TriggerObjective"",
                      ""trigger"": ""DockedWithSpaceStation"",
                      ""required_amount"": 1,
                      ""description"": ""Dock."" }",
            @"{ ""type"": ""KillEnemies"",
                      ""enemy_faction"": ""TradingGuild"",
                      ""required_amount"": 2,
                      ""description"": ""Kill allies."" }");

        var atWar = System.Array.Empty<string>();
        var rep   = new System.Collections.Generic.Dictionary<string, int>
        {
            { "TradingGuild", 100 },
        };
        var ex = Assert.Throws<LlmValidationException>(
            () => new ResponseValidator().Parse(payload, atWar, rep));
        Assert.Contains("enemy_faction", ex.Message);
    }
```

- [ ] **Step 3: Run tests to verify they fail**

Run:
```
make test
```

Expected: compile error — `ResponseValidator.Parse` doesn't accept context parameters + v2 schema unrecognised.

- [ ] **Step 4: Update `ResponseValidator.cs`**

Replace `VGAnima/Llm/ResponseValidator.cs` contents:

```csharp
using System;
using System.Collections.Generic;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace VGAnima.Llm;

/// <summary>Strict parser + validator for LLM output. Dispatches on the
/// <c>schema</c> field: <c>vganima/story/v1</c> produces a dialogue-only
/// <see cref="LlmStory"/>, <c>vganima/mission/v1</c> additionally parses
/// the <c>mission</c> sub-object via <see cref="MissionBlockValidator"/>.
///
/// The dialogue rules (pitch / check_in / payout sizes + ASCII) are identical
/// across schemas. The v2 schema adds exactly one required top-level field
/// (<c>mission</c>) to the strict keyset.
///
/// Cross-context hostility inputs (<c>atWar</c>, <c>reputation</c>) are
/// optional — null means "skip the enemy_faction-is-friendly check". Unit
/// tests without a full context pass null; the orchestrator in
/// <see cref="VGAnima.Patches.BarRefreshPatches"/> threads real values in.
///
/// Uses <see cref="Newtonsoft.Json.Linq.JToken"/> (game-provided Newtonsoft)
/// — see comment in the v1 header for why System.Text.Json is off-limits on
/// Unity 6000.2's Mono profile.</summary>
internal sealed class ResponseValidator
{
    public const string ExpectedSchemaV1 = "vganima/story/v1";
    public const string ExpectedSchemaV2 = "vganima/mission/v1";

    private static readonly HashSet<string> DialogueKeys = new()
    {
        "schema", "pitch", "check_in", "payout",
    };

    private static readonly HashSet<string> MissionKeys = new()
    {
        "schema", "pitch", "check_in", "payout", "mission",
    };

    private readonly MissionBlockValidator _missionValidator = new();

    /// <summary>Parse with no cross-context — hostile-faction rule in the
    /// mission-block validator is skipped. Used by tests and for the v1
    /// dialogue-only path.</summary>
    public LlmStory Parse(string rawContent)
        => Parse(rawContent, atWar: null, reputation: null);

    public LlmStory Parse(
        string rawContent,
        IReadOnlyList<string>? atWar,
        IReadOnlyDictionary<string, int>? reputation)
    {
        JToken root;
        try
        {
            root = JToken.Parse(rawContent);
        }
        catch (JsonException ex)
        {
            throw new LlmValidationException(
                $"content is not valid json ({ex.Message})", ex);
        }

        if (root.Type != JTokenType.Object)
            throw new LlmValidationException(
                $"root must be a json object, got {root.Type}");

        var obj = (JObject)root;

        if (!obj.TryGetValue("schema", out var schemaTok))
            throw new LlmValidationException("missing field `schema`");
        if (schemaTok.Type != JTokenType.String)
            throw new LlmValidationException("field `schema` must be a string");

        var schemaValue = schemaTok.Value<string>();
        var expectedKeys = schemaValue switch
        {
            ExpectedSchemaV1 => DialogueKeys,
            ExpectedSchemaV2 => MissionKeys,
            _                => throw new LlmValidationException(
                                    $"field `schema` must be \"{ExpectedSchemaV1}\" "
                                    + $"or \"{ExpectedSchemaV2}\", got \"{schemaValue}\""),
        };

        foreach (var prop in obj.Properties())
            if (!expectedKeys.Contains(prop.Name))
                throw new LlmValidationException(
                    $"unexpected top-level field `{prop.Name}` (schema is strict)");

        if (!obj.TryGetValue("pitch",    out var pitchTok))    throw new LlmValidationException("missing field `pitch`");
        if (!obj.TryGetValue("check_in", out var checkInTok))  throw new LlmValidationException("missing field `check_in`");
        if (!obj.TryGetValue("payout",   out var payoutTok))   throw new LlmValidationException("missing field `payout`");

        var pitch   = ReadStringArray("pitch",    pitchTok,   minCount: 3, maxCount: 5);
        var checkIn = ReadStringArray("check_in", checkInTok, minCount: 1, maxCount: 2);
        var payout  = ReadStringArray("payout",   payoutTok,  minCount: 2, maxCount: 4);

        LlmMissionBlock? mission = null;
        if (schemaValue == ExpectedSchemaV2)
        {
            if (!obj.TryGetValue("mission", out var missionTok))
                throw new LlmValidationException("missing field `mission`");
            mission = _missionValidator.Parse(
                missionTok,
                atWar      ?? Array.Empty<string>(),
                reputation ?? new Dictionary<string, int>());
        }

        return new LlmStory(pitch, checkIn, payout, mission);
    }

    private static IReadOnlyList<string> ReadStringArray(string fieldName, JToken token, int minCount, int maxCount)
    {
        if (token.Type != JTokenType.Array)
            throw new LlmValidationException(
                $"field `{fieldName}` must be an array, got {token.Type}");

        var arr = (JArray)token;
        var count = arr.Count;
        if (count < minCount || count > maxCount)
            throw new LlmValidationException(
                $"field `{fieldName}` must have {minCount}..{maxCount} elements, got {count}");

        var list = new List<string>(count);
        for (var index = 0; index < count; index++)
        {
            var el = arr[index];
            if (el.Type != JTokenType.String)
                throw new LlmValidationException(
                    $"field `{fieldName}`[{index}] must be a string, got {el.Type}");

            var s = el.Value<string>() ?? string.Empty;
            ValidateString(fieldName, index, s);
            list.Add(s);
        }
        return list;
    }

    private static void ValidateString(string fieldName, int index, string s)
    {
        if (s.Trim().Length == 0)
            throw new LlmValidationException(
                $"field `{fieldName}`[{index}] must be non-empty after trim");

        if (s.Length > 120)
            throw new LlmValidationException(
                $"field `{fieldName}`[{index}] exceeds max length 120 (got {s.Length})");

        if (s.Length != s.Trim().Length)
            throw new LlmValidationException(
                $"field `{fieldName}`[{index}] must not have leading/trailing whitespace");

        for (var i = 0; i < s.Length; i++)
        {
            if (s[i] >= 128)
                throw new LlmValidationException(
                    $"field `{fieldName}`[{index}] has non-ascii char U+{(int)s[i]:X4} at offset {i} (rule: ascii-only)");
        }
    }
}
```

- [ ] **Step 5: Run tests to verify they pass**

Run:
```
make test
```

Expected: all existing `ResponseValidatorTests` + the new v2 dispatch tests pass. `MissionBlockValidatorTests` still pass.

- [ ] **Step 6: Commit**

```bash
git add VGAnima/Llm/LlmStory.cs VGAnima/Llm/ResponseValidator.cs \
  VGAnima.Tests/Llm/ResponseValidatorTests.cs
git -c user.name="Florian Kinder" -c user.email="florian.kinder@fankserver.com" commit -m "$(cat <<'EOF'
feat: schema dispatch in ResponseValidator for v2-mission

Adds vganima/mission/v1 alongside v1. Dialogue rules unchanged; v2
additionally validates a mission block via MissionBlockValidator and
populates the new optional LlmStory.Mission property. Unknown schema
values rejected. Cross-context at_war / reputation inputs threaded
through to the mission validator as optional params (null => skip
hostility check).
EOF
)"
```

---

## Task 4: `MissionFactoryFromJson`

**Files:**
- Create: `VGAnima/Missions/MissionFactoryFromJson.cs`
- Create: `VGAnima.Tests/Missions/MissionFactoryFromJsonTests.cs`

Translates a validated `LlmMissionBlock` + player level + broker station into a live `Source.MissionSystem.Mission`. Per-type mappers for each objective / reward type. Uses `GameMath.GetCreditsValue` / `GameMath.GetExperienceRewardValue` for reward scaling (spec §4 clamp applies before scaling, so the runtime value stays in a sane range even at high levels).

Player level is threaded in as a parameter — the factory never touches `GamePlayer.current` directly, which keeps the class unit-testable. The broker station is passed in as `SpaceStation`; tests exercise `null` station (factory tolerates null and leaves `sourcePoi`/`turnIn` null, logging a warning).

`Faction.Get` and `Source.Item.ItemCategory` parsing happen here — these are first-touch calls to the game API. Factory is invoked on the Unity main thread (post-Scheduler.Enqueue), so accessing `Faction.Get` is safe.

- [ ] **Step 1: Write failing tests**

Create `VGAnima.Tests/Missions/MissionFactoryFromJsonTests.cs`:

```csharp
using System.Collections.Generic;
using Source.MissionSystem;
using Source.MissionSystem.Objectives;
using Source.MissionSystem.Rewards;
using VGAnima.Llm;
using VGAnima.Missions;
using Xunit;
// Rewards.Reputation conflicts with the game's other Reputation — alias for
// clarity in assertions below.
using ReputationReward = Source.MissionSystem.Rewards.Reputation;

namespace VGAnima.Tests.Missions;

public class MissionFactoryFromJsonTests
{
    [Fact]
    public void Build_CopiesTopLevelFields()
    {
        var block = new LlmMissionBlock(
            Name:           "Smash and Grab",
            Description:    "A job.",
            CompletionText: "Nicely done.",
            SourceFaction:  "TradingGuild",
            Steps:          new[] { StepWithTrigger("DockedWithSpaceStation") },
            Rewards:        new LlmReward[] { new LlmCreditsReward(50) });

        var mission = MissionFactoryFromJson.Build(
            block, playerLevel: 5, brokerStation: null, brokerSeed: "broker-0");

        Assert.Equal("Smash and Grab", mission.name);
        Assert.Equal("A job.",         mission.description);
        Assert.Equal("Nicely done.",   mission.completionText);
        Assert.Equal("TradingGuild",   mission.sourceFaction.identifier);
        Assert.True(mission.trackedOnHud);
        Assert.True(mission.dynamicLevel);
        Assert.False(mission.canBeIdled);
        Assert.Equal(MissionDifficulty.Story, mission.difficulty);
        Assert.StartsWith("vganima_llm_", mission.storyId);
        Assert.Contains("broker-0",       mission.storyId);
    }

    [Fact]
    public void Build_CreatesTriggerObjective()
    {
        var block = new LlmMissionBlock(
            Name: "T", Description: "d", CompletionText: "c", SourceFaction: "TradingGuild",
            Steps: new[]
            {
                new LlmMissionStep(new LlmObjective[]
                {
                    new LlmTriggerObjective("ArrivedAtSpaceStation", 2, "Arrive twice."),
                }),
            },
            Rewards: new LlmReward[] { new LlmCreditsReward(50) });

        var mission = MissionFactoryFromJson.Build(block, 5, null, "seed");

        Assert.Single(mission.steps);
        var objs = mission.steps[0].objectives;
        Assert.Single(objs);
        var trig = Assert.IsType<TriggerObjective>(objs[0]);
        Assert.Equal(MissionTrigger.ArrivedAtSpaceStation, trig.trigger);
        Assert.Equal(2, trig.requiredAmount);
        Assert.Equal("Arrive twice.", trig.description);
    }

    [Fact]
    public void Build_CreatesKillEnemies()
    {
        var block = new LlmMissionBlock(
            Name: "T", Description: "d", CompletionText: "c", SourceFaction: "TradingGuild",
            Steps: new[]
            {
                new LlmMissionStep(new LlmObjective[]
                {
                    new LlmKillEnemies("Marauders", 3, "Kill marauders."),
                }),
            },
            Rewards: new LlmReward[] { new LlmCreditsReward(50) });

        var mission = MissionFactoryFromJson.Build(block, 5, null, "seed");
        var kill = Assert.IsType<KillEnemies>(mission.steps[0].objectives[0]);
        Assert.Equal("Marauders", kill.enemyFaction.identifier);
        Assert.Equal(3, kill.requiredAmount);
    }

    [Fact]
    public void Build_CreatesProtectUnit()
    {
        var block = new LlmMissionBlock(
            Name: "T", Description: "d", CompletionText: "c", SourceFaction: "TradingGuild",
            Steps: new[]
            {
                new LlmMissionStep(new LlmObjective[]
                {
                    new LlmProtectUnit("Keep the convoy alive."),
                    new LlmKillEnemies("Marauders", 1, "Kill one."),
                }),
            },
            Rewards: new LlmReward[] { new LlmCreditsReward(50) });

        var mission = MissionFactoryFromJson.Build(block, 5, null, "seed");
        var protect = Assert.IsType<ProtectUnit>(mission.steps[0].objectives[0]);
        Assert.Equal("Keep the convoy alive.", protect.protectText);
    }

    [Fact]
    public void Build_CreatesCollectItemTypes_WithCategory()
    {
        var block = new LlmMissionBlock(
            Name: "T", Description: "d", CompletionText: "c", SourceFaction: "TradingGuild",
            Steps: new[]
            {
                new LlmMissionStep(new LlmObjective[]
                {
                    new LlmCollectItemTypes("Ore", 5, "Collect ore."),
                }),
            },
            Rewards: new LlmReward[] { new LlmCreditsReward(50) });

        var mission = MissionFactoryFromJson.Build(block, 5, null, "seed");
        var collect = Assert.IsType<CollectItemTypes>(mission.steps[0].objectives[0]);
        Assert.Equal(Source.Item.ItemCategory.Ore, collect.itemCategory);
        Assert.Equal(5, collect.requiredAmount);
    }

    [Fact]
    public void Build_CreditsReward_Scaled_ByPlayerLevel()
    {
        var block = new LlmMissionBlock(
            Name: "T", Description: "d", CompletionText: "c", SourceFaction: "TradingGuild",
            Steps:   new[] { StepWithTrigger("DockedWithSpaceStation") },
            Rewards: new LlmReward[] { new LlmCreditsReward(100) });

        var mission = MissionFactoryFromJson.Build(block, playerLevel: 5, null, "seed");
        var credits = Assert.IsType<Credits>(mission.rewards[0]);
        // GameMath.GetCreditsValue(100, 5) — exact number depends on the game's
        // CostMultiplier curve, but must be strictly positive and much larger
        // than the base_value input (it's scaled by 100 + the curve).
        Assert.True(credits.amount > 100,
            $"scaled credits {credits.amount} should be > base_value 100");
    }

    [Fact]
    public void Build_ReputationReward_CarriesFaction()
    {
        var block = new LlmMissionBlock(
            Name: "T", Description: "d", CompletionText: "c", SourceFaction: "TradingGuild",
            Steps:   new[] { StepWithTrigger("DockedWithSpaceStation") },
            Rewards: new LlmReward[]
            {
                new LlmReputationReward("Marauders", -200),
            });

        var mission = MissionFactoryFromJson.Build(block, 5, null, "seed");
        var rep = Assert.IsType<ReputationReward>(mission.rewards[0]);
        Assert.Equal("Marauders", rep.faction.identifier);
        Assert.Equal(-200, rep.amount);
    }

    [Fact]
    public void Build_MultipleSteps_PreservesOrder()
    {
        var block = new LlmMissionBlock(
            Name: "T", Description: "d", CompletionText: "c", SourceFaction: "TradingGuild",
            Steps: new[]
            {
                new LlmMissionStep(new LlmObjective[]
                {
                    new LlmTriggerObjective("DockedWithSpaceStation", 1, "Dock."),
                }),
                new LlmMissionStep(new LlmObjective[]
                {
                    new LlmKillEnemies("Marauders", 2, "Kill two."),
                }),
            },
            Rewards: new LlmReward[] { new LlmCreditsReward(50) });

        var mission = MissionFactoryFromJson.Build(block, 5, null, "seed");
        Assert.Equal(2, mission.steps.Count);
        Assert.IsType<TriggerObjective>(mission.steps[0].objectives[0]);
        Assert.IsType<KillEnemies>(mission.steps[1].objectives[0]);
    }

    [Fact]
    public void Build_StoryId_IsUniqueAcrossCalls()
    {
        var block = Minimal();
        var a = MissionFactoryFromJson.Build(block, 5, null, "same-seed").storyId;
        var b = MissionFactoryFromJson.Build(block, 5, null, "same-seed").storyId;
        Assert.NotEqual(a, b);  // Guid suffix differs
    }

    // ---------- Helpers ----------

    private static LlmMissionStep StepWithTrigger(string trigger) =>
        new(new LlmObjective[]
        {
            new LlmTriggerObjective(trigger, 1, "Do the thing."),
        });

    private static LlmMissionBlock Minimal() => new(
        Name: "T", Description: "d", CompletionText: "c", SourceFaction: "TradingGuild",
        Steps:   new[] { StepWithTrigger("DockedWithSpaceStation") },
        Rewards: new LlmReward[] { new LlmCreditsReward(50) });
}
```

Note on `GameMath.GetExperienceRewardValue` — it reads `GamePlayer.current.commander.level` internally, so calling it from a unit test without a real GamePlayer would NRE. The factory still exposes Experience as an output path, but the reward-scaling unit test is limited to Credits (which only takes the level parameter we supply). Experience correctness is left to manual E2E.

- [ ] **Step 2: Run tests to verify they fail**

Run:
```
make test
```

Expected: compile error — `MissionFactoryFromJson` doesn't exist.

- [ ] **Step 3: Create `MissionFactoryFromJson.cs`**

Create `VGAnima/Missions/MissionFactoryFromJson.cs`:

```csharp
using System;
using Source.Galaxy;
using Source.Galaxy.POI;
using Source.Item;
using Source.MissionSystem;
using Source.MissionSystem.Objectives;
using Source.MissionSystem.Rewards;
using Source.Util;
using VGAnima.Llm;
// Disambiguate between the reward type and the registry type sharing the
// short name "StoryMission".
using StoryMissionRegistry = Source.MissionSystem.StoryMission;

namespace VGAnima.Missions;

/// <summary>Translates a validated <see cref="LlmMissionBlock"/> into a live
/// <see cref="Mission"/> instance. Called on the Unity main thread from the
/// factory delegate the plugin registers via <c>StoryMission.Add</c> at
/// broker-injection time.
///
/// Faction identifiers and item categories are resolved via vanilla APIs
/// (<c>Faction.Get</c>, <c>Enum.Parse&lt;ItemCategory&gt;</c>). The Task 2
/// whitelists have already filtered the values, so these calls always
/// succeed under normal operation — any failure here (e.g. game updated the
/// faction registry) surfaces as an exception caught by the caller's
/// try/catch in <see cref="VGAnima.Patches.BarRefreshPatches"/>.
///
/// Spec §6.</summary>
internal static class MissionFactoryFromJson
{
    /// <summary>Builds the Mission from a validated block. All decomp-typed
    /// touches happen here; tests call this directly (skipping the caller's
    /// <c>StoryMission.Add</c> path) and assert on the returned Mission's
    /// field values.</summary>
    /// <param name="brokerStation">May be null in unit tests — production
    /// always passes the SpaceStation the broker was injected at.</param>
    public static Mission Build(
        LlmMissionBlock block,
        int playerLevel,
        SpaceStation? brokerStation,
        string brokerSeed)
    {
        var mission = new Mission
        {
            name            = block.Name,
            description     = block.Description,
            completionText  = block.CompletionText,
            sourceFaction   = Faction.Get(block.SourceFaction),
            sourcePoi       = brokerStation,
            turnIn          = brokerStation,
            trackedOnHud    = true,
            difficulty      = MissionDifficulty.Story,
            iconName        = "Combat",
            canBeIdled      = false,
            dynamicLevel    = true,
            storyId         = BuildStoryId(brokerStation, brokerSeed),
        };

        foreach (var stepBlock in block.Steps)
        {
            var step = new MissionStep();
            foreach (var objBlock in stepBlock.Objectives)
                step.objectives.Add(BuildObjective(objBlock));
            mission.steps.Add(step);
        }

        foreach (var rewardBlock in block.Rewards)
            mission.rewards.Add(BuildReward(rewardBlock, playerLevel));

        return mission;
    }

    /// <summary>Deterministic-per-broker but always-unique storyId:
    /// <c>vganima_llm_{station.guid}_{brokerSeed}_{guid}</c>. The trailing
    /// Guid.NewGuid avoids collisions across re-rolls. Station may be null
    /// in tests.</summary>
    private static string BuildStoryId(SpaceStation? station, string brokerSeed)
    {
        var stationPart = station?.guid ?? "nullstation";
        var tail        = Guid.NewGuid().ToString("N");
        return $"vganima_llm_{stationPart}_{brokerSeed}_{tail}";
    }

    private static MissionObjective BuildObjective(LlmObjective block)
    {
        switch (block)
        {
            case LlmKillEnemies k:
                return new KillEnemies
                {
                    enemyFaction   = Faction.Get(k.EnemyFaction),
                    requiredAmount = k.RequiredAmount,
                    // KillEnemies has no `description` field — vanilla composes
                    // statusText from a translation key. We surface the LLM's
                    // description at mission.description level instead (already
                    // copied above). Drop k.Description here intentionally.
                };

            case LlmProtectUnit p:
                return new ProtectUnit
                {
                    protectText    = p.ProtectText,
                    requiredAmount = 1,  // ProtectUnit inherits from TriggerObjective;
                                         // default requiredAmount is 1.
                };

            case LlmTriggerObjective t:
                return new TriggerObjective
                {
                    trigger        = Enum.Parse<MissionTrigger>(t.Trigger),
                    requiredAmount = t.RequiredAmount,
                    description    = t.Description,
                };

            case LlmCollectItemTypes c:
                return new CollectItemTypes
                {
                    itemCategory   = Enum.Parse<ItemCategory>(c.ItemCategory),
                    requiredAmount = c.RequiredAmount,
                };

            default:
                throw new InvalidOperationException(
                    $"unknown validated objective type {block.GetType().Name}");
        }
    }

    private static MissionReward BuildReward(LlmReward block, int playerLevel)
    {
        switch (block)
        {
            case LlmCreditsReward c:
                return new Credits
                {
                    amount = GameMath.GetCreditsValue(c.BaseValue, playerLevel),
                };

            case LlmExperienceReward e:
                return new Experience
                {
                    amount = GameMath.GetExperienceRewardValue(e.BaseValue, playerLevel),
                };

            case LlmReputationReward r:
                return new Reputation
                {
                    faction = Faction.Get(r.Faction),
                    amount  = r.Amount,
                };

            default:
                throw new InvalidOperationException(
                    $"unknown validated reward type {block.GetType().Name}");
        }
    }

    // Suppress the "unused alias" warning — the using is kept to document
    // the intent that this class DOES NOT call StoryMission.Add itself; the
    // caller wires the factory into the registry.
    private static readonly Type _keepRegistryAlias = typeof(StoryMissionRegistry);
}
```

- [ ] **Step 4: Run tests to verify they pass**

Run:
```
make test
```

Expected: all `MissionFactoryFromJsonTests` pass except ones requiring `GamePlayer.current.commander.level` (the Experience-reward path — not exercised in these tests). Credits / Reputation / objectives all build cleanly.

- [ ] **Step 5: Commit**

```bash
git add VGAnima/Missions/MissionFactoryFromJson.cs \
  VGAnima.Tests/Missions/MissionFactoryFromJsonTests.cs
git -c user.name="Florian Kinder" -c user.email="florian.kinder@fankserver.com" commit -m "$(cat <<'EOF'
feat: MissionFactoryFromJson for v2-mission

Translates a validated LlmMissionBlock into a live Mission per spec §6.
Per-type objective mappers (KillEnemies, ProtectUnit, TriggerObjective,
CollectItemTypes) and reward mappers (Credits, Experience, Reputation).
Resolves factions via Faction.Get and item categories via Enum.Parse.
StoryId format: vganima_llm_{station.guid}_{seed}_{guid} — unique per
call.

Tests cover field-by-field copy of top-level fields, per-objective
construction, multi-step ordering, credits scaling via GameMath, and
storyId uniqueness. Experience scaling NRE's without GamePlayer.current
and is left to manual E2E.
EOF
)"
```

---

## Task 5: `LlmMissionAssigner` + `ConversionRecord` update

**Files:**
- Modify: `VGAnima/Cache/ConversionRecord.cs`
- Create: `VGAnima/Missions/LlmMissionAssigner.cs`
- Create: `VGAnima.Tests/Missions/LlmMissionAssignerTests.cs`
- Modify: `VGAnima/Missions/TestMissionAssigner.cs` — add `[Obsolete]`
- Modify: `VGAnima/Missions/TestStoryMissions.cs` — add `[Obsolete]`

The new `LlmMissionAssigner` has a fundamentally different role from the old `TestMissionAssigner`: it is NOT called in the pre-LLM pre-flight path. The pre-flight now knows nothing about the specific mission; it only decides "yes, call the LLM for this broker." The assigner runs AFTER the LLM response validates, takes the `LlmMissionBlock` + broker station + player level, invokes `MissionFactoryFromJson.Build`, registers the resulting Mission via `StoryMission.Add(new StoryMission(storyId, _ => mission, ...))`, and returns the storyId.

So `LlmMissionAssigner` doesn't implement `IMissionAssigner` — that interface's contract (pre-flight decision based on bar state) no longer applies. The pre-flight step in `BarRefreshPatches` (Task 6) drops the old `Assigner.Assign` call entirely, substituting a boolean "LLM is available" check.

`ConversionRecord` gains a `StoryId` that's populated AFTER the LLM call completes (previously it was populated pre-flight). Current code already passes `storyId` into the ctor; we keep that shape but the caller's value comes from the assigner's post-LLM call.

`TestMissionAssigner` and `TestStoryMissions` stay in the codebase to support legacy saves (spec §12 recommendation 1) — both marked `[Obsolete]`.

- [ ] **Step 1: Update `ConversionRecord.cs`**

Replace `VGAnima/Cache/ConversionRecord.cs` contents:

```csharp
using System.Collections.Generic;
using Source.Galaxy.POI;
using VGAnima.Llm;

namespace VGAnima.Cache;

/// <summary>Broker-level bookkeeping for one converted Salesman:
///   <list type="bullet">
///     <item>Warmed TTS lines for every pitch variant across all broker states
///       — dropped from VGTTS cache when the broker departs.</item>
///     <item>The source <see cref="SpaceStation"/> the broker was injected at
///       (used for rolloff eviction, departure cleanup, and broker identification
///       via seed prefix).</item>
///     <item>The unique <c>storyId</c> for this broker's LLM-authored mission.
///       Minted by <see cref="VGAnima.Missions.LlmMissionAssigner"/> after the
///       LLM call validates and the Mission is built+registered; never rewritten.
///       Legacy v1 records (with fixed <c>vganima_test_jobsite_survey</c>) still
///       load via <see cref="VGAnima.Missions.TestStoryMissions"/>.</item>
///     <item>The validated <see cref="LlmStory"/> — dialogue + optional
///       <see cref="LlmMissionBlock"/>. For v2-mission responses the Mission
///       block is already materialized as a live registered Mission; the block
///       is kept here only for diagnostics / future save serialization.</item>
///   </list>
/// Not persisted — derived on bar open from the seed prefix; LlmStory dies
/// with the record (eviction or departure) and is re-synthesised on the next
/// injection of the same seed.</summary>
internal sealed class ConversionRecord
{
    public IReadOnlyList<(string Speaker, string Text)> WarmedLines { get; }
    public SpaceStation Station { get; }
    public string StoryId { get; }
    public LlmStory? LlmStory { get; }

    public ConversionRecord(
        IReadOnlyList<(string Speaker, string Text)> warmedLines,
        SpaceStation station,
        string storyId,
        LlmStory? llmStory = null)
    {
        WarmedLines = warmedLines;
        Station = station;
        StoryId = storyId;
        LlmStory = llmStory;
    }
}
```

(Shape is unchanged — we're bumping the xml doc to reflect v2 meaning.)

- [ ] **Step 2: Write failing tests**

Create `VGAnima.Tests/Missions/LlmMissionAssignerTests.cs`:

```csharp
using System.Collections.Generic;
using Source.MissionSystem;
using VGAnima.Llm;
using VGAnima.Missions;
using Xunit;

namespace VGAnima.Tests.Missions;

public class LlmMissionAssignerTests
{
    private static LlmMissionBlock Block() => new(
        Name: "Test", Description: "d", CompletionText: "c", SourceFaction: "TradingGuild",
        Steps: new[]
        {
            new LlmMissionStep(new LlmObjective[]
            {
                new LlmTriggerObjective("DockedWithSpaceStation", 1, "Dock."),
            }),
        },
        Rewards: new LlmReward[] { new LlmCreditsReward(50) });

    [Fact]
    public void Assign_ReturnsStoryIdWithPluginPrefix()
    {
        var registrations = new Dictionary<string, StoryMission>();
        var assigner = new LlmMissionAssigner(
            register: sm => registrations[sm.identifier] = sm);

        var storyId = assigner.Assign(
            Block(), playerLevel: 5, brokerStation: null, brokerSeed: "broker-abc");

        Assert.StartsWith("vganima_llm_", storyId);
        Assert.Contains("broker-abc", storyId);
    }

    [Fact]
    public void Assign_RegistersFactoryUnderTheReturnedStoryId()
    {
        var registrations = new Dictionary<string, StoryMission>();
        var assigner = new LlmMissionAssigner(
            register: sm => registrations[sm.identifier] = sm);

        var storyId = assigner.Assign(Block(), 5, null, "broker-abc");

        Assert.True(registrations.ContainsKey(storyId),
            $"Expected {storyId} registered, got [{string.Join(",", registrations.Keys)}]");
    }

    [Fact]
    public void Assign_TwoCallsSameSeed_DifferentStoryIds()
    {
        var registrations = new Dictionary<string, StoryMission>();
        var assigner = new LlmMissionAssigner(
            register: sm => registrations[sm.identifier] = sm);

        var a = assigner.Assign(Block(), 5, null, "broker-abc");
        var b = assigner.Assign(Block(), 5, null, "broker-abc");

        Assert.NotEqual(a, b);
        Assert.Equal(2, registrations.Count);
    }
}
```

- [ ] **Step 3: Run tests to verify they fail**

Run:
```
make test
```

Expected: compile error — `LlmMissionAssigner` doesn't exist.

- [ ] **Step 4: Create `LlmMissionAssigner.cs`**

Create `VGAnima/Missions/LlmMissionAssigner.cs`:

```csharp
using System;
using Source.Galaxy.POI;
using Source.MissionSystem;
using VGAnima.Llm;
// Alias to avoid confusion with Source.MissionSystem.Rewards.StoryMission.
using StoryMissionRegistry = Source.MissionSystem.StoryMission;

namespace VGAnima.Missions;

/// <summary>Builds + registers an LLM-authored mission at broker-injection time.
/// Unlike <see cref="VanillaSideMissionAssigner"/> / the legacy
/// <c>TestMissionAssigner</c>, this runs AFTER the LLM response validates —
/// the pre-flight path no longer decides which storyId the broker will offer.
///
/// Spec §7: calls <see cref="MissionFactoryFromJson.Build"/> to materialize
/// the Mission, wraps it in a <see cref="StoryMissionRegistry"/> whose
/// factory delegate returns the pre-built instance, and registers via
/// <c>StoryMission.Add</c>. Returns the minted storyId.
///
/// Save/load caveat (spec §7): registered factories live in the vanilla
/// <c>StoryMission.allMissions</c> dict for the session's remainder. A save
/// mid-mission reloaded in the same session will rehydrate; across sessions
/// the factory is gone → <see cref="System.Collections.Generic.KeyNotFoundException"/>.
/// Documented limitation; v1.1 adds persistence.</summary>
internal sealed class LlmMissionAssigner
{
    private readonly Action<StoryMissionRegistry> _register;

    /// <summary>Production ctor — registers into the real vanilla registry
    /// via <c>StoryMission.Add</c>.</summary>
    public LlmMissionAssigner()
        : this(StoryMissionRegistry.Add)
    { }

    /// <summary>Test ctor — injects the registration action so tests can
    /// observe without touching the real Unity-bound static dict.</summary>
    public LlmMissionAssigner(Action<StoryMissionRegistry> register)
    {
        _register = register;
    }

    public string Assign(
        LlmMissionBlock block,
        int playerLevel,
        SpaceStation? brokerStation,
        string brokerSeed)
    {
        var mission = MissionFactoryFromJson.Build(
            block, playerLevel, brokerStation, brokerSeed);

        var storyId = mission.storyId;
        var entry = new StoryMissionRegistry(
            storyId,
            _ => mission,           // factory always returns the pre-built instance
            available: null,        // always available
            pickupHint: "VGAnima Broker");

        _register(entry);
        return storyId;
    }
}
```

- [ ] **Step 5: Mark legacy `TestMissionAssigner` `[Obsolete]`**

Replace `VGAnima/Missions/TestMissionAssigner.cs` contents:

```csharp
using System;
using System.Collections.Generic;
using VGAnima.Patches;

namespace VGAnima.Missions;

/// <summary>Legacy assigner from v1. Retained for save-load tolerance:
/// if a player has an in-flight save pinning the old
/// <see cref="TestStoryMissions.JobsiteSurveyId"/> storyId,
/// <see cref="TestStoryMissions.Register"/> keeps the factory in the vanilla
/// registry so the save doesn't <see cref="KeyNotFoundException"/>-crash on
/// load. That registration is still wired in <c>Plugin.Awake</c>.
///
/// <para>The v2-mission pipeline does NOT call this assigner — broker
/// injection now uses <see cref="LlmMissionAssigner"/> post-LLM instead of a
/// pre-flight storyId decision. Remove this type (and
/// <see cref="TestStoryMissions"/>) once we're confident nobody has stale
/// saves referencing the legacy storyId.</para></summary>
[Obsolete("retained for legacy save compatibility; v2-mission uses LlmMissionAssigner")]
internal sealed class TestMissionAssigner : IMissionAssigner
{
    public string? Assign(string salesmanSeed,
                          ISet<string> alreadyAssignedInThisBar,
                          IGamePlayerView player)
    {
        var id = TestStoryMissions.JobsiteSurveyId;

        if (player.IsArchived(id)) return null;
        if (alreadyAssignedInThisBar.Contains(id)) return null;

        return id;
    }
}
```

- [ ] **Step 6: Mark legacy `TestStoryMissions` `[Obsolete]`**

Replace `VGAnima/Missions/TestStoryMissions.cs` contents:

```csharp
using System;
using Source.Galaxy;
using Source.Galaxy.POI;
using Source.MissionSystem;
using Source.MissionSystem.Objectives;
using Source.MissionSystem.Rewards;
using Source.Player;
using Source.Util;
// Registry and reward both claim the short name "StoryMission" —
// alias the registry so new StoryMission(...) stays unambiguous.
using StoryMissionRegistry = Source.MissionSystem.StoryMission;

namespace VGAnima.Missions;

/// <summary>Legacy v1 plugin-defined <see cref="StoryMission"/> registration.
/// Retained for save-load tolerance only: in-flight saves pinning the
/// <see cref="JobsiteSurveyId"/> need the factory in the registry so
/// <see cref="Mission.FromJson(string)"/> doesn't
/// <see cref="System.Collections.Generic.KeyNotFoundException"/>.
///
/// <para>v2-mission does NOT pitch this mission to new brokers — injection
/// uses <see cref="LlmMissionAssigner"/> with per-broker storyIds. Remove
/// this type (and <see cref="TestMissionAssigner"/>) once the stale-save
/// window has passed.</para></summary>
[Obsolete("retained for legacy save compatibility; v2-mission authors missions per broker via LlmMissionAssigner")]
internal static class TestStoryMissions
{
    public const string JobsiteSurveyId = "vganima_test_jobsite_survey";

    private static bool _registered;

    /// <summary>Idempotent. Called from <c>Plugin.Awake</c> so legacy saves
    /// load cleanly.</summary>
    public static void Register()
    {
        if (_registered) return;
        _registered = true;

        StoryMissionRegistry.Add(new StoryMissionRegistry(
            JobsiteSurveyId,
            CreateJobsiteSurvey,
            available: null,
            pickupHint: "VGAnima Broker"));
    }

    private static Mission CreateJobsiteSurvey(GamePlayer player)
    {
        var sourcePoi = MapPointOfInterest.current;

        var mission = new Mission
        {
            name            = "Jobsite Survey",
            description     = "A local broker wants a quick survey of another docking facility. Undock and dock at any space station - they don't care which, they just want the logbook entry.",
            completionText  = "Survey logged. Easy credits, come back anytime.",
            sourcePoi       = sourcePoi,
            turnIn          = sourcePoi,
            sourceFaction   = Faction.tradingGuild,
            trackedOnHud    = true,
            difficulty      = MissionDifficulty.Story,
            iconName        = "Combat",
            canBeIdled      = false,
            dynamicLevel    = true,
        };

        var step = new MissionStep();
        step.objectives.Add(new TriggerObjective
        {
            requiredAmount = 1,
            trigger        = MissionTrigger.DockedWithSpaceStation,
            description    = "Dock at any space station",
        });
        mission.steps.Add(step);

        mission.rewards.Add(new Credits
        {
            amount = GameMath.GetCreditsValue(50f, player.level),
        });
        mission.rewards.Add(new Experience
        {
            amount = GameMath.GetExperienceRewardValue(30f, player.level),
        });

        return mission;
    }
}
```

- [ ] **Step 7: Suppress `[Obsolete]` warnings at the one call site that still uses them**

`Plugin.cs` still calls `TestStoryMissions.Register()` (intentional, legacy safety net) — the call site must wrap the call in a pragma so the build stays clean. Task 6 handles this as part of the Plugin.cs rewrite. For now, building after Steps 1-6 will emit `CS0618` warnings at the call site — expected, will be addressed in Task 6.

- [ ] **Step 8: Run tests to verify they pass**

Run:
```
make test
```

Expected: `LlmMissionAssignerTests` (3 facts) pass. `VanillaSideMissionAssignerTests` still pass. Existing tests all pass. Build emits `CS0618` warnings pointing at `Plugin.cs` — expected, addressed in Task 6.

- [ ] **Step 9: Commit**

```bash
git add VGAnima/Cache/ConversionRecord.cs \
  VGAnima/Missions/LlmMissionAssigner.cs VGAnima/Missions/TestMissionAssigner.cs \
  VGAnima/Missions/TestStoryMissions.cs \
  VGAnima.Tests/Missions/LlmMissionAssignerTests.cs
git -c user.name="Florian Kinder" -c user.email="florian.kinder@fankserver.com" commit -m "$(cat <<'EOF'
feat: LlmMissionAssigner for per-broker runtime registration

New assigner runs AFTER the LLM call validates: builds a Mission via
MissionFactoryFromJson, wraps in a StoryMission factory delegate, and
registers into vanilla's StoryMission.allMissions dict. Returns the
minted storyId.

TestMissionAssigner and TestStoryMissions marked [Obsolete] but kept
in the build so in-flight saves with vganima_test_jobsite_survey still
rehydrate via TestStoryMissions.Register() in Plugin.Awake (spec §12
recommendation 1).

ConversionRecord docs updated; shape unchanged.
EOF
)"
```

---

## Task 6: v2 prompt + `BarRefreshPatches` integration + `Plugin.cs` wiring

**Files:**
- Modify: `VGAnima/Patches/BarRefreshPatches.cs`
- Modify: `VGAnima/Plugin.cs`

This ties everything together:

1. `BuildSystemPrompt` rewrites to describe the v2-mission schema (spec §8).
2. `FinalizeBrokerInjection` (main-thread continuation):
   - If `story.Mission != null`: call `LlmMissionAssigner.Assign(block, playerLevel, station, seed)`, get the minted storyId, stash it in the `ConversionRecord`.
   - Else (legacy v1 response or LLM returned old schema): fall back to `TestStoryMissions.JobsiteSurveyId` — broker still works for early-testing saves.
3. Pre-flight drops the old `Assigner.Assign` call — no longer knows a storyId pre-LLM. The "is some mission available for this player" gate disappears. (The v2 LLM decides what mission to pitch; the pre-flight just decides "call the LLM or not", which reduces to "is LLM enabled? yes → call".)
4. `Plugin.cs` swaps the `Assigner` field type to `LlmMissionAssigner` and constructs it. `TestStoryMissions.Register()` stays, wrapped in `#pragma warning disable CS0618` to suppress the obsoletion warning at the call site.
5. Rehydrate path (RegistryRehydratePatches) — the legacy branch's `Assigner.Assign(seed, alreadyAssigned, PlayerView)` call is replaced with a simple "broker has no mission yet" stub returning the legacy test-mission id, so rehydrated brokers without a fresh v2 LLM call still work. This is a tolerated regression — v2 brokers that saved mid-session across restarts will pitch the stale test mission. Documented in spec §7.

Also extends `ContextGatherer`'s consumer to thread `atWar` + `reputation` through to `ResponseValidator.Parse` so the enemy_faction hostility check uses live context.

The prompt is empirical — the first version follows spec §8 verbatim; tune once we see real outputs.

- [ ] **Step 1: Update `BuildSystemPrompt` + `BuildUserPrompt` + dispatch flow in `BarRefreshPatches.cs`**

Apply the following **targeted edits** to `VGAnima/Patches/BarRefreshPatches.cs` (no full-file rewrite — existing logic is 700+ lines and most is orthogonal).

6.1. Replace the body of `BuildSystemPrompt()`:

```csharp
    private static string BuildSystemPrompt()
    {
        // v2-mission system prompt per spec §8.
        return
            "You are a writer for bar-broker NPCs in a space-trading game. Your job is to\n" +
            "produce ONE dialogue-and-mission JSON object the broker will offer the player.\n\n" +
            "You ONLY output valid JSON matching this schema - no preamble, no markdown fences:\n\n" +
            "{\n" +
            "  \"schema\": \"vganima/mission/v1\",\n" +
            "  \"pitch\":    [ /* 3..5 short in-character lines pitching the job */ ],\n" +
            "  \"check_in\": [ /* 1..2 lines for when the captain returns mid-job */ ],\n" +
            "  \"payout\":   [ /* 2..4 lines for when the captain turns the job in */ ],\n" +
            "  \"mission\": {\n" +
            "    \"name\":            /* <=60 chars */,\n" +
            "    \"description\":     /* <=500 chars */,\n" +
            "    \"completion_text\": /* <=200 chars */,\n" +
            "    \"source_faction\":  /* one of: Marauders PoliceGuild BountyGuild\n" +
            "                          TradingGuild MiningGuild IndustrialGuild SalvageGuild\n" +
            "                          Stranded MercenaryGuild Smugglers Darkspacers Puppeteers\n" +
            "                          Fanatics HolyRadicals Amalgam Gold */,\n" +
            "    \"steps\": [ /* 1..3 steps, each with 1..2 objectives */\n" +
            "      { \"objectives\": [ /* objective objects */ ] } ],\n" +
            "    \"rewards\": [ /* 1..5 reward objects */ ]\n" +
            "  }\n" +
            "}\n\n" +
            "OBJECTIVE TYPES (each objective object has a `type` plus fields):\n" +
            "  { \"type\": \"KillEnemies\",\n" +
            "    \"enemy_faction\":   <faction from list above>,\n" +
            "    \"required_amount\": 1..5,\n" +
            "    \"description\":     <<=80 chars> }\n" +
            "  { \"type\": \"ProtectUnit\",\n" +
            "    \"protect_text\":    <<=80 chars> }\n" +
            "  { \"type\": \"TriggerObjective\",\n" +
            "    \"trigger\":         one of [DockedWithSpaceStation, ArrivedAtSpaceStation, MoveToArea],\n" +
            "    \"required_amount\": 1..3,\n" +
            "    \"description\":     <<=80 chars> }\n" +
            "  { \"type\": \"CollectItemTypes\",\n" +
            "    \"item_category\":   one of [Ore, Salvage, RefinedProduct, TradeGoods, Junk],\n" +
            "    \"required_amount\": 1..50,\n" +
            "    \"description\":     <<=80 chars> }\n\n" +
            "REWARD TYPES:\n" +
            "  { \"type\": \"Credits\",    \"base_value\": 10..200 }\n" +
            "  { \"type\": \"Experience\", \"base_value\": 10..150 }\n" +
            "  { \"type\": \"Reputation\", \"faction\": <faction>, \"amount\": -500..500 }\n\n" +
            "RULES FOR EVERY DIALOGUE LINE:\n" +
            "- ASCII only (no em-dashes, smart quotes, or emoji; hyphens and straight apostrophes OK)\n" +
            "- Maximum 120 characters\n" +
            "- Non-empty, no leading/trailing whitespace\n" +
            "- In character for the broker; reference the player's state or the location when it fits\n\n" +
            "COHERENCE RULES:\n" +
            "- Dialogue and mission must match: if the pitch promises a rescue, include ProtectUnit\n" +
            "  or KillEnemies, not a lone CollectItemTypes.\n" +
            "- source_faction is the hiring broker's faction. enemy_faction must be hostile or\n" +
            "  neutral to the player (check reputation / at_war in the player context).\n" +
            "- At least one objective across all steps must NOT be ProtectUnit (a mission of pure\n" +
            "  protect is degenerate).\n" +
            "- Scale to the player: high level = tougher enemies or higher amounts; low credits\n" +
            "  player = larger reward ratios.\n\n" +
            "Reply with ONLY the JSON object.";
    }
```

6.2. Replace `BuildUserPrompt()`:

```csharp
    private static string BuildUserPrompt(string contextJson, BrokerInfo brokerInfo, SpaceStation station)
    {
        var gender = brokerInfo.IsMale ? "male" : "female";
        return
            "Player and world context:\n" +
            contextJson + "\n\n" +
            $"Broker to voice: {brokerInfo.Name}, {gender}, at {station.name}, " +
            $"aligned with {brokerInfo.StationFaction}.\n\n" +
            "Produce one JSON object matching the vganima/mission/v1 schema.";
    }
```

6.3. Replace `StartInjectMissionBroker` pre-flight assigner block — the current code uses `plugin.Assigner.Assign(candidateSeed, alreadyAssigned, plugin.PlayerView)` to mint a storyId. v2 drops that. **Remove** the 8-line block from `// Ask the assigner for a storyId.` through `if (storyId == null) { ... return; }`, and **remove** the `candidateSeed` field from the subsequent context/LLM dispatch. The minimal replacement:

```csharp
        // v2-mission: no pre-flight storyId decision. The LLM authors the
        // mission, and LlmMissionAssigner registers it after validation.
        var storyId = string.Empty;  // placeholder; populated post-LLM
```

Keep `alreadyAssigned` logic intact — it's still used for logging / diagnostics (and the orchestrator can still bail if the player's mission cap is full; currently that's a separate concern).

6.4. Thread `atWar` and `reputation` into `ResponseValidator.Parse`. Replace the `story = plugin.Validator.Parse(rawContent);` line in `DispatchAsync` with:

```csharp
        LlmStory story;
        try
        {
            // Re-gather the hostility context so the validator can reject
            // kill-ally missions. Uses the view directly (no cached copy)
            // so the check races against actual current state — acceptable
            // because reputation changes slowly and the cost is < 1ms.
            IReadOnlyList<string>? atWar = null;
            IReadOnlyDictionary<string, int>? reputation = null;
            try
            {
                atWar      = plugin.GameStateView.AtWar;
                reputation = plugin.GameStateView.Reputation;
            }
            catch (Exception gatherEx)
            {
                Plugin.Log.LogWarning(
                    $"[vganima] hostility context gather failed: {gatherEx.Message}; " +
                    $"proceeding without enemy_faction cross-check");
            }

            story = plugin.Validator.Parse(rawContent, atWar, reputation);
        }
        catch (LlmValidationException ex)
        {
            var preview = rawContent.Length > 500 ? rawContent.Substring(0, 500) : rawContent;
            Plugin.Log.LogInfo(
                $"[vganima] LLM response failed validation: {ex.Message}; " +
                $"skipping broker at '{station.name}'. First 500 chars: {preview}");
            return;
        }
        catch (Exception ex)
        {
            Plugin.Log.LogError(
                $"[vganima] Unexpected validation failure; skipping broker at '{station.name}': {ex}");
            return;
        }
```

6.5. Replace the final `plugin.Scheduler.Enqueue(() => FinalizeBrokerInjection(...))` line to drop the now-empty `storyId` parameter (it's computed inside the continuation instead):

```csharp
        plugin.Scheduler.Enqueue(() => FinalizeBrokerInjection(
            plugin, bar, station, newPatron, candidateSeed, story));
```

6.6. Replace `FinalizeBrokerInjection`'s signature and body (the existing one takes a `storyId` arg we no longer have pre-flight):

```csharp
    private static void FinalizeBrokerInjection(
        Plugin plugin, Bar bar, SpaceStation station, Salesman newPatron,
        string candidateSeed, LlmStory story)
    {
        try
        {
            if (SpaceStation.current != station)
            {
                Plugin.Log.LogDebug(
                    $"[vganima] Player left '{station.name}' before LLM returned; dropping broker");
                return;
            }
            if (bar.availablePatrons.Contains(newPatron))
            {
                Plugin.Log.LogDebug("[vganima] Broker already added (race); skipping");
                return;
            }

            // Build + register the mission if the LLM returned a v2 mission block.
            // Legacy fallback: if story.Mission is null (LLM returned the old
            // dialogue-only schema), pitch the legacy TestStoryMissions mission
            // so early-testing saves still work.
            string storyId;
            if (story.Mission != null)
            {
                try
                {
                    var playerLevel = plugin.GameStateView.PlayerLevel;
                    storyId = plugin.MissionAssigner.Assign(
                        story.Mission, playerLevel, station, candidateSeed);
                    Plugin.Log.LogInfo(
                        $"[vganima] LLM-authored mission '{story.Mission.Name}' registered " +
                        $"with storyId={storyId}");
                }
                catch (Exception ex)
                {
                    Plugin.Log.LogError(
                        $"[vganima] MissionFactoryFromJson threw for '{story.Mission.Name}'; " +
                        $"skipping broker at '{station.name}': {ex}");
                    return;
                }
            }
            else
            {
                storyId = TestStoryMissions.JobsiteSurveyId;
                Plugin.Log.LogWarning(
                    $"[vganima] LLM returned v1 dialogue-only schema; falling back to legacy " +
                    $"'{storyId}' for broker at '{station.name}'");
            }

            // Build dialogueLines from the pitch block so VGTTS's BarPatron.Initialize
            // postfix has text to warm.
            var dialogueLines = new List<DialogueLine>(story.Pitch.Count);
            foreach (var text in story.Pitch)
            {
                if (string.IsNullOrWhiteSpace(text)) continue;
                var character = new Character(newPatron.name).WithPortret(newPatron.icon);
                dialogueLines.Add(DialogueLine.cDL(character, text));
            }
            Traverse.Create(newPatron).Field<List<DialogueLine>>("dialogueLines").Value = dialogueLines;

            // Voice + warm all lines across states.
            var voice = newPatron.isMale ? ProceduralMaleVoice : ProceduralFemaleVoice;
            plugin.Vgtts.RegisterVoice(newPatron.name, voice);
            var warmedPairs = new List<(string Speaker, string Text)>();
            foreach (var line in story.Pitch)   if (!string.IsNullOrWhiteSpace(line)) warmedPairs.Add((newPatron.name, line));
            foreach (var line in story.CheckIn) if (!string.IsNullOrWhiteSpace(line)) warmedPairs.Add((newPatron.name, line));
            foreach (var line in story.Payout)  if (!string.IsNullOrWhiteSpace(line)) warmedPairs.Add((newPatron.name, line));
            _ = Task.Run(async () =>
            {
                foreach (var (speaker, text) in warmedPairs)
                {
                    try { await plugin.Vgtts.WarmCacheAsync(speaker, text, CancellationToken.None); }
                    catch { /* best-effort; live TTS warms again on dialogue open */ }
                }
            });

            plugin.Registry.Register(newPatron, new ConversionRecord(warmedPairs, station, storyId, story));
            bar.availablePatrons.Add(newPatron);

            Plugin.Log.LogInfo(
                $"[vganima] Added LLM-authored broker '{newPatron.name}' to bar at '{station.name}' " +
                $"(seat {newPatron.seat}, isMale={newPatron.isMale}, storyId={storyId}, " +
                $"{bar.availablePatrons.Count} patrons total)");

            var barUI = UObject.FindAnyObjectByType<BarUI>();
            barUI?.RefreshPatrons();
        }
        catch (Exception ex)
        {
            Plugin.Log.LogError($"[vganima] FinalizeBrokerInjection threw: {ex}");
        }
    }
```

6.7. In `RegistryRehydratePatches`, replace the `plugin.Assigner.Assign(seed, alreadyAssigned, plugin.PlayerView)` call (around line 526 in the current file) with a direct fallback to the legacy id. Rehydrated brokers from older saves serve the legacy mission — a known v1 carry-over that survives for save compatibility. Replace the block starting with `var storyId = plugin.Assigner.Assign(...)` up to the next `if (storyId == null)` check with:

```csharp
                // Rehydrated brokers from legacy saves offered a fixed storyId.
                // v2-mission doesn't persist the LLM-authored storyId across sessions
                // (spec §7 known limitation) so we fall back to the legacy factory.
                var storyId = TestStoryMissions.JobsiteSurveyId;
                if (plugin.PlayerView.IsArchived(storyId))
                {
                    Plugin.Log.LogWarning(
                        $"[vganima] Rehydrate: legacy storyId {storyId} is archived; " +
                        $"leaving broker '{patron.name}' unregistered");
                    continue;
                }
```

The rehydrate's LLM call + `FinalizeRehydrate` stay unchanged (they already accept a `LlmStory` and pass the storyId through).

6.8. Update `BuildRehydrateSystemPrompt` + `BuildRehydrateUserPrompt` to match the new v2 prompt — duplicate the same body from 6.1 / 6.2. (Alternative: extract to a shared module. Not in scope for this slice per the existing comment; accept the 80-line duplication.)

- [ ] **Step 2: Update `Plugin.cs`**

Replace `VGAnima/Plugin.cs` contents:

```csharp
using System;
using System.Linq;
using BepInEx;
using BepInEx.Logging;
using HarmonyLib;
using Source.Galaxy.POI.Station;
using VGAnima.Cache;
using VGAnima.Config;
using VGAnima.Llm;
using VGAnima.Missions;
using VGAnima.Patches;
using VGAnima.Pitch;
using VGAnima.Tts;
using VGAnima.Unity;

namespace VGAnima;

[BepInPlugin(PluginGuid, PluginName, PluginVersion)]
[BepInProcess("VanguardGalaxy.exe")]
[BepInDependency("vgtts", BepInDependency.DependencyFlags.SoftDependency)]
public class Plugin : BaseUnityPlugin
{
    public const string PluginGuid = "vganima";
    public const string PluginName = "Vanguard Galaxy Anima";
    public const string PluginVersion = "0.2.0";

    internal static Plugin Instance { get; private set; } = null!;
    internal static ManualLogSource Log { get; private set; } = null!;

    internal AnimaConfig Cfg { get; private set; } = null!;

    /// <summary>v2-mission: post-LLM mission builder + registrar. Replaces
    /// the v1 <see cref="IMissionAssigner"/> slot.</summary>
    internal LlmMissionAssigner MissionAssigner { get; private set; } = null!;

    internal IPitchProvider PitchProvider { get; private set; } = null!;
    internal IGamePlayerView PlayerView { get; private set; } = null!;
    internal VgttsBridge Vgtts { get; private set; } = null!;
    internal ConversionRegistry<BarPatron, ConversionRecord> Registry { get; private set; } = null!;

    internal ILlmClient? LlmClient { get; private set; }
    internal IGameStateView GameStateView { get; private set; } = null!;
    internal ContextGatherer Gatherer { get; private set; } = null!;
    internal ResponseValidator Validator { get; private set; } = null!;
    internal UnityMainThreadScheduler Scheduler { get; private set; } = null!;

    private Harmony _harmony = null!;

    private void Awake()
    {
        Instance = this;
        Log = Logger;

        Cfg = new AnimaConfig(Config);

        // Register the legacy TestStoryMissions factory so in-flight saves
        // with the v1 storyId `vganima_test_jobsite_survey` rehydrate
        // cleanly (spec §12 recommendation 1). The factory is marked
        // [Obsolete]; suppression is local so the rest of the build stays
        // warning-clean.
#pragma warning disable CS0618
        TestStoryMissions.Register();
#pragma warning restore CS0618

        MissionAssigner = new LlmMissionAssigner();
        PlayerView      = new GamePlayerView();
        Vgtts           = new VgttsBridge();
        Registry        = new ConversionRegistry<BarPatron, ConversionRecord>();

        GameStateView = new GameStateView();
        Gatherer      = new ContextGatherer();
        Validator     = new ResponseValidator();
        Scheduler     = gameObject.AddComponent<UnityMainThreadScheduler>();

        if (Cfg.LlmEnabled.Value && !string.IsNullOrEmpty(Cfg.LlmBaseUrl.Value))
        {
            LlmClient = new HttpLlmClient(
                baseUrl:        Cfg.LlmBaseUrl.Value,
                model:          Cfg.LlmModel.Value,
                apiKey:         Cfg.LlmApiKey.Value,
                enableThinking: Cfg.LlmEnableThinking.Value,
                maxTokens:      Cfg.LlmMaxTokens.Value,
                temperature:    Cfg.LlmTemperature.Value,
                timeout:        TimeSpan.FromSeconds(Cfg.LlmTimeoutSeconds.Value));
        }

        PitchProvider = new LlmPitchProvider(name =>
        {
            var rec = Registry.FindByValue(r => r.Station != null && name != null &&
                r.Station.bar != null &&
                r.Station.bar.availablePatrons.Exists(p => p.name == name));
            return rec?.LlmStory;
        });

        Log.LogInfo($"[vganima] VGTTS detected: {(Vgtts.IsAvailable ? "yes" : "no")}");
        Log.LogInfo($"[vganima] LLM enabled: {(LlmClient != null ? "yes" : "no")}  " +
                    $"Chance: {Cfg.MissionChance.Value}  " +
                    $"BaseUrl: {(string.IsNullOrEmpty(Cfg.LlmBaseUrl.Value) ? "(unset)" : Cfg.LlmBaseUrl.Value)}  " +
                    $"Model: {Cfg.LlmModel.Value}  " +
                    $"ApiKey: {RedactApiKey(Cfg.LlmApiKey.Value)}  " +
                    $"MaxTokens: {Cfg.LlmMaxTokens.Value}  " +
                    $"Temperature: {Cfg.LlmTemperature.Value}");

        _harmony = new Harmony(PluginGuid);
        _harmony.PatchAll(typeof(SalesmanPatches));
        _harmony.PatchAll(typeof(BarRefreshPatches));
        _harmony.PatchAll(typeof(RegistryRehydratePatches));
        _harmony.PatchAll(typeof(BarUIDebugPatches));
        _harmony.PatchAll(typeof(BarPatronImageDebugPatches));

        Log.LogInfo($"{PluginName} v{PluginVersion} loaded ({_harmony.GetPatchedMethods().Count()} patches)");
    }

    private void OnDestroy()
    {
        _harmony?.UnpatchSelf();
        if (LlmClient is IDisposable disposable) disposable.Dispose();
    }

    internal static string RedactApiKey(string? apiKey) =>
        string.IsNullOrEmpty(apiKey) ? "<empty>" : "<set>";
}
```

- [ ] **Step 3: Build + run tests**

Run:
```
make build && make test
```

Expected:
- Build succeeds (zero warnings — `CS0618` suppressed at the call site).
- All existing tests + new Task 1-5 tests pass.
- The legacy `VanillaSideMissionAssigner` is no longer referenced anywhere (`Plugin.Assigner` is gone); that file stays in the tree as dead code for now (deletion can ride a later tidy-up commit).

If tests fail due to `Plugin.Assigner` references in other test files or code (e.g. `RegistryRehydratePatches` accesses `plugin.Assigner`), fix the field name on that code path — search for `plugin.Assigner`, replace with `plugin.MissionAssigner` or remove depending on context per Step 6.7 above.

- [ ] **Step 4: Commit**

```bash
git add VGAnima/Patches/BarRefreshPatches.cs VGAnima/Plugin.cs
git -c user.name="Florian Kinder" -c user.email="florian.kinder@fankserver.com" commit -m "$(cat <<'EOF'
feat: wire v2-mission through BarRefreshPatches + Plugin

System/user prompts rewritten per spec §8 to describe the
vganima/mission/v1 schema, whitelists, and coherence rules.
FinalizeBrokerInjection now builds+registers an LLM-authored Mission
via LlmMissionAssigner when the response carries a mission block;
falls back to the legacy TestStoryMissions id when the LLM returns a
v1 dialogue-only schema.

Validator receives at_war + reputation context from GameStateView so
the enemy_faction hostility check operates on live state.

Plugin swaps the IMissionAssigner field for LlmMissionAssigner; the
legacy TestStoryMissions.Register() call stays for save-load tolerance,
wrapped in a local CS0618 suppression.

Rehydrate path temporarily falls back to the legacy storyId for
brokers whose v2 storyId didn't survive the session (known limitation
per spec §7, addressed in v1.1).
EOF
)"
```

---

## Task 7: Manual E2E verification

**Files:** none — deploy + observe.

Spec §14 checklist. All six scenarios performed against a live game install. No code changes — if any fail, open a follow-up fix PR.

- [ ] **Step 1: Deploy the plugin**

```
make deploy
```

Expected: `VGAnima.dll` (+ any copied runtime deps) land in `BepInEx/plugins/VGAnima/` under the game install.

- [ ] **Step 2: Scenario 1 — LLM disabled → no broker (regression)**

Edit `BepInEx/config/vganima.cfg`, set `[Llm] Enabled = false`. Launch the game, dock at any station with a bar, open the bar UI.

Expected: no injected broker. BepInEx log contains:
```
[Info   :Vanguard Galaxy Anima] [vganima] LLM enabled: no  ...
[Debug  :Vanguard Galaxy Anima] [vganima] LLM disabled; skipping broker injection
```

- [ ] **Step 3: Scenario 2 — Happy path**

Set `[Llm] Enabled = true` + valid `BaseUrl` / `Model`. Dock at a station with a bar. Wait for injection (2-3s).

Expected:
- A broker appears in the bar UI.
- Click the broker → dialogue plays (TTS if available, text otherwise). Dialogue lines reference the mission it's offering.
- Click "Accept" or dismiss → mission appears in the active-mission log with the LLM-authored name/description.
- Complete the mission's objectives in-game (dock somewhere, kill the hostile faction, etc.).
- Return to the broker → "ReadyToClaim" dialogue plays. Complete dialogue → credits/experience/reputation rewards apply. Broker departs. BarUI refreshes without the broker.

BepInEx log key markers:
```
[Info   :Vanguard Galaxy Anima] [vganima] LLM-authored mission '<name>' registered with storyId=vganima_llm_...
[Info   :Vanguard Galaxy Anima] [vganima] Added LLM-authored broker 'The Mission Broker' ...
```

- [ ] **Step 4: Scenario 3 — Multi-step mission**

Repeat Scenario 2 until the LLM produces a multi-step mission (may take several reloads — re-roll by closing + reopening the bar).

Expected: both steps tick correctly in order. After step 1 completes, the mission HUD advances to step 2's objective.

- [ ] **Step 5: Scenario 4 — Enemy faction hostility**

With a non-default rep snapshot (e.g. at war with Marauders, friendly to TradingGuild): trigger broker injections. If the LLM proposes `KillEnemies` of a faction the player is NOT hostile to, the validator rejects it and the broker is skipped — verify in log.

Expected log marker on rejection:
```
[Info   :Vanguard Galaxy Anima] [vganima] LLM response failed validation: field `mission.steps[...].objectives[...].enemy_faction` is friendly to player (rep=...); skipping broker
```

Happy case: enemy_faction matches at-war or rep<=0 → broker renders; player can execute the kill mission without rep-side-effects on a friend.

- [ ] **Step 6: Scenario 5 — Validator rejection path (synthetic)**

Temporarily point `[Llm] BaseUrl` at a local stub that returns deliberately-broken JSON (e.g. `{"schema":"vganima/mission/v1"}` with no dialogue arrays). Restart game, dock.

Expected: no broker. Log:
```
[Info   :Vanguard Galaxy Anima] [vganima] LLM response failed validation: missing field `pitch`; skipping broker at '...'. First 500 chars: ...
```

- [ ] **Step 7: Scenario 6 — Save/reload mid-session**

Accept a v2-authored mission. Save the game. Continue playing; the mission should still be in the active list. Quit to menu and re-load the same save within the same session.

Expected (in-session reload): mission rehydrates. StoryMission factory still in `StoryMission.allMissions` dict, so `Mission.FromJson(storyId)` finds it.

Known limitation (spec §7): quit the game entirely and re-launch, load the save → mission factory is gone → `KeyNotFoundException` on the vanilla load path. Documented, not fixed here. Verify the exception trace matches the documented one and that the rest of the save loads cleanly (crash is isolated to the one mission slot).

- [ ] **Step 8: Commit the (empty) verification marker if all six scenarios pass**

If E2E passes, there's no code to commit — the change set from Tasks 1-6 is already in place. Document the verification in your PR description or append a note commit:

```bash
git -c user.name="Florian Kinder" -c user.email="florian.kinder@fankserver.com" commit --allow-empty -m "$(cat <<'EOF'
chore: v2-mission E2E verification complete

All 6 scenarios from spec §14 verified against live game install:
  1. LLM disabled  → no broker (regression clean)
  2. Happy path    → broker + LLM mission + payout + departure
  3. Multi-step    → steps advance correctly
  4. Hostility     → friendly faction rejected by validator
  5. Malformed     → broker skipped, log marker present
  6. Save/reload   → in-session OK; cross-session KeyNotFoundException
                     (documented limitation per spec §7)
EOF
)"
```

---

## Self-review checklist (applied before publishing this plan)

- **No placeholders / TBDs / "similar to Task N" shortcuts.** Every code block is full-body.
- **Type consistency cross-checked.** `LlmMissionBlock` property names (`Name`, `Description`, `CompletionText`, `SourceFaction`, `Steps`, `Rewards`) are identical in Task 2 (definition), Task 3 (ResponseValidator storage), Task 4 (factory consumer), Task 5 (assigner consumer). Objective record names (`LlmKillEnemies`, `LlmProtectUnit`, `LlmTriggerObjective`, `LlmCollectItemTypes`) match in all five sites. Reward records (`LlmCreditsReward`, `LlmExperienceReward`, `LlmReputationReward`) likewise. `LlmMissionAssigner.Assign(block, playerLevel, brokerStation, brokerSeed)` signature identical in Task 5 definition and Task 6 call site.
- **netstandard2.1 hazards flagged.** Whitelists use `IReadOnlyCollection<string>` + a `Contains` method (no `IReadOnlySet<T>` — unavailable pre-net5). No `Enumerable.TakeLast` on `IReadOnlyList<T>` (no uses anyway).
- **xUnit visibility: test classes are public, nested helpers can be private.** `WhitelistsTests`, `MissionBlockValidatorTests`, `MissionFactoryFromJsonTests`, `LlmMissionAssignerTests` all `public class`. Inside, private record/class helpers are fine (pattern matches `ContextGathererTests.FakeGameStateView`).
- **Facts AND Theories are both public — no `private [Fact]`.** All test methods shown are public by default (no access modifier = internal on a nested member, but xUnit runs them on `public class`; the method-level access inherits `public` when omitted inside a public class per C# defaults — matches the existing `ResponseValidatorTests` style). Verified by eye.
- **Decomp-cross-checked.**
  - `Faction.Get(string)` confirmed in `Source.Galaxy/Faction.cs:232`.
  - `StoryMission.Add(StoryMission)` confirmed `Source.MissionSystem/StoryMission.cs:64`.
  - `StoryMission.Get(player, id)` sets `mission.storyId = id` after factory — setting it in factory is redundant but harmless (plan does set it for storage + broker diagnostics).
  - `Mission.storyId` is a public field (line 63).
  - `KillEnemies` has NO `description` field — plan calls this out explicitly and drops the LLM-supplied description at objective level (retained at mission level via `mission.description`).
  - `CollectItemTypes` takes `ItemCategory?`, not item-identifier list — plan reinterprets spec §3 from `item_types[]` to single `item_category`, shelving curated item-list to v1.1. Whitelist and prompt reflect this.
  - `MissionTrigger` enum has `MoveToArea`, NOT `TravelToPOI` — plan substitutes `MoveToArea`.
  - `ProtectUnit` inherits from `TriggerObjective` and has `protectText` field — plan sets `protectText` and `requiredAmount = 1` (ProtectUnit's triggeredBy is `MissionTrigger.UnitProtected`, requiredAmount default 1).
  - `Reputation.faction` is nullable-falling-back-to-sourceFaction on complete — plan requires non-null in JSON to avoid surprise rep fall-through.

## Unresolved questions (observe during implementation)

- **Prompt quality.** First-pass prompt follows spec §8 verbatim; tune after observing the first ~10 live LLM outputs. Likely candidates: move the OBJECTIVE TYPES / REWARD TYPES block above the schema skeleton (LLMs front-load detail better than back-load).
- **Schema case sensitivity.** Plan's validator rejects `kill_enemies` (lower-snake). Spec §16 accepts future case-normalization — not in this slice. If the LLM frequently violates, add `toLowerInvariant`-aware dispatch in the validator.
- **Reward generosity.** If LLM consistently proposes `KillEnemies 5` + `Credits base_value: 15`, stinginess is a reward/difficulty mismatch. Validator doesn't enforce; observe empirically.
- **Session memory leak via `StoryMission.allMissions`.** Each broker mints a new storyId. The dict grows un-bounded per session — minor since sessions are hours, not days. Accept; v1.1 adds eviction.
- **Experience reward test coverage.** `GameMath.GetExperienceRewardValue` reads `GamePlayer.current.commander.level` internally — NRE in unit tests. Plan's test suite covers Credits but not Experience scaling; Experience correctness left to manual E2E Scenario 2 (payout values in notification).
