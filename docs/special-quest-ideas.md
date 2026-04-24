# Special-Quest Ideas

Backlog of rare, high-flavor broker missions that sit outside the normal
archetype pool (combat / mining / salvage / trade / deliver / escort). Each entry
has a small probability of firing per broker roll — think "once in a
while, the broker pitches something weird that's memorable." The normal
mission pool stays the default; these are the exceptions.

None of these are implemented yet. This file is a design sketch. Treat
it as a menu we pick from when designing a future `vganima/mission/v2`
schema or an opt-in extension point.

---

## Why not blend these into the normal pool?

- **Narrative weight.** "Buy one crystal for 500,000cr" only works if it's
  rare enough to feel special. Make it common and it becomes a chore.
- **Mechanics scope.** These leverage edge-case vanilla systems (crystal
  drops, blueprint brokers, Umbral tools, dog-tag currency) that the
  normal LLM prompt doesn't know about. Each one needs a targeted prompt
  variant + factory wiring.
- **Frequency control.** Instead of 1/5 of brokers being a "blueprint
  dealer" (overkill), gate the special quests behind a sub-roll — e.g.
  a broker has a 2% chance of being a special-quest variant, and within
  that 2% the variant type is picked from a weighted table.

Proposed top-level probability knob: `Cfg.SpecialMissionChance = 0.02`
(2% of LLM-dispatched brokers are special). Within the special pool,
each variant has its own sub-weight in the table below.

---

## Variant Menu

### Transactional variants — broker wants a THING, pays huge

| # | Name | Mechanic hook | What the broker asks | Suggested rarity |
|---|---|---|---|---|
| 1 | **Junk Fetish** | `ItemCategory.Junk` raw material (distinct from Salvage) | "I'm paying a fortune for one specific piece of junk — bring me any item of `ItemCategory.Junk`, any tier, and I'll hand you a payout twenty times what it's worth on the market." | very rare (0.2 %) |
| 2 | **Crystal Commission** | `Asteroid.SpawnInnerCoreOre` — crystals drop from deep-ore mining at level 12+ (0.1–2%) | "My engineer needs a BallisticCrystal / EnergyCrystal / KineticCrystal / ModuleCrystal. Bring me just one and I'll make it worth your week." | very rare (0.2 %) |
| 3 | **Blueprint Hustle** | `Salesman.SalesmanSpaceShipPNG` — 10% patron chance sells `SpaceShipPng` blueprint at 251×cost | "Word is you've got a line on rare ship schematics. Bring me ANY blueprint you've acquired and I'll double the market price." | very rare (0.2 %) |
| 4 | **Dog Tag Dossier** | `DogTagItem` — looted from faction-specific kills, normally converted to faction currency | "I need N dog tags from [hostile faction] — don't ask why. Pay's high." | rare (0.5 %) |

### Event-flavor variants — the broker points at a vanilla niche system

| # | Name | Mechanic hook | Shape | Suggested rarity |
|---|---|---|---|---|
| 5 | **Distress Relay** | `DistressCombat` — random allied-ship-under-attack travel event | Broker hints at a specific allied signal location; first to reach it gets the rescue reward. (Essentially nudging the player toward an existing vanilla event.) | rare (1 %) |
| 6 | **Derelict Fleet Tip** | `DerelictFleet` SystemStoryteller — generates pocket-system with salvage stations + BonusSkillPointTemplate | "Heard rumors of a drifting graveyard past the gate at X. I'll pay for proof you were there." Unlocks the pocket. | very rare (0.3 %) |
| 7 | **Pirate Hideout Leak** | `PirateHideout` SystemStoryteller — Marauder-controlled pocket system | "I know where the Corsair inner sanctum is. Clear it and the loot's yours — I just want the coordinates confirmed." | very rare (0.3 %) |
| 8 | **Crew Pod Hunt** | `CrewPod` drops (Leadership-gated, 0–25% per kill) | "Kill pirates in [system], grab whatever crew pods eject. I'll hire them off you, no questions." | rare (0.8 %) |
| 9 | **Industrial Siege** | `IndustrialOutpost` defense waves | "Our supply station is about to get hit. Hold it through N waves and I unlock their production line as a personal trader for you." | rare (0.5 %) |

### Specialized archetype variants — still combat/mining/salvage, but weird

| # | Name | Mechanic hook | Shape | Suggested rarity |
|---|---|---|---|---|
| 10 | **Bounty Lord** | `BountyBoard` extreme tier at bounty-rank 50+ | Named high-tier target with signature ship + crew as loot. Triggered only when player has `BountyRank ≥ 50`. | rare (endgame) |
| 11 | **Treasure Cascade** | `Asteroid.SpawnInnerCoreOre` + Mining Treasure skilltree (sandbox) | "There's a fat roid in [system] — rumor is it's got a loot cache. Crack it before the NPC miner does." Competes with an NPC. | very rare (0.3 %) |
| 12 | **Proxy War** | `FactionSkirmish` pocket system | "Run supplies to BOTH sides of this skirmish — they'll never know. Losing faction drops experimental weapons." | rare (0.5 %) |
| 13 | **Umbral Whisper** | `UmbralHackingTool` / `UmbralTransponderItem` — endgame Umbral progression | Only fires if player is on the Umbral questline. Asks for one specific Umbral hack action against a conquest station. | once per playthrough |

### Meta variants — break the fourth wall carefully

| # | Name | Mechanic hook | Shape | Suggested rarity |
|---|---|---|---|---|
| 14 | **Reputation Launderer** | Reputation penalties on hostile kills | "I'll pay to take a reputation hit OFF your record. Bring me [item] and I'll grease palms." Reward: positive rep bump with a single faction. | very rare (0.3 %) |
| 15 | **Defector's Choice** | Crew-pod rescue + faction-switching | Rescued crew member from an enemy faction offers to feed intel / trigger mutiny on their former faction's ships. Unlocks faction-specific dialogue tags. | once per playthrough |

---

## Rarity roll shape

Proposed structure when this ships:

```
broker injection fires
  ↓
MissionChance roll (existing) decides if a broker appears at all
  ↓
if appears:
    SpecialMissionChance roll (e.g. 0.02) decides if it's a special variant
    ↓
    if special:
        weighted pick from the variant table above
        LLM gets a VARIANT-SPECIFIC prompt (different system prompt,
        different reward scaling, different whitelists)
    else:
        normal pool (combat / mining / salvage / trade / deliver / escort)
```

Each variant has its own prompt addendum + factory code path. Variants
MUST declare what vanilla mechanic they lean on so future-you can keep
them working when vanilla updates.

---

## Rules of thumb for adding a new variant

1. **Must point at a real vanilla mechanic.** If the game doesn't have
   a hook to lean on, the variant is fantasy, not design. Every entry
   in the table above cites a concrete `Source.X.Y` path.
2. **Payout scales with rarity.** 0.2 % variants can pay 10-20× normal;
   1 % variants pay 3-5×; anything at once-per-playthrough can hand the
   player a ship / permanent skill / unique crew member.
3. **Reward clamps still bound economy abuse.** Even rare payouts go
   through the validator's hard limits. The "huge payout" feel comes
   from the LLM declaring a narrative matching the payout, not from
   actually minting 10,000,000 credits.
4. **No variant should block the main game.** Every special quest must
   be abandonable without soft-locking progression. If the variant
   unlocks a pocket system, the player can still just leave.
5. **One special at a time per player.** Even if two brokers roll the
   same variant, at most one of them offers it — otherwise the "weird
   one-off encounter" feeling dies.

---

## Implementation cost estimate

(Rough, for when we pick a variant to actually build.)

- **Transactional variants (1-4):** each ~200-400 LOC. New validator
  branch, new prompt addendum, payout math. Lowest risk; no new
  spawns needed.
- **Event-flavor variants (5-9):** each ~500-800 LOC. Need to
  understand the vanilla storyteller / pocket-system mechanism to
  hook into it. Medium risk.
- **Specialized archetype (10-13):** each ~300-600 LOC. Reuses
  existing `ClearPoi` / `CollectItemTypes` machinery but with
  altered parameters / target set.
- **Meta variants (14-15):** each ~400-700 LOC. Faction rep + crew
  rescue both touch shared systems; coordination cost.

None of this is in any current plan. File against the v2-mission
follow-up epic once we decide which variants to pursue.
