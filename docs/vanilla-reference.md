# Vanilla Vanguard Galaxy — Mechanics Reference

Self-contained reference for VGAnima. All concrete numbers, thresholds, and
rule tables needed to reason about vanilla missions, factions, and POIs
without re-running decompilation.

---

## Reward formulas

Two helpers compute final reward amounts from an LLM-emitted `base_value`:

**Credits**:
```
amount = round(base × 100 × 2^(level / 14))
```
Credits double every 14 levels. No player-level read and no over-level
penalty — only the `level` argument matters. `level` comes from whichever
path constructs the mission (see below).

**Experience**:
```
amount = ceil(base × 5 × modifier × 2^(playerLevel / 9))
```
`modifier` depends on player level vs `targetLevel`:

| Player vs target | Modifier |
|---|---|
| player > target by 1 | 0.8 |
| player > target by 2 | 0.6 |
| player > target by 3 | 0.4 |
| player > target by 4+ | 1e-05 (effectively zero — vanilla anti-farm) |
| player = target | 1.0 |
| player < target by 1 | 1.1 |
| player < target by 2 | 1.2 |
| player < target by 3+ (capped) | 1.3 |

The exponential `2^(playerLevel / 9)` always applies. An over-leveled
player doing low-level content collapses to ~1 XP; an on-level player
sees rewards grow exponentially with progression.

**Conquest-style commendations** use a separate helper that computes
`CostMultiplier(poi.level - 30)` — the -30 offset bakes in a late-game
assumption. Not relevant for brokers, but good to know if something shows
odd commendation values.

---

## Mission reward paths

Vanilla has two distinct reward-generation paths, differing only in which
`level` they feed the formulas:

| Path | `level` passed in | `dynamicLevel` | Use case |
|---|---|---|---|
| Procedural board missions (`MissionGenerator.AddRewards`) | `mission.level` (= `sourcePoi.level`) | `false` | Station mission boards — Courier, MineOre, BountyHunt, Escort, ClearAsteroidField, ClearSalvageField, SalvageWreck, SalvageSamples, OreSamples, HelpMiner, DeliverCraftedGoods, StationBattle, TradeTerminal, TradeMaterials, Umbral |
| Story side missions (`SideMissions`) | `GamePlayer.current.level` | `true` | Evergreen repeatable loops — Patrol, Bounty, FastLane |

**VGAnima brokers follow the procedural path** — area-anchored, station
level, `dynamicLevel = false`. A level-20 player at a level-3 station gets
level-3 credits and ~1 XP from anti-farm, exactly like a vanilla-board
mission at the same station.

---

## Procedural reward construction

Procedural missions compute rewards from three ingredients, multiplied
together with random noise on top:

### (1) Difficulty → `rewardMultiplier` (XP only)

| Difficulty | Multiplier | XP base (= √m × 50) | XP base with ±25% noise |
|---|---|---|---|
| Easy | 1.0 | 50 | 40–63 |
| Normal | 1.8 | 67 | 54–84 |
| Hard | 2.8 | 84 | 67–105 |
| Skull | 3.6 | 95 | 76–119 |
| Insane | 6.0 | 122 | 98–153 |

Credits do **not** use `rewardMultiplier`. Difficulty affects credits only
indirectly through higher-level stations (which have higher `poi.level`).

### (2) Per-generator `itemValue` multiplier (credits)

Applied against a flat `40f` base value. Per-objective-archetype
modifiers:

| Generator | Multiplier | Credits base | Credits base with ±25% noise |
|---|---|---|---|
| Courier (deliver-only) | 0.6 | 24 | 19–30 |
| MineOre | 0.75 | 30 | 24–38 |
| TradeMaterials | 0.5 × rewardMultiplier | 20 (Easy) .. 120 (Insane) | 16–150 |
| BountyHunt | 1.0 | 40 | 32–50 |
| Escort | 1.0 | 40 | 32–50 |
| ClearAsteroidField | 1.0 | 40 | 32–50 |
| ClearSalvageField | 1.0 | 40 | 32–50 |
| SalvageWreck | 1.0 | 40 | 32–50 |
| SalvageSamples | 1.0 | 40 | 32–50 |
| OreSamples | 1.0 | 40 | 32–50 |
| HelpMiner | 1.0 | 40 | 32–50 |
| DeliverCraftedGoods | 1.0 | 40 | 32–50 |
| StationBattle | 1.0 | 40 | 32–50 |
| TradeTerminal | 1.0 | 40 | 32–50 |
| Umbral (endgame override) | 5.0 | 200 | 160–250 |

### (3) Noise envelope

Both credits and XP get a uniform `RandomRange(0.8, 1.25)` multiplier
after the main formula.

### (4) Reputation

Procedural missions pick `amount` uniformly from `{200, 250, 300, 350}`
and multiply by an `enemyBonus` of 1 (neutral mission) or 4 (mission
against a player-enemy faction).

---

## Step count does not multiply rewards

Critical, non-obvious rule: vanilla's `AddRewards` is called **once** per
mission with a single reward pool, regardless of how many `MissionStep`s
the mission has. Step count is UI structure only.

Concrete examples from vanilla:

- `OreSamples` — 2 steps (mine, then deliver). Pays the same as
  `MineOre` (1 step) at the same difficulty and level.
- `SalvageSamples` — 2 steps (salvage, then deliver). Pays the same as
  a comparable 1-step salvage mission.
- `Courier` — 1 step, `0.6` multiplier. Pays less than `OreSamples`
  (2 steps, `1.0` multiplier) because the *archetype multiplier* differs,
  not because of step count.

Mission complexity is expressed through the per-archetype `itemValue`
multiplier (0.5 / 0.6 / 0.75 / 1.0), never through step count. When
authoring broker missions, pick `base_value` by archetype.

---

## Vanilla's full reward ladder (all mission families)

| Source | Credits base | XP base (with noise) | Reputation | Scope |
|---|---|---|---|---|
| Procedural (Easy) | 20–50 | 40–63 | 200–350 × (1 or 4) | Station mission board |
| Procedural (Normal → Insane) | 20–50 | 54–153 | 200–350 × (1 or 4) | Station mission board |
| TradeMaterials (all difficulties) | 16–150 | per difficulty | 200–350 × (1 or 4) | Station mission board |
| SideMissions story (Patrol / Bounty / FastLane) | 50 or 100 flat | 50 or 100 flat | 400 | Infinite side loops |
| SkilltreeMissions (progression gates) | 200 flat | n/a (Skillpoints) | 300 | One-shot unlocks |
| UmbralMissions (endgame) | 100 – 20 000 | 100 – 400 | 300 – 1000 | Post-campaign arc |
| ConquestMissions (story) | 2 000 000, 10 000 000 | 100 | 300 – 800 | Scripted wars |
| MercenaryIntroduction | — | — | 1000 | One-shot intro |

Brokers should stay inside the Procedural + SideMissions envelope.
Everything below (Skilltree / Umbral / Conquest / Mercenary intro) is
progression-gated and should not appear from a bar-spawned broker.

---

## VGAnima-observed baseline (decoded from live vanilla at level 1)

Three vanilla level-1 missions observed on a live mission board at a
level-1 station. Inferred bases by running the displayed amounts back
through the formulas:

| Mission | Credits (raw) | XP (raw) | Reputation | Inferred credits base | Inferred XP base |
|---|---|---|---|---|---|
| Deliver 2 units | 1797 | 333 | 300 | 17 | 62 |
| Kill pirates in POI | 999 | 226 | 300 | 10 | 42 |
| Mine 14 items | 3793 | 323 | 250 | 36 | 60 |

Fits the expected procedural envelope: Courier-ish (24), BountyHunt-ish
(40), MineOre-ish (30), each within the ±25% noise band.

---

## VGAnima broker clamp choices

Clamps chosen to align with the Procedural + SideMissions envelope and
keep broker missions from feeling richer than vanilla's board.

| Reward | Min | Max | Rationale |
|---|---|---|---|
| Credits `base_value` | 15 | 100 | Procedural observed 24–50; SideMissions ceiling 100. Above this mimics Skilltree gates. |
| Experience `base_value` | 30 | 100 | Procedural 40–153 across all difficulties; cap at SideMissions ceiling 100 to avoid crossing into gate territory. |
| Reputation `amount` | -500 | 500 | Procedural 200–350, SideMissions 400, up to 500 for faction-defining favors. Negative allowed for hostile-faction side effects. |

### Archetype magnitude guidance (for prompt)

| Archetype | Credits base | XP base |
|---|---|---|
| Deliver / fetch / courier | 20–30 | 40–55 |
| Mine / salvage / collect | 25–40 | 45–60 |
| Kill / escort / protect / clear | 35–50 | 55–75 |
| Hazardous multi-objective | 60–100 | 75–100 |
| Reputation amount | 200–350 standard; up to 500 for faction-defining |

Step count is not a multiplier. Archetype is what moves the needle.

---

## Faction model

Vanilla has exactly 19 factions — 18 interactable + `Player`. The set is
closed; no runtime-loaded faction path exists.

### The 18 identifiers, display names, abbreviations, category

| Identifier | Display name (en-US) | Abbreviation | Category |
|---|---|---|---|
| Marauders | Corsair Syndicate | SYN | Outlaw (starts enemy) |
| Fanatics | Meridia's Chosen | — | Outlaw (starts enemy) |
| HolyRadicals | Meridia's Radicals | — | Outlaw (starts enemy) |
| Amalgam | Amalgam | — | Outlaw (starts enemy, story-scripted) |
| Smugglers | Void Drifters | — | Criminal |
| Darkspacers | Darkspace Compact | — | Criminal |
| Puppeteers | Your Employer (→ Umbral Reach after story flag) | — | Criminal / story |
| Stranded | Stranded | — | Neutral non-corporate |
| MiningGuild | Mindus Holdings | — | Guild |
| TradingGuild | Intertrade Network | — | Guild |
| SalvageGuild | Steel Vultures | — | Guild |
| IndustrialGuild | Forge Industries | — | Guild |
| PoliceGuild | Canisec | — | Guild |
| BountyGuild | Orsanon Security | — | Guild |
| MercenaryGuild | Omnitac Agency | — | Guild |
| Gold | Luminate Combine | — | Corporation |
| Red | Kolyatov Collective | — | Corporation |
| Blue | Stellar Industries | — | Corporation |

The three corporations (Gold, Red, Blue) are grouped internally as
`Faction.corporations`. Stations routinely report their allegiance as one
of these three.

Display names resolve at UI render time from the locale TextAsset via
`@FactionName<Identifier>` keys (e.g. `@FactionNameMarauders` →
"Corsair Syndicate"). Descriptions use `@FactionDesc<Identifier>`.

**Rule for VGAnima:** identifiers in all programmatic paths (mission
block fields, `Faction.Get`, whitelists); display names only in
player-facing dialogue text.

### Starting reputation

Four factions are seeded as immediate enemies: **Marauders, Fanatics,
Amalgam, HolyRadicals**. Each gets rep `-6000` against every other
faction. Three criminal factions (Smugglers, Darkspacers, Puppeteers) get
lighter penalties. Every other faction starts at 0 or positive per
per-faction defaults.

For a fresh character, the hostile-faction list is exactly those four.
Amalgam and HolyRadicals are additionally excluded from the random-enemy
picker — they're story-scripted special enemies, not fodder for
general-purpose procedural combat.

### Hostility rule

A faction is an enemy iff the player is in `atWar` with them, or
reputation is strictly less than `-500`. No faction subclass overrides
this.

| Condition | Is enemy? |
|---|---|
| Player in `atWar` with faction | Yes |
| `rep < -500` | Yes |
| `rep ≥ -500` AND not at war | No |

### VGAnima three-band relation model

| Band | Rule | Use in broker missions |
|---|---|---|
| `friendly` | `rep > 0` | Eligible for `source_faction` (can hire the player) |
| `neutral` | `-500 ≤ rep ≤ 0` | Not targetable for kills; not a plausible employer |
| `hostile` | `at_war OR rep < -500` | Eligible for `enemy_faction` (kill targets) |

The validator refuses kill missions against friendly or neutral factions,
matching vanilla's "can I dock there" test.

---

## POI (Point of Interest) model

### POI level and area scaling

All POIs (stations, asteroid fields, combat zones) inherit a single
integer `level` field from `MapElement.level`. Display strings like
`dangerLevel` exist separately (e.g. `@MapPOIDangerPirates`,
`@MapPOIAttackFriendlies`) and are localization keys, not numbers.

For VGAnima broker missions: mission level = `station.level` at
construction time. `Mission.level` is a getter. When `dynamicLevel =
false` (our case), it takes the max of the source POI's level and every
POI referenced by any step, so multi-leg missions anchor to the hardest
area they touch. When `dynamicLevel = true` (vanilla SideMissions), it
returns the live player level on every access.

### MissionStep POI references

A `MissionStep` binds to POIs two ways:

| Field | Type | Ownership | Serialization |
|---|---|---|---|
| `dynamicPointOfInterest` | `MapPointOfInterest` | Mission-owned, spawned at mission construction | Inline in the step JSON; fully reloaded on save/load |
| `poiHints` | `List<MapPointOfInterest>` | References to pre-existing map POIs | Guid strings; resolved via galaxy-map lookup on load |

Objectives themselves rarely reference POIs directly. Most rely on the
containing step's `IsPointOfInterest` gate: triggers and kill counters
only fire while the player is inside the step's POI. `TravelToPOI` is the
exception — it stores a target POI guid and resolves at activation.

### Combat POI lifecycle

A Combat POI auto-removes itself from the system when:

1. Its `lastVisitedTime` is set (player has been there), AND
2. No timer remains (`timeLeft ≤ 0`), AND
3. No triggered payloads pending, AND
4. No persistable data remaining on the POI demands it stay alive, AND
5. No player-enemy units still present.

This means "clear the POI" missions automatically clean up after the
player kills the last enemy — no explicit despawn needed.

### Spawning a Combat POI

The system-map has a method that creates a fresh `Combat` POI, configures
it with a faction and optional forced level, randomizes its position, and
inserts it into the system's POI list. Returns the live POI reference.

### Populating a POI with units

Methods on `MapPointOfInterest` (inherited by Combat):

| Method | Purpose |
|---|---|
| `CreateUnitPayload(pointsScale, gameplayType, faction, minPointsPerUnit, maxPointsPerUnit, minUnits, maxUnits, fixedRank)` | Builds a list of unit data matching the requested point budget and constraints. `pointsScale = 1.0` gives a standard combat group. |
| `AddGuards(payload, random)` | Attaches the payload as ambient defenders. `random` defaults to the global seeded random if null. |
| `AddPirateTurrets(count, random, faction)` | Adds static turret defenders. |
| `AddTriggeredSpawn(payload, delaySeconds)` | Optional wave that appears `delaySeconds` after the player engages. |
| `totalUnitCount` (read) | Count of all enemies spawned. Read AFTER `AddGuards` / `AddTriggeredSpawn`. |

### Vanilla "clear a POI" composition (no dedicated class)

Vanilla has **no** `ClearPoi` / `DestroyStation` / `ClearField` objective
class. The feel is composed from existing parts:

1. Create a Combat POI in the source station's system.
2. Set its faction; seed it with guards (and optionally triggered waves
   for harder difficulties).
3. Build a `MissionStep` with `system` set and `dynamicPointOfInterest`
   pointing at the spawn.
4. Add a `KillEnemies` objective whose `requiredAmount` equals
   `totalUnitCount`.

When the last enemy dies, the objective count hits the requirement, the
step completes, and the POI self-cleans.

**Variants:**

- `ClearAsteroidField` — also calls `SetAsteroidFieldData(...)` and
  `InitializeAsteroids()` on the spawn, plus uses a `TriggerObjective`
  keyed to `MinerChasedOff` (fired by ambient miner AI when they flee)
  instead of `KillEnemies`. Same underlying pattern.
- `ClearSalvageField` — salvager variant, `SalvagerChasedOff` trigger.
- `BountyHunt` — the cleanest template (pure `KillEnemies` with
  `totalUnitCount`), what VGAnima's ClearPoi mirrors.

### MissionObjective subclasses (complete vanilla list)

`CollectCredits`, `CollectItemTypes`, `ConquestFactionEliminated`,
`ConquestFleetStrength`, `Crafting`, `CreditOffer`, `DroneTrigger`,
`Item`, `KillEnemies`, `Mining`, `ProtectUnit`, `Reputation`, `Salvage`,
`StationsInfected`, `SystemsConquered`, `TradeOffer`, `TravelToPOI`,
`TriggerObjective`.

No `ClearPoi`, `DestroyStation`, `ClearField`, or similar — the clear-POI
mechanic is always composed.

### TriggerObjective triggers (vanilla list)

Used in `TriggerObjective.trigger` enum. Subset relevant to brokers is a
whitelist; full vanilla list includes more values but most are story-
scripted:

- `DockedWithSpaceStation` — fires when the player docks at any station.
- `ArrivedAtSpaceStation` — fires when the player drops out of jump at a
  station.
- `MoveToArea` — fires when the player enters the step's POI area.
- `MinerChasedOff` — fired by ambient miner AI fleeing combat.
- `SalvagerChasedOff` — fired by ambient salvager AI fleeing combat.
- (Story-only: `UnitDestroyed`, various scripted arc triggers.)

VGAnima whitelist: `DockedWithSpaceStation`, `ArrivedAtSpaceStation`,
`MoveToArea`.

### VGAnima objective whitelist → vanilla realization

| LLM type | Emits | Notes |
|---|---|---|
| `KillEnemies` | `KillEnemies` objective | `requiredAmount` from LLM (1..5). Kill any N of faction, anywhere. |
| `ProtectUnit` | `ProtectUnit` objective | `requiredAmount = 1`. Keep the named unit alive. |
| `TriggerObjective` | `TriggerObjective` objective | Trigger one of DockedWithSpaceStation / ArrivedAtSpaceStation / MoveToArea. `requiredAmount` 1..3. |
| `CollectItemTypes` | `CollectItemTypes` objective | Category one of Ore, Salvage, RefinedProduct, TradeGoods. `requiredAmount` 1..50. (`Junk` intentionally excluded — reserved for special-quest variants in `docs/special-quest-ideas.md`.) |
| `ClearPoi` | Spawns Combat POI + `KillEnemies` | Mirrors `BountyHunt`. `requiredAmount` derived from `combat.totalUnitCount`. |

---

## Localization

All user-facing strings in vanilla are stored as `@Key` placeholder
strings on model objects (e.g. `faction.name = "@FactionNameMarauders"`,
mission `description = "@BountyHuntMissionDesc"`). At UI render time,
a translation helper resolves them against a locale TextAsset loaded
from `Resources/Language/<locale>`. The asset is an ini-style
`key = value` file.

Common key prefixes:

- `@FactionName<Identifier>` — faction display name
- `@FactionDesc<Identifier>` — faction description
- `@MapPOIDanger<Type>` — danger-level tooltip string
- `@Mission<TypeName>Name` / `...Mission` / `...MissionDesc` /
  `...MissionComplete` — generator mission text

For plugin-authored content (VGAnima broker dialogue), no translation
layer is used — the LLM emits the final string directly in the player's
language.

---

## Ten load-bearing rules for broker missions

1. Reward math anchors to area level (`station.level`), not player level.
2. `dynamicLevel` stays false; mission level is set once at construction.
3. XP collapses to ~1 for players >3 levels above the station — vanilla anti-farm, not a bug.
4. Step count is not a reward multiplier; archetype is.
5. Identifiers go in programmatic fields; display names only in dialogue text.
6. A faction is hostile iff `at_war` or `rep < -500`.
7. Only friendly factions plausibly hire brokers; only hostile factions are legal kill targets.
8. "Clear a POI" = spawn a Combat POI, attach to step, emit `KillEnemies` with `requiredAmount = totalUnitCount`.
9. Clamp credits base to 15..100 and XP base to 30..100 — outside this, rewards drift from the vanilla board.
10. Reputation rewards cluster around 200–350 in vanilla; 400 is the SideMission ceiling, 500 is the top of the broker-feasible range.

---

## Save / Load / Mission-Lifecycle API

Harmony hook targets and semantics for cross-session persistence work.
Scouted against `/tmp/decomp` — line numbers are approximate.

### Save-write

**Target:** `Source.Util.SaveGame.Store(JsonObject data, string saveName, SaveGameFormat format, int attempt)`

```csharp
public static void Store(JsonObject data, string saveName,
    SaveGameFormat format = SaveGameFormat.Compressed, int attempt = 0)
{
    _saves = null;
    FileInfo fileInfo = new FileInfo(SavesDir.FullName + "/" + saveName + ".save");
    // ... GZip-compress UTF-8 JSON into fileInfo ...
}
```

- **Save path:** `SavesDir.FullName + "/" + saveName + ".save"`. `SavesDir` resolves to `Application.persistentDataPath + "/Saves"` (public static `SaveGame.SavesPath`, initialized in static ctor).
- **Single-threaded** — `FileStream.Open(FileMode.Create)` on the main thread. Retries up to 5 attempts on IOException.
- **Harmony postfix** on `Store` is clean: `__args[1]` gives the `saveName`, so a postfix can derive the sidecar path as `$"{SaveGame.SavesPath}/{saveName}.save.vganima.json"` and write atomically after `Store` returns.

### Save-load

**Target:** `Source.Util.SaveGameFile.LoadSaveGame()` — instance method.

```csharp
public void LoadSaveGame()
{
    SaveGame.LoadState(Recall());  // Recall() reads + gunzips the file
}
```

- **Instance state exposes full path:** the `SaveGameFile` instance has `public readonly FileInfo File` and `public readonly string Name` (saveName without `.save` suffix). A Harmony prefix on `LoadSaveGame` sees `__instance.File.FullName` and can read the paired sidecar before `Recall()` and `LoadState(...)` fire.
- **Load order:** `LoadSaveGame` → `Recall` → `LoadState(data)` → `GamePlayer.FromJson(data["Player"])` → mission list deserialization via `Mission.FromJson` per entry.
- **Prefix on `SaveGameFile.LoadSaveGame`** fires strictly before any `Mission.FromJson` call — ideal hook for factory registration.

### Mission deserialization semantics

**Target:** `Source.MissionSystem.Mission.FromJson(JsonValue data)`

```csharp
public static Mission FromJson(JsonValue data)
{
    if (data.IsString)
        return StoryMission.Get(GamePlayer.current, data);  // <-- storyId reference path
    Mission mission = new Mission();
    mission.DataFromJson(data);                             // <-- embedded-object path
    return mission;
}
```

- **Active missions in a save are serialized as full JsonObjects**, not string IDs. They rehydrate via `DataFromJson` and **do NOT re-invoke the `StoryMission` factory**. Meaning: our `MissionFactoryFromJson.Build` is called once at creation time; the in-memory `Mission` is serialized with all its state; on load it's reconstructed directly without the factory.
- **String-path is hit** for cross-references where only the storyId was persisted (e.g. some archive or queue entries). Unknown `vganima_llm_*` IDs on this path throw `KeyNotFoundException` at `StoryMission.Get`.
- **Upshot for VGAnima:** registered factories are insurance, not primary. The placeholder safety net is what matters — it catches the string-path hits we can't otherwise satisfy after a session restart.

### Mission lookup

**Target:** `Source.MissionSystem.StoryMission.Get(GamePlayer player, string id)`

```csharp
public static Mission Get(GamePlayer player, string id)
{
    Mission mission = allMissions[id].generator(player);  // throws KeyNotFoundException on miss
    mission.storyId = id;
    return mission;
}
```

- Backing field `allMissions` is the static registry.
- **Harmony prefix on `Mission.FromJson` (not `StoryMission.Get`)** is the cleaner injection point — checks `data.IsString` + `StoryMission.allMissions.ContainsKey(id)` before the dictionary indexer fires. On miss for a `vganima_llm_*` id, returns a `PlaceholderMission` and skips the original via `return false`.

### Mission acceptance

**Target:** `Source.Player.GamePlayer.AddMissionWithLog(Mission mission)` (not the `string` overload — that one just delegates via `StoryMission.Get(this, id)` into the same `Mission` overload).

```csharp
public void AcceptMission(Mission mission)
{
    if (!IsMissionsLimitExceeded())
    {
        AddMissionWithLog(mission);
        SpaceStation.current.missionBoard.AcceptMission(mission);
        RefreshMissionPanel(mission);
    }
}
```

- **Single chokepoint for all acceptance paths** (dialogue accept, broker accept, mission board accept).
- **Harmony postfix** on `AddMissionWithLog(Mission)` fires for every transition from offered → active.

### Mission resolution

**Targets:**

- `Source.Player.GamePlayer.CompleteMission(Mission m, bool force)` — `GamePlayer.cs:826`
- `Source.MissionSystem.Mission.MissionFailed(string reason)` — `Mission.cs:355`
- `Source.Player.GamePlayer.ArchiveMission(string id, bool allowDuplicate)` — `GamePlayer.cs:769`

All three are non-virtual entry points on `GamePlayer.current` (singleton) or `Mission` itself. Harmony postfixes cover every resolution path.

### Player mission list access

- Active missions: `GamePlayer.current.missions` (`List<Mission>`)
- Extended active (bounty/patrol/industry + regular): `GamePlayer.current.allMissions` (`IEnumerable<Mission>`)
- Archived IDs: `GamePlayer.current.missionsArchive` (`List<string>`)
- Per-id lookup: `GamePlayer.current.GetActiveStoryMission(string id)` — null if not active

VGAnima's existing `IGamePlayerView.IsArchived(id)` / `GetActive(id)` already wrap these correctly.

### Save directory

```csharp
// SaveGame.cs static ctor
SavesPath = Application.persistentDataPath + "/Saves";
```

Save files live at `{persistentDataPath}/Saves/{saveName}.save`. Sidecar lives at `{persistentDataPath}/Saves/{saveName}.save.vganima.json`. Startup dead-sidecar sweep enumerates `*.vganima.json` in this directory and drops any whose paired `.save` is gone.
