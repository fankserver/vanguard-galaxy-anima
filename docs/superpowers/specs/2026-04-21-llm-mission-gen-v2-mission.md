# VGAnima — LLM-Authored Missions (v2-mission) Design

**Date:** 2026-04-21
**Status:** Design proposed, pending plan + implementation.
**Builds on:** Shipping v1 (dialogue-only LLM output).
**Related:** `docs/superpowers/notes/2026-04-20-vganima-llm-authored-missions-future.md` — original Option C vision this slice realizes.

## Goal

Let the LLM author the entire mission — name, description, multi-step objectives, rewards — fused with the dialogue it already writes. Replaces the plugin-authored `Jobsite Survey` test mission. Every broker ships a unique mission whose narrative and mechanics are coherent because they're generated together.

## Architecture (one paragraph)

The v1 pipeline stays intact end-to-end (bar injection → async context gather → HTTP → validate → cache → main-thread dispatch → dialogue replay). **What changes is the output schema and what we do with it after validation.** The LLM now returns `vganima/mission/v1`: the same 3 dialogue arrays PLUS a `mission` block describing name / description / 1-3 steps of whitelisted objectives / clamped rewards. `ResponseValidator` grows a `MissionBlockValidator` that enforces tight whitelists (faction enum, objective subclass allowlist, trigger allowlist, item-type curated list) and hard clamps (step count, required_amount, reward amounts). `MissionFactoryFromJson` translates the validated JSON into a real `Mission` instance with fresh per-broker `storyId`. The mission is registered into vanilla's `StoryMission` registry at broker-injection time, not at plugin startup. Any validation failure → no broker, same as v1.

## Tech Stack

Unchanged from v1: BepInEx 5.4.23.2 + HarmonyX 2.10, netstandard2.1, Newtonsoft.Json (game-provided), `HttpClient`, xUnit cross-TFM. No new package references.

## What changes vs v1 (delta summary)

| Area | v1 | v2-mission |
|---|---|---|
| Output schema | `vganima/story/v1` | `vganima/mission/v1` (replaces v1) |
| Mission body | Hardcoded `Jobsite Survey` template | LLM-authored, per-broker |
| Assigner | `TestMissionAssigner` → single fixed storyId | `LlmMissionAssigner` → per-broker storyId generated from the LLM blob |
| `StoryMission.Add` | Called once in `Plugin.Awake` (single id) | Called per broker at injection time (unique id per mission) |
| Validator | 3 dialogue arrays | + mission block (factions, objectives, triggers, items, rewards — all whitelisted + clamped) |
| Prompt | Describes dialogue schema only | Describes mission schema + whitelists + examples of allowed shapes |

## Section 1 — Contract

Same versioning principle as v1 (see v1 spec §1 if referenced elsewhere — not in repo since we pruned): strict schema field; v2 schema value is `"vganima/mission/v1"` (the `/mission/` segment distinguishes this family from future `/story/` or `/arc/` schemas); validator rejects any unknown top-level keys; no silent extensions.

## Section 2 — Output Schema

```jsonc
{
  "schema": "vganima/mission/v1",

  // Dialogue blocks — same rules as v1 (ASCII, size bounds, <=120 chars/line).
  "pitch":    [ "...", "...", "..." ],   // 3-5 lines
  "check_in": [ "..." ],                  // 1-2 lines
  "payout":   [ "...", "..." ],           // 2-4 lines

  "mission": {
    "name":             "string <= 60 chars, ASCII",
    "description":      "string <= 500 chars, ASCII",
    "completion_text":  "string <= 200 chars, ASCII",
    "source_faction":   "one of Faction.all identifiers",

    "steps": [  // 1..3 steps
      {
        "objectives": [  // 1..2 objectives per step
          { "type": "<whitelisted objective type>", ...type-specific fields }
        ]
      }
    ],

    "rewards": [  // 1..5 rewards
      { "type": "<whitelisted reward type>", ...type-specific fields }
    ]
  }
}
```

## Section 3 — Objective Whitelist (v2-mission v1)

Intentionally narrow at launch. Expand once we see what the LLM actually produces under real prompts. Each type gets its own per-field validator.

### `KillEnemies`
```jsonc
{
  "type": "KillEnemies",
  "enemy_faction":   "string, must be in Faction.all identifiers",
  "required_amount": 1..5,
  "description":     "string <= 80 chars, ASCII"
}
```
Validator also cross-checks `enemy_faction` against the player's context (`at_war` or `reputation <= 0`) and rejects if the faction is friendly — no "kill your allies" missions.

### `ProtectUnit`
```jsonc
{
  "type": "ProtectUnit",
  "protect_text": "string <= 80 chars, ASCII"
}
```
No parameters beyond flavor text. Vanilla `ProtectUnit` fires `UnitProtected` when the protected unit survives a combat step.

### `TriggerObjective`
```jsonc
{
  "type":            "TriggerObjective",
  "trigger":         "DockedWithSpaceStation" | "ArrivedAtSpaceStation" | "TravelToPOI",
  "required_amount": 1..3,
  "description":     "string <= 80 chars, ASCII"
}
```
Whitelist of triggers is hardcoded — the three above are confirmed firing in plain gameplay per the decomp survey. Other triggers (CompletePatrol, BountyTargetKilled, SalvagedItem, etc.) require specific game setup we're not staging here; added in later versions when proven.

### `CollectItemTypes`
```jsonc
{
  "type":            "CollectItemTypes",
  "item_types":      ["IronOre", "TitaniumOre", ...],  // curated whitelist below
  "required_amount": 1..50,
  "description":     "string <= 80 chars, ASCII"
}
```
**Curated item-type whitelist** (v1 — extends over time): `IronOre`, `TitaniumOre`, `SilicaOre`, `CopperOre`, `ScrapMetal`, `RefinedIron`, `RefinedTitanium`, `SalvageParts`. Everything else rejected. The whitelist lives as a `HashSet<string>` in `ItemTypeWhitelist.cs`.

### Explicitly NOT allowed in v2-mission v1

- `TradeOffer` — needs validated `deliver_to` POI. Add in v2-mission v1.1 once we validate POI references against `context.connected_systems`.
- `Mining` / `Salvage` subclasses — need specific POI setup (asteroid field, wreck field). Add once we can generate or locate the POI from context.
- `Crafting` — needs forge + crafting-recipe plumbing.
- `CombatStationDestroyed`, `MinerChasedOff`, `CraftItem`, `BountyTargetKilled` — game-specific setup.
- Conquest objectives — story-mode concerns, not broker-shaped.
- Custom objectives not in the whitelist.

## Section 4 — Reward Whitelist

Three reward types. All amounts clamped at runtime against `GameMath` scaling so the LLM can't print 10M credits.

### `Credits`
```jsonc
{ "type": "Credits", "base_value": 10..200 }
```
Plugin runs `GameMath.GetCreditsValue(base_value, player.level)` to scale. Hard cap: `base_value <= 200`.

### `Experience`
```jsonc
{ "type": "Experience", "base_value": 10..150 }
```
Plugin runs `GameMath.GetExperienceRewardValue(base_value, player.level)`.

### `Reputation`
```jsonc
{ "type": "Reputation", "faction": "<Faction.all>", "amount": -500..500 }
```
Must match a real faction. Negative values are allowed (a paid-hit-job mission legitimately damages rep with the hit faction).

## Section 5 — Validator

New file: `VGAnima/Llm/MissionBlockValidator.cs`. Called by `ResponseValidator` after the dialogue block passes.

Rule ordering (first failure raises):

1. `mission` field is a JSON object.
2. Strict field set: `{name, description, completion_text, source_faction, steps, rewards}` — no extras.
3. Each string field: non-empty, ASCII-only, under its length cap.
4. `source_faction` in `Faction.all` identifiers.
5. `steps` is an array, 1..3 elements.
6. Each step: strict `{objectives}` key, 1..2 elements.
7. Each objective: `type` is in the whitelist; dispatch to per-type validator.
8. `rewards` is an array, 1..5 elements. Each reward: `type` whitelisted; per-type validator.
9. Global coherence: at least one step has a `KillEnemies`/`TriggerObjective`/`CollectItemTypes` objective (something that isn't pure `ProtectUnit` — a mission of only "protect" with no other action is degenerate).

Validation failure raises `LlmValidationException` with field-path + rule in the message; caught by the orchestrator → log + skip broker (same as v1 failure matrix).

## Section 6 — MissionFactoryFromJson

New file: `VGAnima/Missions/MissionFactoryFromJson.cs`.

Translates validated JSON into a `Mission` instance. Each objective type has a small mapper:

- `KillEnemies` JSON → `Source.MissionSystem.Objectives.KillEnemies` with `enemyFaction = Faction.Get(...)`, `requiredAmount`, `description`.
- `ProtectUnit` → `ProtectUnit { protectText = ... }`.
- `TriggerObjective` → `TriggerObjective { trigger = MissionTrigger.<parsed>, requiredAmount, description }`.
- `CollectItemTypes` → `CollectItemTypes { itemType = InventoryItemType.Get(...), requiredAmount, description }` (multiple item types → one `CollectItemTypes` objective per item — or if the game supports item arrays, map directly; TBD in impl).

Rewards:
- `Credits` → `new Credits { amount = GameMath.GetCreditsValue(base_value, player.level) }`.
- `Experience` → `new Experience { amount = GameMath.GetExperienceRewardValue(base_value, player.level) }`.
- `Reputation` → `new Source.MissionSystem.Rewards.Reputation { faction = ..., amount = ... }`.

Global fields:
- `name`, `description`, `completionText` — copied verbatim from JSON.
- `sourcePoi`, `turnIn` — both set to the broker's station (matches v1 flow).
- `sourceFaction` — from JSON.
- `difficulty` = `MissionDifficulty.Story`, `trackedOnHud` = true, `dynamicLevel` = true, `canBeIdled` = false, `iconName` = `"Combat"` (safe default; can extend schema to let LLM pick an icon later).
- `storyId` — generated per broker: `$"vganima_llm_{station.guid}_{brokerSeed}_{Guid.NewGuid():N}"`. Uniqueness guarantees no collision with prior brokers or in-flight saves.

## Section 7 — Runtime StoryMission Registration

Different from v1: `StoryMission.Add(...)` is now called **per broker at injection time**, not at `Plugin.Awake`. The factory delegate captures the validated JSON (already materialized as a real `Mission` ready to return) so subsequent calls to `StoryMission.Get(player, id)` return the already-built instance.

Implementation sketch:
```csharp
var mission = MissionFactoryFromJson.Build(validatedJson, player, brokerStation);
var storyId = mission.storyId;
StoryMission.Add(new StoryMission(
    storyId,
    _ => mission,  // factory delegate returns pre-built instance
    checkAvailable: null,
    pickupHint: "VGAnima Broker"));
```

**Save/load:** if the player saves mid-mission and reloads in the same session, `Mission.FromJson(storyId)` resolves via the registered factory → returns the same Mission instance. If the player reloads across sessions, the registration is gone → `KeyNotFoundException`. Mitigation in scope for v2-mission v1.1 (not this slice): persist validated JSON blobs to `BepInEx/cache/vganima/<save-guid>.json` and replay on `Plugin.Awake` before save load. For now: **documented known limitation; players shouldn't save mid-LLM-mission across sessions**.

## Section 8 — Prompt

System prompt grows (rough sketch — tune empirically):

```
You write missions for a broker NPC in a space-trading game. The player
will give you their current state and the broker's identity. You output
ONE JSON object matching the vganima/mission/v1 schema.

The JSON has two parts:
1. Dialogue (pitch / check_in / payout) — 3 short line arrays, same as
   before. In-character, ASCII only, <= 120 chars each, no emoji.
2. Mission — the actual job:
   - name, description, completion_text (max 60/500/200 chars, ASCII)
   - source_faction (one of: marauders, policeGuild, bountyGuild,
     tradingGuild, miningGuild, industrialGuild, salvageGuild, stranded,
     <expanded at impl time>)
   - 1-3 steps, each with 1-2 objectives. Objective types:
     * KillEnemies: enemy_faction, required_amount (1-5), description
     * ProtectUnit: protect_text
     * TriggerObjective: trigger in [DockedWithSpaceStation,
       ArrivedAtSpaceStation, TravelToPOI], required_amount (1-3),
       description
     * CollectItemTypes: item_types (from [IronOre, TitaniumOre,
       SilicaOre, CopperOre, ScrapMetal, RefinedIron, RefinedTitanium,
       SalvageParts]), required_amount (1-50), description
   - 1-5 rewards. Reward types:
     * Credits: base_value 10-200
     * Experience: base_value 10-150
     * Reputation: faction (as above), amount -500..500

RULES:
- Dialogue + mission must be consistent: if the pitch promises rescue,
  the mission includes ProtectUnit or KillEnemies, not CollectItemTypes
  alone.
- source_faction is the hiring broker's faction. enemy_faction must be
  hostile or neutral to the player (check reputation / at_war in context).
- Let the player's state shape the mission: high level = tougher, low
  credits = smaller rewards, specialization hints at the activity.
- No narrative contradictions. No impossible asks. No emoji.

Reply with ONLY the JSON object — no preamble, no markdown fences.
```

User prompt carries the context JSON + a short brief:
```
Context:
<LlmContext JSON>

Broker: <broker.name>, <gender>, at <station>, aligned with <faction>.

Produce one vganima/mission/v1 JSON.
```

Exact prompt wording is in-scope for empirical iteration during implementation; this is a sketch.

## Section 9 — Caching / Lifetime

Same as v1:
- Validated mission lives in `ConversionRecord.LlmStory` (rename: the field now holds dialogue + mission metadata; we can either rename to `LlmPayload` or keep the name and extend the record).
- Dies with the broker. Evict / depart → record removed → factory delegate dropped from registry. (Note: this means the `StoryMission.allMissions` dict grows over a session. We don't remove registered storyIds — minor leak in memory of dict entries proportional to brokers seen per session. Accept for v1; add cleanup in v1.1 if needed.)

## Section 10 — Failure Matrix

Same 7 classes as v1 + one new:

| Failure | Log level | Marker |
|---|---|---|
| LLM disabled / unreachable | Debug/Warning | same as v1 |
| Timeout / HTTP non-2xx / network | Warning | same as v1 |
| Malformed JSON | Warning | same as v1 |
| Schema mismatch (dialogue block) | Info | same as v1 |
| **Mission block rejected by validator** | Info | `LLM mission validation failed: <field> <rule>; skipping broker` |
| Any other exception | Error | full trace |

In every case: **no broker injected**. Same silent-skip policy as v1.

## Section 11 — Integration Changes

- **New:** `MissionBlockValidator.cs`, `MissionFactoryFromJson.cs`, `ItemTypeWhitelist.cs`, `LlmMissionAssigner.cs` (replaces `TestMissionAssigner`).
- **Rename / extend:** `LlmStory` → holds dialogue + mission metadata (the validated Mission reference).
- **Update:** `ResponseValidator` adds a branch on the new schema value, dispatches to the mission-block validator after the dialogue block passes.
- **Update:** `BarRefreshPatches.FinalizeBrokerInjection` — on validator success, build Mission, register storyId via `StoryMission.Add`, stash in ConversionRecord.
- **Remove:** `TestStoryMissions.cs` and `TestMissionAssigner.cs` are retired (in-flight save-state with the old `vganima_test_jobsite_survey` id will `KeyNotFoundException` — documented migration note below).
- **Unchanged:** Context gatherer, HTTP client, async scheduler, dialogue dispatch, pitch provider, rehydrate flow.

## Section 12 — Migration From v1

One breaking change: savegames with an active `vganima_test_jobsite_survey` will fail to rehydrate because `TestStoryMissions.Register()` is gone.

Options:
1. **Leave `TestStoryMissions.Register()` in place** — keeps the legacy factory around so old saves load. Zero cost (8 lines of code).
2. **Full removal + documented "finish your Jobsite Survey before updating"** — breaks the worst case but cleaner codebase.

**Recommendation:** option 1. Cost is negligible; breakage is obnoxious for early-game testers. Mark as `[Obsolete("retained for legacy save compatibility")]` and remove in a later version once we're confident nobody has stale saves.

## Section 13 — Config

No new config keys. Existing `[Llm]` section applies unchanged:
- `Enabled` / `BaseUrl` / `Model` / `TimeoutSeconds` / `ApiKey` / `EnableThinking` / `MaxTokens` / `Temperature`.

Clamp ceilings (step count, required_amount, credits base_value, reputation amount, etc.) are internal constants in the validator — not user-tunable. If they need tuning, bumped in a code release.

## Section 14 — Testing

**Unit (xUnit):**
- `MissionBlockValidator` — one test per rule per objective/reward type. ~40 cases target. Cover:
  - Happy paths for each objective type.
  - Each objective's field-by-field rejections (missing field, wrong type, out-of-range, non-ASCII, non-whitelisted enum value).
  - Global rules (step count, objective count per step, reward count, degenerate-mission rule).
  - Cross-context rules (enemy_faction not hostile-to-player rejection).
- `MissionFactoryFromJson` — builds a `Mission` from a known-good JSON and asserts field-by-field equality. Uses the same `FakeGameStateView`-style seam for `GamePlayer.current.level` (which drives `GameMath.GetCreditsValue`).
- Extend `LlmContextJsonTests` round-trip pattern to cover the `mission` block.

**Not unit-testable (integration):**
- `StoryMission.Add` runtime registration — requires Unity-bound static ctor. Test in manual E2E.
- End-to-end real LLM call — rely on existing InMemoryLlmClient fake for deterministic tests; live LLM validated via manual E2E only.

**Manual E2E checklist (rough):**
1. LLM disabled → no broker (regression — same as v1).
2. Happy path: broker appears, dialogue references mission it's offering. Click accept → quest log shows generated mission name/description. Complete via in-game actions → payout + departure.
3. Multi-step mission (e.g., "kill 3 pirates + collect 5 ore") → both steps tick correctly in order.
4. Enemy-faction sanity: LLM's chosen `enemy_faction` is actually hostile to player in-game.
5. Validator rejection path: point at a broken endpoint (serve nonsense JSON) → no broker, log marker.
6. Save/reload mid-session with an active LLM mission → mission survives (in-memory registry still has the factory). Across sessions → known limitation, KeyNotFoundException or leave `TestStoryMissions.Register()` in place for legacy tolerance.

## Section 15 — Non-goals

- `TradeOffer` objectives (add when we validate POI refs against `connected_systems`).
- `Mining` / `Salvage` / `Crafting` subclasses (game-setup-dependent).
- Mission chains (LLM reward type `StoryMission { missionId }` that auto-queues a next mission).
- Cross-session persistence of LLM-authored missions.
- Multi-broker narrative arcs (brokers remembering prior interactions).
- Prompt-engineering framework / A/B testing of prompt variants.
- LLM picks `iconName` / `difficulty` / `level` overrides.
- Mission templates — explicitly removed from the design vs. the earlier v2-theme sketch.

## Section 16 — Open Questions (observe during implementation)

- **Prompt quality.** First-pass system prompt will need iteration. Plan includes explicit "tune after first 10 live outputs" step.
- **Schema drift under stress.** If the LLM frequently violates the schema (e.g., sends `kill_enemies` instead of `KillEnemies`), we add case normalization at the validator layer — but we start strict and see.
- **Mission-dialogue coherence.** No mechanical check. Rely on: (a) prompt instructions, (b) LLM is seeing both in one pass so it has no incentive to diverge.
- **Rewards-to-difficulty mismatch.** If the LLM asks for `KillEnemies 5` but sets `Credits base_value: 15`, the reward feels stingy. Validator doesn't enforce reward↔difficulty. Observe; add heuristic clamp if pattern emerges.
- **`storyId` uniqueness over session.** `Guid.NewGuid()` is unique per mission. Dict grows but doesn't collide.
- **Item-type whitelist completeness.** Starts with 8 common ore/scrap items. Expand as LLM outputs surface interesting gaps.

## Follow-up

Once this ships and plays well:
- **v2-mission v1.1:** add `TradeOffer`, richer POI validation.
- **v2-mission v1.2:** cross-session persistence via JSON cache.
- **v3:** mission chains via reward-driven auto-queue; brokers with memory across bar visits.
