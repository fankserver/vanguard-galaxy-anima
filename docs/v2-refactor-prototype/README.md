# v2 mission schema refactor — prototype code

## Status
Not wired up. Committed artifacts so far: `AccessibleDestinationsBuilder` (commit `f71e100`). Everything else below is DESIGN, not code — the prototype from session 2026-04-22 was written, partially integrated, then reverted because the cascade (ArchetypeInferrer rewrite, SidecarSerializationBinder allowlist, 800-line BarRefreshPatches prompt section, ~10 test files) was deeper than one session could ship cleanly without leaving a broken build overnight.

## Decisions (confirmed by user)
- **Q1 — Destination picking**: Plugin enumerates accessible stations (0-1 jumpgate hops, cap 8, ranked by jumps asc / same-faction-as-broker desc / name asc). LLM picks by short `dest_N` id. Validator rejects unknown ids.
- **Q2 — Migration**: Hard cut v1 → v2. No version shuffling. In-flight v1 missions get orphan-purged on first v2 load. We're in dev; breaking changes are fine.

## Intent vocabulary (7 intents)
Each intent = one complete step. Step JSON shape: `{ "intent": "<name>", ...params }`. NOT `{ "objectives": [...] }`. One intent per step makes "2 POIs in one step" structurally impossible.

| Intent | LLM params | Plugin mechanics |
|--------|------------|------------------|
| `clear_combat_site` | enemy_faction, description | `AddCombat(faction)` + `AddGuards(CreateUnitPayload(scale, Combat))` → `KillEnemies(totalUnitCount)`; `step.dynamicPointOfInterest = combat` |
| `gather_ore` | required_amount (1..50), description | `AddMiningPoi(sourceFaction)` → `CollectItemTypes(Ore, amount)`; `step.dynamicPointOfInterest = mining` |
| `gather_salvage` | required_amount (1..50), description | `AddDerelictFleetPoi(sourceFaction)` → `CollectItemTypes(Salvage, amount)`; `step.dynamicPointOfInterest = salvage` |
| `defended_gather_ore` | required_amount (1..50), guards_faction, description | `AddMiningPoi` + `AddGuards` + `dangerLevel` → `CollectItemTypes(Ore, amount)` |
| `defended_gather_salvage` | required_amount (1..50), guards_faction, description | `AddDerelictFleetPoi` + `AddGuards` + `dangerLevel` → `CollectItemTypes(Salvage, amount)` |
| `deliver_to_station` | destination_id, description | `TravelToPOI(destination.guid)`; completes when player docks |
| `haul_goods` | required_amount (1..20), destination_id, description | `CollectItemTypes(TradeGoods, amount)` + `TravelToPOI(destination.guid)` (two objectives, one step) |

**Dropped for v2**: `escort_to_station`. Vanilla's escort pattern needs 4 things lining up (`CreateFixedPayload` + `playerFriendly` + `AddGuards` + `CreateEscortLocation` + `EscortUnitCargoUnloaded` trigger). Shipping 7 that all work beats shipping 8 where the 8th is flaky. Revisit once v2 is stable.

**Key primitive**: `TravelToPOI.targetPOI = station.guid` (decomp line 45969) — no `MissionTrigger` enum plumbing needed, no global-dock ambiguity. Vanilla already has this class.

## Intent → archetype mapping (for `mission_guidance.forbidden_archetypes`)
An intent is blocked if ANY of its archetypes appears in forbidden_archetypes:

- `clear_combat_site` → [combat]
- `gather_ore` → [gather]
- `gather_salvage` → [salvage]
- `defended_gather_ore` → [combat, gather]
- `defended_gather_salvage` → [combat, salvage]
- `deliver_to_station` → [deliver]
- `haul_goods` → [gather, deliver]

## Files to rewrite (in place, no V2 suffix — one clean break)
1. `VGAnima/Llm/LlmMissionBlock.cs` — flat intent record hierarchy
2. `VGAnima/Llm/IntentWhitelist.cs` — **new**, 7 intents + `Archetypes()` helper
3. `VGAnima/Llm/MissionBlockValidator.cs` — intent parser, accepts `accessibleDestinations`
4. `VGAnima/Missions/MissionFactoryFromJson.cs` — intent dispatcher, takes `accessibleDestinations`
5. `VGAnima/Missions/ArchetypeInferrer.cs` — switch on intent type
6. `VGAnima/Persistence/VGAnimaSidecarSerializationBinder.cs` — allowlist new intent types, remove old objective types
7. `VGAnima/Patches/BarRefreshPatches.cs` — new `BuildSystemPrompt`, wire `AccessibleDestinationsBuilder.Build` + pass list to validator/factory, add `accessible_destinations` section to `LlmContext`
8. `VGAnima/Llm/LlmContext.cs` — add `AccessibleDestinations` field (optional, null-ignored)
9. `VGAnima/Llm/ContextGatherer.cs` — accept + thread `accessibleDestinations` parameter

## Files to delete
- `VGAnima/Llm/TriggerWhitelist.cs` — LLM no longer names triggers (plugin picks `TravelToPOI` or `DockedWithSpaceStation` internally)
- `VGAnima/Llm/ObjectiveTypeWhitelist.cs` — replaced by `IntentWhitelist`
- `VGAnima/Llm/ItemCategoryWhitelist.cs` — plugin picks Ore/Salvage/TradeGoods from intent

## Files untouched
- Rewards schema (Credits/XP/Reputation/Item unchanged, already 1:1 with vanilla)
- `FactionWhitelist`, `ItemRewardKindWhitelist`, `RewardTypeWhitelist`
- Journal, BarEcosystem, PurchaseProfile, StationCondition builders
- Stage-direction config
- Persistence (except the serialization binder's allowlist)

## Persistence hard-cut strategy
Bump `SidecarSchema.CurrentVersion` to v2. On load, if sidecar version != current, run orphan purge on all VGAnima missions in that sidecar (leave vanilla state intact). Next load has a clean slate. Existing `OrphanPurger` already knows how to do this — just trigger it conditionally.

## Tests to rewrite (~10 files)
- `MissionBlockValidatorTests.cs` — rebuild from intent parsing instead of objective parsing
- `WhitelistsTests.cs` — drop Trigger/ObjectiveType/ItemCategory sections, add Intent
- `ItemRewardValidatorTests.cs` — keep; mission shape in helper changes from objectives array to single intent
- `ArchetypeInferrerTests.cs` — rewrite tests around intent types
- `MissionFactoryFromJson` tests (if any) — rewrite for new Build signature
- `ResponseValidatorTests.cs` — expected JSON shapes change
- `ContextGathererTests.cs` — add accessible_destinations expectation

## Known risks / open questions flagged by advisor
1. Prompt bloat: current prompt already ~16.5k chars. 7 intent descriptions will add pressure. Need to SHORTEN existing rules (journal / archetype / ecosystem) since the new schema makes many violations structurally impossible — less need to say "don't do X".
2. Layer B (bar salesman purchase hook on `ItemSaleInfo.ButtonPurchase`) is still unverified in live play. Confirm by buying from Bailey Tucker at the bar and looking for `BarPurchase: VGAnima_...Counter++` in `LogOutput.log`. Do this BEFORE the refactor lands so any bug we hit later is unambiguously v2, not a pre-existing Layer B issue.
3. Reward scaling confirmed normal: XP=1 on a level-12 mission when player is level 18 is vanilla's `GetExperienceRewardValue` over-level penalty (6 levels above station = 0.00001 multiplier × 4 level-scaling = 0.00004 → `CeilToInt` = 1). Not a bug.

## Commit chunk plan (~4 commits for next session)
1. **Schema + validator + factory + ArchetypeInferrer + SerializationBinder** (atomic — all interlocking types)
2. **BarRefreshPatches new prompt + wire `AccessibleDestinationsBuilder` + context/gatherer plumbing**
3. **Delete dead whitelists (Trigger/ObjectiveType/ItemCategory) + `TestStoryMissions` legacy**
4. **Persistence hard-cut (bump `SidecarSchema.CurrentVersion`, orphan-purge on version mismatch)**

Deploy + live test after commit 4. Expected wall time: ~3-4 focused hours.
