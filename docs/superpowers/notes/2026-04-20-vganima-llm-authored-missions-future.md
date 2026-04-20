# VGAnima — LLM-Authored Missions (Future Work)

> **Status:** captured 2026-04-20. Deferred behind Option A (vanilla-mission wrapping). Not in current spec.

## The Idea

Instead of wrapping vanilla `StoryMission`s (Option A) or plugin-authored templates (Option B), let an LLM propose the **full mission structure** per broker: steps, objectives, rewards, turn-in location, sourceFaction. The plugin validates against a whitelist schema and registers the result at runtime via `StoryMission.Add(...)`.

Every broker in the bar thus offers a fresh, hand-crafted-feeling mission — no two brokers repeat, no pool exhaustion, no one-shot-per-save ceiling. The LLM writes the dialogue flavor AND the mission body.

## Why It's Worth Keeping on the Roadmap

- The current ceiling on Option A is ~3 `SideMissions` per save; once archived, brokers go silent forever. That's fine for testing the foundation but thin for a shipped mod.
- Plugin-authored templates (Option B) scale the pool but are still static — the LLM only varies the words, not the shape of the task.
- Option C is the only one where broker content feels genuinely open-ended and tied to whoever the broker *is* (their backstory → their mission).

## Hard Constraints From the Game

The mission subsystem has fixed surfaces the LLM can't stretch:

1. **`MissionTrigger` enum is closed.** `Source.MissionSystem/MissionTrigger.cs` is compile-time. LLM cannot invent a new trigger (no way to register one without IL patching). Any authored mission must bind its `TriggerObjective` to a value that already exists and that real gameplay code fires.
2. **`MissionObjective` subclasses are compile-time.** `TriggerObjective`, `TradeOffer`, `Salvage`, `Mining`, `KillEnemies`, `TravelToPOI`, etc. — the LLM can only compose from this set.
3. **`MissionReward` subclasses are compile-time.** Same story — `Credits`, `Experience`, `Reputation`, `Item`, `Skillpoint`, `Source.MissionSystem.Rewards.StoryMission` (chain next), etc.
4. **`turnIn` must be a real `MapPointOfInterest`.** LLM can only pick from known POIs in the current sector.
5. **Save/load round-trip.** A story mission serializes as just `storyId`. On load, `Mission.FromJson(string)` calls `StoryMission.Get(player, id)` — re-running the registered factory. If the plugin didn't persist the LLM-authored factory between sessions, the mission can't rehydrate → `KeyNotFoundException`.

## Proposed Approach

### 1. Constrain the LLM with a JSON schema

```jsonc
{
  "id": "vganima_llm_<broker-guid>_<n>",
  "name": "…",                        // LLM
  "description": "…",                 // LLM
  "sourceFaction": "tradingGuild",    // enum: Faction names
  "difficulty": "Story",              // enum
  "steps": [
    {
      "objectives": [
        {
          "type": "TriggerObjective",
          "trigger": "BountyTargetKilled",   // enum from whitelisted MissionTriggers
          "requiredAmount": 3,
          "description": "Hunt down 3 pirates in the Kepler belt"
        }
      ]
    }
  ],
  "rewards": [
    { "type": "Credits", "amount": 15000 },
    { "type": "Reputation", "faction": "tradingGuild", "amount": 200 }
  ],
  "dialogue": {
    "pitch": ["line 1", "line 2", "line 3"],
    "progress": ["line 1"],
    "complete": ["line 1", "line 2"]
  }
}
```

### 2. Plugin validates the JSON against a whitelist
- Reject any `MissionTrigger` not on the "LLM-safe" list (ones the plugin can verify real gameplay actually fires).
- Reject objective types beyond the composable subset (`TriggerObjective`, `TradeOffer`, possibly `Salvage`/`Mining`/`KillEnemies`).
- Reject rewards beyond `Credits`/`Experience`/`Reputation`/`Item`.
- Clamp numeric ranges (no 10 million credit rewards).
- Validate faction/POI names against real game enums.

### 3. Register at runtime
After validation, plugin builds a `CreateMission` factory from the JSON and calls `StoryMission.Add(new StoryMission(id, factory, checkAvailable, pickupHint))`.

### 4. Persist definitions between sessions
Store accepted JSON blobs in VGAnima's own save data (e.g., `BepInEx/config/vganima/broker-missions.json`). On `Plugin.Awake`, iterate stored blobs and re-register them into `StoryMission` **before** `GamePlayer.LoadGame()` runs. This makes save/load durable even across game restarts — the factory is reconstructed deterministically from the stored JSON, no LLM re-call needed.

### 5. Dialogue via Option A/B path
Broker's `Character.createDialogue` is the standard 3-branch pattern — archived→null, active+ready→complete, active→check-in, none→pitch. The `Dialogue` object just reads from the stored `dialogue.pitch` / `.progress` / `.complete` arrays. No change in dialogue wiring vs Option A.

## Prerequisites From Option A Foundation

Before C is viable, A must land:
- Broker character injection into bars.
- `character.createDialogue` routing for brokers.
- VGTTS warm-cache for broker voice.
- 3-branch dialogue pattern (archived/active-ready/active/none).
- `Plugin.Awake`-ordered StoryMission registration (critical — Option C just extends this).
- Cross-save persistence of VGAnima state.

Once A is proven stable, C adds: JSON schema, validator, generator (LLM client), persistent store. The dialogue layer is unchanged.

## Open Questions For When We Get Here

- **LLM call timing.** Do we generate a mission when a bar patron is converted to a broker (once per broker lifetime), or lazily on first interaction? Lazy is cheaper but adds latency to the first talk.
- **Trigger safety.** Which `MissionTrigger` values are safe for LLM to use? Probably `BountyTargetKilled`, `MinedOre`, `SalvagedItem`, `DockedWithSpaceStation`, `ItemCollected`, `UnitDestroyed`. Probably NOT the plot-critical ones like `CompletePatrol` (collision with Canisec) or `FastTravelTalkToNPC` (wired to a specific NPC).
- **Reward balance.** LLM must not trivially print unlimited credits. Clamp ranges and cross-check against game economy constants (`GameMath.GetCreditsValue(...)`).
- **Turn-in locations.** Does the broker always turn-in at its own station, or can the LLM send the player to another system? Multi-system pulls in `SectorMapData.current` lookups — more surface to validate.
- **Mission chains.** Does the LLM get to chain missions via the `Rewards.StoryMission` reward type? Powerful but needs the full JSON for the next mission up-front (or two-stage LLM call).
- **Deduplication.** Two brokers shouldn't independently invent the "deliver 5 ore to X" mission on the same save. Need either a per-save uniqueness check or a broader theme-assignment layer.
- **LLM access.** What model? Local (Ollama) to keep it air-gapped? Or HTTP to user-configured endpoint? Config surface matters.
