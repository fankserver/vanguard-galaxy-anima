# Vanguard Galaxy Decompilation Survey

> Historical snapshot, not current CLR bindings. Personnel/reward names below include retired types. See [current-game compatibility](current-game-compatibility.md) and inspect the current assembly before coding.

A catalog of canonical identifiers, enum values, and mechanic constants drawn from the decompiled `Assembly-CSharp.dll` at `/tmp/decomp/`. Feeds the VGAnima player-journal feature with machine-readable counterparts to the [wiki survey](./vanguard-galaxy-wiki-survey.md) (display names + narrative framing).

**Survey date:** 2026-04-22. **Game build:** decompilation of current VG build in `/tmp/decomp/`. Citations reference decomp path + line as `Source.Foo/Bar.cs:NN`.

**What this doc does not cover:** `Resources/SpaceShips/*.asset` (ship ScriptableObjects loaded at runtime per `Behaviour.Unit/SpaceShip.cs:1140-1147`) and `Resources/Languages/*.ini` (translation values for `@FactionName*`, `@{factionId}{rank}`, etc.). The decomp gives key patterns; the values live in Unity asset bundles.

---

## 1. Factions

### Canonical identifier ↔ wiki display name

Each faction is a `class Foo : Faction` in `Source.Galaxy.Factions/`; `identifier = GetType().Name` (`Source.Galaxy/Faction.cs:90`). The display name is a translation key `@FactionName{identifier}` (`Source.Galaxy/Faction.cs:91`). `Faction.Get(identifier)` is the canonical lookup.

| Identifier | Wiki display name | Category | Evidence |
|---|---|---|---|
| `Player` | (player) | Special | `Source.Galaxy/Faction.cs:17` |
| `Red` | Kolyatov Collective | Major | `Source.Galaxy.Factions/Red.cs:28` → `StationKolyatov.GenerateKolyatovStationName()`; `Source.Dialogues.Content/ConquestMissions.cs:225` "good Kolyatov friend is …" uses `Faction.red.identifier` |
| `Blue` | Stellar Industries | Major | `Source.Galaxy.Factions/Blue.cs:28` → `StationStellar.GenerateStellarStationName()` |
| `Gold` | Luminate Combine | Major | `Source.Galaxy.Factions/Gold.cs:28` → `StationLuminate.GenerateLuminateStationName()` |
| `MiningGuild` | Mindus Holdings | Minor | `Source.Galaxy.Factions/MiningGuild.cs` |
| `SalvageGuild` | Steel Vultures | Minor | `Source.Galaxy.Factions/SalvageGuild.cs:31` → `StationSalvage.GenerateVulturesStationName()` |
| `TradingGuild` | Intertrade Network | Pacifist | `Source.Galaxy.Factions/TradingGuild.cs` |
| `PoliceGuild` | Canisec | Pacifist | `Source.Galaxy.Factions/PoliceGuild.cs`; patrol missions use `Faction.policeGuild` (`Source.MissionSystem/PatrolMission.cs:146`) |
| `BountyGuild` | Orsanon Security | Pacifist | `Source.Galaxy.Factions/BountyGuild.cs`; bounties use `Faction.bountyGuild` (`Source.Galaxy.POI.Station/BountyBoard.cs:134`) |
| `IndustrialGuild` | Forge Industries | Pacifist | `Source.Galaxy.Factions/IndustrialGuild.cs`; industry ops use `Faction.industrialGuild` (`Source.MissionSystem/IndustryMission.cs:126`) |
| `MercenaryGuild` | Omnitac Agency | Pacifist | `Source.Galaxy.Factions/MercenaryGuild.cs`; hire cost rep in `Source.Player/GamePlayer.cs:482` |
| `Stranded` | Stranded | Minor | `Source.Galaxy.Factions/Stranded.cs` |
| `Darkspacers` | Darkspace Compact | Minor | `Source.Galaxy.Factions/Darkspacers.cs`; owns Penumbra/Lucifer (`Source.Simulation.World/ConquestWorld.cs:403-410`) |
| `Smugglers` | Void Drifters | Minor | `Source.Galaxy.Factions/Smugglers.cs` (only Courier/BountyHunt/Escort/Trade, no mining/salvage) |
| `Puppeteers` | Umbral Reach (dynamic: `@FactionNamePuppeteers` → `@FactionNamePuppeteers2` once `Conquest` storyteller active or `PuppeteersNameChange` flag set) | Minor | `Source.Galaxy.Factions/Puppeteers.cs:22-36` |
| `Marauders` | Corsair Syndicate | Antagonist | `Source.Simulation.World/ConquestWorld.cs:378` names combat stations `"Corsair Hideout"` when `f == Faction.marauders` |
| `Fanatics` | Meridia's Chosen | Antagonist | `Source.Simulation.World/ConquestWorld.cs:378` names stations `"Meridian Hideout"` for `Faction.fanatics` |
| `Amalgam` | — (internal/unused) | Excluded | `Source.Galaxy.Factions/Amalgam.cs` — `offersMissionsForShip=false`, no `missionTypes`, `minShipVariety=1`. Excluded from `Faction.RandomEnemyFaction` (`Source.Galaxy/Faction.cs:257`). Not on wiki. |
| `HolyRadicals` | — (internal/unused) | Excluded | `Source.Galaxy.Factions/HolyRadicals.cs` — same shape as Amalgam. Excluded at `Source.Galaxy/Faction.cs:258`. Not on wiki. |

### Faction config fields

Per faction (`Source.Galaxy/Faction.cs:55-73`):

- `allowCrossFactionShipUse` (bool, default true) — whether allies can fly this faction's hulls. False for `Red`, `Blue`, `Gold`, `Marauders`, `PoliceGuild`, `Smugglers`, `Darkspacers`, `Amalgam`, `Fanatics`, `HolyRadicals`, `Puppeteers`.
- `minShipVariety` (int, default 4). 1 for `PoliceGuild`, `Fanatics`, `Darkspacers`, `Amalgam`, `HolyRadicals`, `Puppeteers`. 2 for `Marauders`.
- `conquestColor` (Color, default clear) — used to render Conquest territory. Values: Red `#FF0400`, Blue `#0004FF`, Gold `#FFC61E`, MiningGuild `#3FAD1D`, SalvageGuild `#FFA830`, Stranded `#689900`, Marauders `#AF0017`, Fanatics `#7F00CE`, Darkspacers `boringGrey`.
- `missionTypes` (List<string>) — IDs of `MissionGenerator` subclasses this faction offers. See §3.

### Conquest faction sets (`Source.Simulation.Story/Conquest.cs:43-70`)

```
conquestFactions           = { Red, Blue, Gold, MiningGuild, SalvageGuild,
                               Marauders, Fanatics, Darkspacers, Stranded }
autoPopulatingFactions     = { Red, Blue, Gold, Darkspacers }
playerPopulatingFactions   = { MiningGuild, SalvageGuild, Marauders, Stranded }
corporations (Faction.cs:55) = { Red, Blue, Gold }
```

Only the 9 `conquestFactions` participate in Conquest. `Puppeteers` participates via a separate `umbralContribution` counter on `Conquest` (`Source.Simulation.Story/Conquest.cs:78`; rank lookup at `Source.Galaxy/Faction.cs:217`).

---

## 2. Reputation thresholds and enemy cutoff

Per `Source.Util/ReputationLevelExtensions.cs:11-57`:

```
ReputationLevel        Threshold
AbsoluteThreat         -50000
Hated                  -30000
Despised               -15000
Hostile                 -5000
Wary                     -500
Neutral                     0
Cordial                  1500
Friendly                 5000
Respected               15000
Distinguished           30000
Exalted                 50000
```

Enum order: `Source.Galaxy/ReputationLevel.cs:4-15`.

### Enemy cutoff — wiki-wrong

The `IsEnemy` threshold is **−500, not −5000**. `Source.Galaxy/FactionData.cs:27` defines `public const float foeReputation = -500f;` and `FactionData.cs:71` returns `GetReputation(self, other) < -500f`. This aligns with `ReputationLevel.Wary`, not `ReputationLevel.Hostile`. The wiki's Reputation page names "Hostile" loosely — the actual hostile-attack threshold is the `Wary` floor. `Source.Simulation.Story/Conquest.cs:90` also caps max earnable rep at `Distinguished` (30 000) inside Conquest.

### Per-level mechanics (all from `ReputationLevelExtensions.cs`)

| Level | ShopDiscount | RepairCost | RepairSpeed | MissionRewardMult | Board refresh (s) | Bonus missions |
|---|---|---|---|---|---|---|
| Cordial | 2% | 0% | 0.9× | +5% | 60 | 0 |
| Friendly | 4% | 20% off | 0.6× | +10% | 55 | 1 |
| Respected | 6% | 40% off | 0.4× | +20% | 50 | 2 |
| Distinguished | 8% | 60% off | 0.3× | +30% | 45 | 2 |
| Exalted | 10% | 80% off | 0.2× | +40% | 40 | 3 |

Shop refresh at `Friendly` (1 token), `Distinguished`+ (2 tokens) (`ReputationLevelExtensions.cs:240`). Board refresh unlocks at `Cordial` (`.cs:278`).

### Conquest rank thresholds (`Source.Util/ConquestRankExtension.cs:13-43`)

```
Rank         Contribution    MissionCommendBonus   CreditBonus   RepBonus   FleetStrengthBonus
None                 0       0                     0             0          0
Rank1               10      +10%                  +4%           +3%        +10%
Rank2              150      +20%                 +10%           +6%        +20%
Rank3              450      +40%                 +16%          +10%        +40%
Rank4             1000      +60%                 +24%          +15%        +60%
Rank5             2500      +80%                 +32%          +20%        +80%
Rank6             4500     +100%                 +40%          +25%       +100%
MaxConquestContribution = 4500 (ConquestRankExtension.cs:11)
DestroyerRank = Rank2 (.cs:179) — unlocks faction-specific Destroyer ships
```

Translation key per faction: `@{factionIdentifier}{rank}` (`ConquestRankExtension.cs:51`). Example keys: `@SalvageGuildRank6` → "Scraplord", `@PuppeteersRank6` → "The Hand of Umbral", etc. Values live in `Resources/Languages/en-US.ini` (not decompiled). `Puppeteers` rank uses `Conquest.umbralContribution` instead of faction standing; commendation bonus is ×0.25 (`.cs:108`); has no fleet-strength bonus and cannot unlock Destroyer (`.cs:129-174`).

### Reputation deltas — wiki-wrong

The wiki's "−200 small / −800 large per ship destroyed" figure is **incorrect**. Actual formula in `Behaviour.Managers/LootManager.cs:54-69`:

```csharp
int num = Mathf.CeilToInt(unit.baseExperienceReward * 0.5f);
// ×20 penalty if faction wasn't already hostile
if (unit.faction.GetReputation(Faction.player) > -500f) num *= 20;
unit.faction.ChangePlayerReputation(-num);
// nearby SystemMapData faction that hates the victim gets +(num*0.2) rep
if (nearby.IsEnemy(unit.faction) && -500 < nearbyRep < 1500)
    nearby.ChangePlayerReputation(ceil(num * 0.2));
```

Preconditions to skip the penalty: `unit.unitData.noReputationLoss == true`, or `GamePlayer.current.hasUmbralTransponder == true` (Decoy Transponder is active).

Other rep-bleed call sites:
- **Stealing tractored items owned by another faction**: −1 per pickup (`Behaviour.Tractoring/TractorBeam.cs:141`). `Register.AddCounter("OreStolen", amount)` on ore theft.
- **Hiring from MercenaryGuild**: adds `rarity.GetRarityCostMultiplier()` rep on hire (`Source.Player/GamePlayer.cs:482`).
- **Station repair (personal hangar)**: +20 station-faction rep on full repair (`Behaviour.UI.Spacestation/SpaceStationInterior.cs:202`).

### Mission rep rewards (fixed-number scales)

- **Standard board mission**: `random.Choose([200, 250, 300, 350])` rep to source faction (`Source.MissionSystem/MissionGenerator.cs:158`).
- **Patrol mission**: +125 rep to `policeGuild` + +250 rep to the "aid-request" faction (`Source.MissionSystem/PatrolMission.cs:144-153`).
- **Bounty mission**: +175 to `bountyGuild` + +350 to the victim faction (`Source.Galaxy.POI.Station/BountyBoard.cs:132-141`).
- **Industry mission**: +325 to `industrialGuild` (`Source.MissionSystem/IndustryMission.cs:124`).
- **Umbral mission**: `random.Choose([400, 500, 600, 700])` to `Puppeteers` (`Source.MissionSystem.Generator.Umbral/UmbralMissionGenerator.cs:52`).
- **Enemy-source missions** (rep < −500): rewards cleared, rep reward ×4 (`Source.MissionSystem/MissionGenerator.cs:152-157`).

---

## 3. Mission archetypes

All concrete procedural generators live under `Source.MissionSystem.Generator/` (vanilla 14) and `Source.MissionSystem.Generator.Umbral/` (3). `MissionGenerator.Get(id)` resolves them via reflection: `Type.GetType("Source.MissionSystem.Generator." + id)` (`Source.MissionSystem/MissionGenerator.cs:230`).

| Generator identifier | GameplayType | canBeIdled | N factions | Offered by |
|---|---|---|---|---|
| `BountyHunt` | Combat | true | 11 | Red, Blue, Gold, Marauders, Stranded, Darkspacers, Smugglers, TradingGuild, PoliceGuild, BountyGuild, MercenaryGuild |
| `ClearAsteroidField` | Combat | true | 11 | Red, Blue, Gold, Marauders, Stranded, Darkspacers, MiningGuild, IndustrialGuild, PoliceGuild, BountyGuild, MercenaryGuild |
| `ClearSalvageField` | Combat | true | 11 | Red, Blue, Gold, Marauders, Stranded, Darkspacers, SalvageGuild, IndustrialGuild, PoliceGuild, BountyGuild, MercenaryGuild |
| `Courier` | Cargo | true | 7 | Red, Blue, Gold, Stranded, Darkspacers, Smugglers, TradingGuild |
| `DeliverCraftedGoods` | Mining | false | 5 | Red, Blue, Gold, Stranded, Darkspacers |
| `EscortShip` | Combat | false | 8 | Red, Blue, Gold, Smugglers, TradingGuild, BountyGuild, PoliceGuild, MercenaryGuild |
| `HelpMiner` | Mining | true | 7 | Red, Blue, Gold, Stranded, Darkspacers, MiningGuild, IndustrialGuild |
| `MineOre` | Mining | true | 8 | Red, Blue, Gold, Marauders, Stranded, Darkspacers, MiningGuild, IndustrialGuild |
| `OreSamples` | Mining | true | 7 | Red, Blue, Gold, Stranded, Darkspacers, MiningGuild, IndustrialGuild |
| `SalvageSamples` | Salvage | true | 6 | Red, Blue, Gold, Stranded, Darkspacers, SalvageGuild |
| `SalvageWreck` | Salvage | true | 7 | Red, Blue, Gold, Marauders, Stranded, Darkspacers, SalvageGuild |
| `StationBattle` | Combat | false | 7 | Red, Blue, Gold, Marauders, PoliceGuild, BountyGuild, MercenaryGuild |
| `TradeMaterials` | Cargo | false | 1 | TradingGuild |
| `TradeTerminal` | Cargo | false | 13 | all except TradingGuild and `offersMissionsForShip=false` set (Amalgam, HolyRadicals, Fanatics, Puppeteers) |
| `Umbral.BountyHunt` | Combat | false | — | Puppeteers Umbral pool (`Puppeteers.cs:13-18`) |
| `Umbral.MiningDeadDrop` | Mining | false | — | Puppeteers Umbral pool |
| `Umbral.SalvageDeadDrop` | Salvage | false | — | Puppeteers Umbral pool |

Notes:
- TradingGuild has **no TradeTerminal generator** — it uses the more specialized `TradeMaterials` instead.
- `SalvageGuild.missionTypes` has only 4 generators (SalvageWreck, SalvageSamples, TradeTerminal, ClearSalvageField) — focused on salvage + terminal.
- `MercenaryGuild` offers 6 generators (BountyHunt, ClearAsteroid, ClearSalvage, TradeTerminal, StationBattle, EscortShip) — combat focus.
- Hostile factions `Amalgam`, `HolyRadicals`, `Fanatics`, `Puppeteers` have `offersMissionsForShip=false` and no `missionTypes` → no board missions. `Marauders` has 7 generators (BountyHunt, ClearAsteroid, ClearSalvage, MineOre, SalvageWreck, TradeTerminal, StationBattle) — pirate-shaped.

### Non-board mission classes (`Source.MissionSystem/`)

- **`Mission`** — base class (`Mission.cs`).
- **`BountyMission`** — `BountyBoard.GenerateBounties` at Orsanon stations (`BountyMission.cs`; `BountyBoard.cs:132`). Multi-wave ending in a named captain; `bountyLevel` 0/1/2 = Normal/Elevated/Extreme.
- **`PatrolMission`** — 5 waves, picked from `patrolFactions = {Gold, Blue, Red, MiningGuild, TradingGuild}` (`Source.MissionSystem/PatrolMission.cs:24`). `patrolLevel` 0/1/2 = difficulty; Extreme + wave%2==0 → `patrolRank++`.
- **`IndustryMission`** — station-defense/crafting, `industryLevel` 0/1/2 (`Source.MissionSystem/IndustryMission.cs`). Extreme + wave%2==0 → `industryRank++`.
- **`StoryMission`** — fixed narrative nodes, statically registered at type-load in `Source.MissionSystem/StoryMission.cs:24-32`:
  ```csharp
  static StoryMission() {
      new TutorialMissions();      // Source.MissionSystem.Story/TutorialMissions.cs
      new UmbralMissions();        // UmbralMissions.cs
      new SkilltreeMissions();     // SkilltreeMissions.cs
      new SideMissions();          // SideMissions.cs
      new ConquestMissions();      // ConquestMissions.cs
  }
  ```
  Registered via `StoryMission.Add(...)`; lookup via `StoryMission.Get(player, id)`. This is the registry VGAnima already hooks.

### MissionDifficulty enum (`Source.MissionSystem/MissionDifficulty.cs:4-13`)

```
Easy, Normal, Hard, Skull, Insane, Faction, Tutorial, Story
```

The five main tiers are rolled by `MissionDifficultyExtension.GetMissionDifficulty(level, …, random)` (called in `MissionGenerator.cs:236`). Reward rarity picked by difficulty (`MissionGenerator.cs:60-67`):
- Easy → 50% Standard / 50% Enhanced
- Normal → Enhanced
- Hard → 50% Enhanced / 50% HighGrade
- Skull → 90% HighGrade / 10% Exotic
- Insane → 70% HighGrade / 30% Exotic

---

## 4. MissionTrigger enum (full 113-value dump)

`Source.MissionSystem/MissionTrigger.cs:3-113`. Triggered via `MissionObjective.Trigger(MissionTrigger.X, payload)` from ~40 call sites.

```
// Core ambient-gameplay events
None, ItemCollected, UnitDestroyed, UnitProtected, MinedOre, SalvagedItem,
TakeDamage, TargetAsteroid, TargetWreckage, ArrivedAtSpaceStation,
DockedWithSpaceStation, PersonalHangarRepair, InstallMiningLaser,
InstallCombatTurret, InstallSalvageLaser, MoveCamera, MoveToArea,
LootContainerOpened, BountyTargetKilled, CompleteDynamicMission,
VisitUniqueSystem, CombatStationDestroyed, PatrolWaveFinished,
PocketSystemSkirmishVictory, EscortUnitCargoUnloaded, CraftItem,
SalvagedModule, EquipDroneShip, FriendlyRepaired, CompletePatrol,
IndustryBoardCraft, FindCargoWithScanner, PlaceTracker,
DecoyTransponderUsed,

// Tutorial chain
Tutorial2Welcome, Tutorial3Complete, Tutorial4Complete,
UnlockJumpgateOrbitan, Tutorial5Welcome, UnlockJumpgateBalam,
Tutorial6Welcome, SalvageAICore, Tutorial7Complete, CraftAICore,
Tutorial8Complete, Tutorial9Complete, Tutorial10CombatComplete,
TutorialJumpgateStructure, TutorialJumpgatePlates,
TutorialJumpgateConduit, TutorialJumpgateBeacon, Tutorial10Complete,
TutorialLastJump,

// Ambient combat outcome signals
MinerChasedOff, SalvagerChasedOff,

// Umbral questline
InteractWithUmbralBeacon, UmbralLuminatePrisoner4,
Umbral4LuminatePrisonerRelease, Umbral5KolyatovWelcome,
Umbral5KolyatovAttack, UmbralSteelVultureComputer, Umbral7StellarWelcome,
Umbral7StellarSkirmish, Umbral7StellarComplete, Umbral10Smuggler,
Umbral11Smuggler, Umbral12Stellar, Umbral13Smuggler,
Umbral14SmugglerComplete, Umbral14SmugglerFailed, Umbral15Darkspacers,
Umbral16Smuggler, Umbral18Stellar, Umbral20Stellar, Umbral21Umbral,
Umbral22, Umbral22Failed,

// Skill/fast-travel/merc intro
FastTravelTalkToNPC, SkillTier2TalkToNPC, MercIntroEmbassyTalkToNPC,

// Conquest embassy & HQ chain
ConquestCanisec1, ConquestStellarEmbassy, CSHQIntro, CS2, CS3,
ConquestLuminateEmbassy, CLHQIntro, CL2, CL3, ConquestKolyatovEmbassy,
CKHQIntro, CK2, CK3, EarnCombatStrengthForFaction, UmbralStationInfected,
MissionBoardOpenedWithUmbral, CU1TalktoNPC, CU3TalktoNPC, CU4TalktoNPC,
CU5TalktoNPC, CU6TalktoNPC, CU8TalktoNPC, CU9TalktoNPC, CU10TalktoNPC,
TradeTerminalProfit, CD1TalktoNPC, CD2TalktoNPC, CD3TalktoNPC
```

### Primary trigger call sites (journal-relevant subset)

| Trigger | Call site | Payload |
|---|---|---|
| `UnitDestroyed` | `Behaviour.Unit/AbstractUnit.cs:1499` | `(this, damageData)` tuple. `IdleStat.Kills++` at `.cs:1520` if `hitByPlayer`. |
| `MinedOre` | `Behaviour.Mining/Asteroid.cs:727` + `Behaviour.Equipment.Module/DroneBayModule.cs:619` | `itemType.item` |
| `SalvagedItem` | `Source.Data.Persistable/SalvageData.cs:544,661` + `DroneBayModule.cs:626` | `item` |
| `SalvagedModule` | `Source.Data.Persistable/SalvageData.cs:646` | module InventoryItemType |
| `TakeDamage` | `Behaviour.Unit/SpaceShip.cs:964` | `(int)damageData.totalDamageAmount` |
| `TargetAsteroid` | `Behaviour.Mining/Asteroid.cs:758` | — |
| `TargetWreckage` | `Behaviour.Salvage/SalvageContainer.cs:177` | — |
| `ItemCollected` | `Behaviour.Tractoring/TractorableItem.cs:156` + `DroneBayModule.cs:608` | `(TractorableItemData, SpaceShipData)` |
| `LootContainerOpened` | `Behaviour.Persistables/LootContainer.cs:133` | `LootContainerData` |
| `ArrivedAtSpaceStation` | `SpacestationExteriorManager.cs:207` | — |
| `DockedWithSpaceStation` | `Behaviour.UI.Spacestation/SpaceStationInterior.cs:191` | — |
| `VisitUniqueSystem` | `Source.Player/Register.cs:141` (fires once per system GUID via `Register.AddVisitedSystem`) | — |
| `CraftItem` | `Source.Mining/ForgeJob.cs:65,81` | `(key, value)` + `spaceStation` |
| `CompleteDynamicMission` | `Source.MissionSystem/Mission.cs:347` | — (on every completed procedural mission) |
| `CompletePatrol` | `Source.MissionSystem/PatrolMission.cs:190` | — |
| `IndustryBoardCraft` | `Source.Simulation.World.POI/IndustrialOutpost.cs:135` | count |
| `BountyTargetKilled` | via `unitData.deathTrigger` (see below) | — |
| `UmbralStationInfected` | `Behaviour.Unit.Parts/UmbralHackingBot.cs:41` | — |
| `PlaceTracker` | `Behaviour.Unit.Parts/UmbralTrackingBot.cs:76` | — |
| `FindCargoWithScanner` | `Behaviour.Unit.Parts/UmbralCargoScannerBot.cs:138` | — |
| `DecoyTransponderUsed` | `Behaviour.Item.Usable/UmbralTransponderItem.cs:36` | — |
| `MinerChasedOff` | `AbstractUnit.cs:1510` + `Source.SpaceShip.Auto/AmbientMinerActions.cs:47` | `unitData` |
| `SalvagerChasedOff` | `AbstractUnit.cs:1514` + `Source.SpaceShip.Auto/AmbientSalvagerActions.cs:51` | `unitData` |
| `EarnCombatStrengthForFaction` | `Source.MissionSystem.Rewards/ConquestStrength.cs:36` | `(amount, faction)` |
| `MissionBoardOpenedWithUmbral` | `Behaviour.UI/MissionBoard.cs:135` | — |
| `EquipDroneShip` | `Behaviour.Equipment.Module/DroneBayModule.cs:815` | — |
| `FriendlyRepaired` | `Behaviour.Equipment.Module/ShieldGeneratorModule.cs:128` | — |
| `InstallMiningLaser` / `InstallCombatTurret` / `InstallSalvageLaser` | `Behaviour.UI.Spacestation.Location/PersonalHangar.cs:603-611` | — |

### Dynamic death-triggers

`Source.Data/AbstractUnitData.deathTrigger` is an optional `MissionTrigger?` on individual NPC prefabs; when the unit dies `AbstractUnit.cs:1501-1503` fires `MissionObjective.Trigger(deathTrigger.Value, (this, damageData))`. This is how story-specific triggers like `BountyTargetKilled`, `Umbral5KolyatovAttack`, `PocketSystemSkirmishVictory` fire without having explicit call sites — the kill-trigger is baked into the enemy's ScriptableObject. **Implication for the journal:** patching `AbstractUnit` at `.cs:1499` covers all unit-death events, and the deathTrigger dispatch at `.cs:1503` covers all faction/story-specific kill milestones with zero extra patches.

### Dialogue triggers

Characters fire their registered `MissionTrigger` on dialogue-complete via `Behaviour.Dialogues/CharacterMono.cs:73` (`MissionObjective.Trigger(trigger.Value, 1)`). Every `FastTravelTalkToNPC` / `CU*TalktoNPC` / `CK*` / `CS*` / `CL*` / `CD*` trigger listed above fires from this single path. Also `Behaviour.UI.Spacestation.Bar/ItemSaleInfo.cs:78` fires misc triggers when items sell.

---

## 5. Conquest subsector and world structure

Per `Source.Simulation.World/ConquestWorld.cs`:

### Staging area (the three embassy subsectors)

```
Lux Arctos  (N)  CreateSector(...,-18..-8,  4..5)   ConquestWorld.cs:81
Lux Magna   (M)  CreateSector(...,-18..-8,  0)      ConquestWorld.cs:82
Lux Australis (S) CreateSector(...,-18..-8, -4..-5) ConquestWorld.cs:83
```

- Lux Magna is seeded with a level-31 `PoliceGuild` empty system (Canisec) at `ConquestWorld.cs:97`.
- Owners of the three embassy sectors are a random shuffle of `{blue, red, gold}` = `sectorOwners` (`ConquestWorld.cs:98-104`).
- 5–8 systems per subsector, level 31–41 (`ConquestWorld.cs:88,141`).
- Embassy stations are added to every system in each subsector (`ConquestWorld.cs:129-135`); `AddEmbassyStation`.
- Guild factions distributed round-robin across the three subsectors: `{salvageGuild, miningGuild, tradingGuild, bountyGuild, industrialGuild, mercenaryGuild, stranded}` (`ConquestWorld.cs:109-128`).

### Main Ara Martis sector

```
Ara Martis  CreateSector(0,0, "Ara Martis")  ConquestWorld.cs:333
conquestSector = true
75-89 systems (.cs:336), each level 32-37 (.cs:352)
All systems are Darkspacers-owned seed faction (.cs:353)
Each system gets ConquestStation (.cs:359) + ConquestSystem storyteller (.cs:355)
60% chance each system gets a guild substation (.cs:360-367)
20% chance each system gets a Corsair/Meridian hideout combat station (.cs:368-371 → CreateCombatStation .cs:375-383)
```

No refineries/forges inside Ara Martis (derived from guild substation filter at `GetStationFacilities` `.cs:436-439`).

### Darkspace area (Penumbra/Lucifer)

```
Penumbra  CreateSector(..., 11..15, -4..-3)  ConquestWorld.cs:390
Lucifer   CreateSector(..., 11..15,  3..4)   ConquestWorld.cs:391
3-6 systems each at level 60 (.cs:396,403)
Penumbra: Darkspacers
Lucifer: Fanatics (.cs:410)
Only Penumbra has an embassy (first system) (.cs:403-405)
```

### ConquestSystem fields (`Source.Simulation.World.System/ConquestSystem.cs`)

Relevant instance state: `controlLevel` (int; 0–2), `combatStrength` (float), `reinforcements` (float), `owner` (Faction), `previousOwner`, etc. Range: `ReinforcementsMin=0f`, `ReinforcementsMax=4f` (`Source.Simulation.Story/Conquest.cs:39-41`). Conquest tick period: `TickDelay = 3600f` seconds (1 hour; `.cs:29`).

Umbral thresholds on Conquest stations: `UmbralControlForMissions = 0.05f` (5% infection → daily mission available), `UmbralControlForShop = 0.5f` (50% → Umbral Shop opens) (`Conquest.cs:33-35`).

### Dynamic system names

`Source.Galaxy.NameGenerator/ConquestSystem.GenerateConquestSystemName` generates Ara Martis system names; names are uniqueified at `ConquestWorld.cs:354` — **the journal can record these procedural names per-system for broker dialogue**.

---

## 6. Crew / companion mechanics

### Profession flags (`Source.Crew/Profession.cs:5-14`)

```csharp
[Flags] enum Profession {
    None = 0,
    Mining = 1,
    Engineering = 4,
    Salvaging = 0x10,
    Combat = 0x20,
    Industrial = 0x40
}
```

Note the gaps (2, 8, 0x02) — likely space left for deprecated/future professions. Captain specialization is a *different* enum (`CommanderSpecialization`), see below.

### CrewType enum (`Source.Crew/CrewType.cs:4-11`)

```
Crew=1, Salvage, Miner, Combat, Boarder, DroneCult
```

NPC enemy type only; not the player's crew profession enum.

### CommanderSpecialization (`Source.Crew/CommanderSpecialization.cs:4-14`)

```
Leadership = 1,
Mining, Drones, Engineering, Industrial,
Salvaging, Economy, Offense, Defense
```

Maps 1:1 to the wiki's 8 skill trees (Mining, Salvaging, Combat→Offense, Defense, Drones, Economy, Industrial, Autopilot→Engineering) plus Leadership. Default starter spec depends on `PersonalHistory` (see §9). Skill-tree names are `SkillTreeData.GetSpecializationTreeName(spec)`.

### Crew slots and skills

Per `Source.SpaceShip/SpaceShipData.cs:65`: `crewMembers = new CrewMemberData[cls.maxOfficers]` — each hull has an integer `maxOfficers` slot count. Crew members of rarity ≥ Enhanced unlock tiers 4-6; HighGrade unlocks tiers 7-9 (`Source.Crew/CrewMemberData.cs:142-154`). Skill-node selection draws from `Skilltree.GetNodesForCrew(profession)` with random tier-1-3 minor nodes + a random tier-3 major node.

### PersonalHistory (backgrounds) — see §9

### Named companions

The decomp distinguishes "companions" (player's crew slots) from "characters" (`Source.Dialogues/Characters.cs` — station-anchored NPCs). The only actual player-crew named companions found in the decomp code are recruited via story events, but the decomp does not special-case them — they're ordinary `CrewMemberData` instances created in the relevant story mission scripts. The wiki's "Elena Scott" / "John Raythor" are `TutorialMissions.cs` / `UmbralMissions.cs` creations — search those files by name for the concrete stats.

**Gap:** Per-companion stats are in dialogue content scripts, not in structured crew data.

---

## 7. Items, rarities, and ItemCategory

### Enum (`Source.Item/ItemCategory.cs:3-23`)

```
Empty = 0,
Ore, Ammo, Turret, Module, Booster, Junk,
UnusedMissionItem, Drone, RefinedProduct, Torpedo,
JumpgatePass, TradeGoods, Usable, DefensiveTurret,
Salvage, Currency, Crystal
```

The wiki survey's category list was partial; `UnusedMissionItem` (deprecated mission artifact slot), `DefensiveTurret` (tractorable deployed turret), and `Drone` as its own category are here. `Currency` covers Credits + commendations + workshop credit.

### Rarity (`Source.Item/Rarity.cs:3-10`)

```
Standard, Enhanced, HighGrade, Exotic, Legendary
```

i.e. `White, Green, Blue, Purple, Orange`.

### Behavior by category (`Source.Item/ItemCategoryExtensions.cs`)

| Category | Equippable | Sell-value × | Has buyback |
|---|---|---|---|
| Ore / Ammo / Salvage / RefinedProduct / Junk | — | 0.8 | no |
| TradeGoods | — | 0.5 | no |
| Currency | — | 0.05 | no |
| Crystal | — | 0.8 | no |
| Turret / Module / Booster | yes (equipslot) | 0.12 | yes |
| DefensiveTurret / JumpgatePass / Usable / Torpedo / Drone | — | 0.12 | varies |

Equippable categories (`.cs:10-19`): Booster, Module, Turret.

---

## 8. Ship enumeration

Ship data lives in Unity ScriptableObjects at `Resources/SpaceShips/*.asset`, loaded at runtime via `Resources.LoadAll<SpaceShip>("SpaceShips")` (`Behaviour.Unit/SpaceShip.cs:1143`). The decomp lets you see **ship identifiers as string literals** in code but not their stats / module layouts.

### Classification enums

```
SpaceShipType (Source.SpaceShip/SpaceShipType.cs):
  Drone, Size1..Size8
SpaceShipRole (Source.SpaceShip/SpaceShipRole.cs):
  Generic, Combat, Mining, Salvaging, Cargo
```

Range bonus per size (Combat vs other): `CombatRangeBonus = {0, 0.2, 0.4, 0.7, 1.0, 1.2, 1.8, 2.0}` (Size 1-8); `IndustrialRangeBonus = {0, 0.1, 0.2, 0.3, 0.4, 0.5, 0.7, 0.8}` (`Source.SpaceShip/SpaceShipRoleType.cs:11-13`).

### Ship identifiers discoverable in code (string literals)

From mission scripts:

- **Quest ships**: `"Eclipse"` (`Source.MissionSystem.Story/UmbralMissions.cs:3172`), `"Terravex"` (`UmbralMissions.cs:2427, 3347, 3835`), `"Blood Dredger"` (Pirate start), `"Margil"` (Navy start), `"Chisel Mk I"` / `"Chisel Mk I SN"`, `"Garnil"`, `"Tugbit"`, `"Raptor"`, `"Oxlo Mk I"`, `"Acolyte AC-1"` (all from `Source.Player/PersonalHistoryData.cs:37-82`).
- **Stellar patrol fleet presets**: `{"Sparta", "Varyag", "Grom", "Maniple", "Kamin"}` at `UmbralMissions.cs:3604`.
- **Recruitment offerings**: `{"Fultor", "Exdyne", "Bero", "Osprey"}` at `Source.Galaxy.POI.Station/RecruitmentCenter.cs:76`.

### Hull ↔ faction affinity

Governed by `SpaceShip.shopItemData.factionPrereq` list (a `List<FactionPrerequisites>`; `Source.Galaxy/Faction.cs:119`). NPC ship selection logic at `Faction.GetNPCShipTypes(level, min, max, activity)` (`.cs:109-149`) filters `allShips` by `shopItemData.factionPrereq` and `IsNPCShipAvailable`. The full roster is in the asset bundles.

**Gap:** Full hull list, stats, slot layouts, and manufacturer-to-faction mappings are in the asset bundles, not decompilable from .cs.

---

## 9. Background / prologue variants

`Source.Crew/PersonalHistory.cs:4-11` defines the 6 background enums:

```
Miner, NavyCaptain, Salvaging, Hauler, BountyHunter, Pirate
```

Starting state per background (`Source.Player/PersonalHistoryData.cs:30-92`):

| PersonalHistory | Locked? | Credits | Specialization | Starter ships | Crew | Ammo | Description key |
|---|---|---|---|---|---|---|---|
| Miner | no | 5000 | Mining | Chisel Mk I, Garnil | 0 | no | `@NGMiningStart` |
| Salvaging | no | 5000 | Salvaging | Tugbit, Chisel Mk I SN | 0 | no | `@NGSalvageStart` |
| NavyCaptain | no | 4000 | Offense | Margil, Raptor | 0 | yes | `@NGCombatStart` |
| Hauler | **yes** | 10000 | Economy | Oxlo Mk I | 1 | no | `@NGNotAvailable` |
| BountyHunter | **yes** | 5000 | Drones | Acolyte AC-1 | 0 | yes | `@NGNotAvailable` |
| Pirate | **yes** | 1000 | Offense | Blood Dredger | 0 | yes | `@NGNotAvailable` |

Only 3 backgrounds are unlocked as of this decomp: Miner, Salvaging, NavyCaptain. The other 3 are present in code, locked in the New Game UI (`Behaviour.UI.Main/NewGame.cs:265-270`).

### Tutorial branch per background (`Source.Simulation.Story/Tutorial.cs:627-629`)

```
Miner       → BigFatAsteroids travel event
NavyCaptain → DistressCombat travel event
Salvaging   → TravelEvents.Salvage event
```

Title awarded (`Source.Util/GamePatch.cs:783-788`): `PersonalHistory.Miner → Titles.Miner`, `NavyCaptain → Titles.NavyCaptain`, `Salvaging → Titles.Salvager`, `Hauler → Titles.Hauler`, `BountyHunter → Titles.BountyHunter`, `Pirate → Titles.Pirate`.

Display labels used in the New Game UI (`Source.Util/NewGameExtension.cs:10-17`): Miner → "Prospector", Hauler → "Cargo Runner", Pirate → "Raider", NavyCaptain → "Navy Officer", BountyHunter → "Contract Specialist", Salvaging → "Wreckage Explorer".

### NavyCaptain-specific branch

`Source.MissionSystem.Story/TutorialMissions.cs:1017` branches on `ply.commander.personalHistory == PersonalHistory.NavyCaptain` — grep that file for the Navy-specific Defend Station flow (the only in-code background divergence found).

---

## 10. Journal-worthy event hooks (Harmony patch targets)

The journal needs two classes of hooks: (a) **event fires** — one-shot observable moments; (b) **state reads** — aggregate player stats accessible at read time.

### High-value patch points for (a)

| Hook | Method | Notes |
|---|---|---|
| Any unit dies | `Behaviour.Unit/AbstractUnit.cs:1499` — `MissionObjective.Trigger(MissionTrigger.UnitDestroyed, (this, damageData))` | One patch covers all kills. `(this as AbstractUnit).faction`, `.level`, `.unitData` available. `hitByPlayer` is `damageData.inflictor is Player`. |
| Station docked | `Behaviour.UI.Spacestation/SpaceStationInterior.cs:191` | `SpaceStation.current` is the station. |
| System entered | `Source.Player/Register.cs:136` — `AddVisitedSystem(guid)` | Fires `VisitUniqueSystem` once per GUID. Patch `AddVisitedSystem` to log first-visit; patch `FactionData.current` read site or `GamePlayer.current.currentSystem` for every visit. |
| Loot container opened | `Behaviour.Persistables/LootContainer.cs:133` | Payload: `LootContainerData` (Steel Vultures lockboxes). |
| Ore mined | `Behaviour.Mining/Asteroid.cs:727` | Counter `Register.OresMined`, `SurfaceOreMined`, `CoreOreMined` auto-tracked (`Source.Player/Register.cs:18-26`). |
| Salvage retrieved | `Source.Data.Persistable/SalvageData.cs:544` | Counter `Register.SalvagingScrap` auto-tracked. |
| Mission complete | `Source.MissionSystem/Mission.cs:347` — `MissionObjective.Trigger(CompleteDynamicMission)` — or `GamePlayer.CompleteMission(m)` at `Source.Player/GamePlayer.cs:820` | VGAnima already hooks this. |
| Bounty kill | `unitData.deathTrigger.Value` dispatch at `AbstractUnit.cs:1503` | Same hook as `UnitDestroyed` — check `trigger == BountyTargetKilled`. |
| Station infected with Umbral | `Behaviour.Unit.Parts/UmbralHackingBot.cs:41` | One-off per station. |
| Crafting completes | `Source.Mining/ForgeJob.cs:65,81` | `(itemKey, amount)` + station. |
| Mission accepted | `Source.Player/GamePlayer.cs:731` — `AcceptMission(mission)` | VGAnima already hooks. |
| Item sold / bought | `Behaviour.UI.Spacestation.Bar/ItemSaleInfo.cs:78` | Fires configurable `MissionTrigger`. |
| Reputation changed | `Source.Galaxy/FactionData.cs:33` — `ChangeReputation(self, other, change)` | Single chokepoint for all rep movements. |

### Class (b): `Register` counters the journal can read for free

`Source.Player/Register.cs:18-60` already tracks these (read via `Register.GetCounter("X")`):

```
OresMined, SurfaceOreMined, CoreOreMined, SurfaceOreYieldMax,
CoreOreYieldMax, OreStolen, SalvagingScrap, SalvagingScrapYieldMax,
SalvagingItemsRetrieved, SalvagingItemMaxYield,
SalvagingCreditsRetrieved, SalvagingLootboxRetrieved,
EmergencyJumps, StationsVisited, CreditsGained, LootBoxesGained,
LootboxSkillPointsGained, WorkshopCreditsGained,
CargoScannerHit, CargoScannerValue, TrackerPlaced,
DecoyTransponderUsed
```

Plus `IdleStat` counters via `GamePlayer.current.AddAutopilotStat(IdleStat.X, n)` — see `Source.Player/IdleStat.cs`. Known: `IdleStat.Kills`, `Modules`, `Turrets`, `ReputationGained`, `ReputationLost` (referenced in `LootManager.cs`, `TractorBeam.cs`, `FactionData.cs:43-52`).

### Additional rank/counter state on `GamePlayer`

`GamePlayer.cs` exposes: `maxBountyLevel`, `bountyRank`, `maxPatrolLevel`, `patrolRank`, `maxIndustryLevel`, `industryRank`, `hasUmbralTransponder`, `autoPlay`, `atWar` (List<Faction>), `level`, `register`, `factionData`, `commander`, `currentSpaceShip`, `fleet`. Plus storyteller-scoped state via `GetStoryteller<Tutorial>()`, `GetStoryteller<Default>()`, `GetStoryteller<Conquest>()`, `GetStoryteller<Economy>()`.

### Register flag API

`Source.Player/Register.cs:74-101` — `HasFlag(name)`, `SetFlag(name, val)`, `GetCounter(name)`, `AddCounter(name, add)`, `SetCounter`, `GetData`/`SetData`. **The journal can persist its own flags in vanilla's save by piggybacking on `Register`** — they serialize into `Register.ToJson` automatically (`.cs:155-179`).

Known vanilla-set flags: `"PuppeteersNameChange"` (`Source.Galaxy.Factions/Puppeteers.cs:26`). Known vanilla-set counters: all of the above `Register.*` constants + anything passed to `Register.AddCounter(...)`.

---

## 11. Miscellaneous mechanics notes

### GameplayType (used to disambiguate activity)

```csharp
enum GameplayType { Generic, Combat, Mining, Salvage, Cargo, Repair }
```

(`Source.Util/GameplayType.cs:3-10`) — attached to ships via `SpaceShipRoleType.GetGameplayType()`, to mission generators via `GetMissionType()`, and to station facility offerings. Maps naturally to journal "activity archetype" tracking. The shipping assembly adds a 6th member, `Repair`, that the earlier survey omitted; it covers repair-station facility offerings / repair work, alongside the five documented labels.

### Mission board refresh

Full automatic refresh every **300 seconds** (5 min) — `Source.Galaxy.POI.Station/MissionBoard.cs:19` `RefreshTime = 300f` (wiki is correct here). The rep-gated `GetMissionBoardRefreshTimer` (`ReputationLevelExtensions.cs:282`) values `Friendly=55s, Respected=50s, Distinguished=45s, Exalted=40s` (default 60s) are the cooldown on a **manual refresh** action, gated behind `level >= Cordial` (`CanRefreshBoard`). Bonus missions per refresh: `ReputationLevelExtensions.GetBonusMissionAmount` returns 1/2/2/3 at Friendly/Respected/Distinguished/Exalted.

### Bonus skill-point cap

`MaxBonusSkillPoints` defined in `GameMath`; `Conquest.maxBonusSkillpoints = 61` (`Source.Simulation.Story/Conquest.cs:88`). `Conquest.maxLootBoxSkillPoints = 10`. Bonus skill points accumulate over cap (`CommanderData.TryGiveBonusSkillPoints`, `Source.Crew/CommanderData.cs:65-88`).

### Umbral content thresholds

Daily Umbral mission available at 5% station infection; Umbral shop opens at 50%. `Source.Simulation.Story/Conquest.cs:33-35`.

### Conquest tick

1 hour real-time between ticks (`Conquest.TickDelay = 3600f`); ticks catch up across sessions by walking `lastConquestTick.DayOfYear → DateTime.Now.DayOfYear` (`.cs:175-197`), max 5 missed.

---

## Cross-references to the wiki survey

- Faction display names (wiki §2) ↔ identifiers (decomp §1).
- Rep tiers (wiki §2) ↔ `ReputationLevelExtensions.ReputationThresholds` (decomp §2).
- Conquest ranks (wiki §7) ↔ `ConquestRankExtension.ConquestRankThresholds` (decomp §2).
- Mission archetypes (wiki §9) ↔ full generator enumeration (decomp §3).
- POI types (wiki §3) ↔ `MissionTrigger.VisitUniqueSystem` + `Register.visitedSystems` + ConquestWorld subsector names (decomp §4, §5).
- Item categories (wiki §4 partial) ↔ full enum (decomp §7).
- Skill trees (wiki §7) ↔ `CommanderSpecialization` (decomp §6).
- Story arcs (wiki §8) ↔ Umbral trigger chain + `StoryMission` registry (decomp §3, §4).
- Background starts (wiki mentions) ↔ `PersonalHistoryData` table (decomp §9).
- Reputation mechanics (wiki §2 "−200/−800") ↔ actual formula (decomp §2; wiki is **wrong**).

## Wiki corrections

1. **Enemy cutoff**: wiki implies `Hostile` (−5000); actual is `−500` (`Wary` floor). `FactionData.cs:27`.
2. **Rep loss on kill**: wiki says "−200 small / −800 large"; actual is `ceil(baseXPReward * 0.5) × 20` if not-yet-hostile. `LootManager.cs:54-69`.
3. (Wiki correct) Mission board full refresh is 300s / 5 min. The 40-60s timer gated on rep `Cordial`+ is a manual-refresh cooldown, not auto-refresh.
4. **Faction count**: wiki lists 16; decomp has 19 (adds Player, Amalgam, HolyRadicals). Latter two look unused — `offersMissionsForShip=false`, `minShipVariety=1`, excluded from `RandomEnemyFaction`.
5. **Umbral Reach display name**: wiki calls it "Umbral Reach" throughout; vanilla swaps `@FactionNamePuppeteers` → `@FactionNamePuppeteers2` once `Conquest` storyteller active (suggests in-lore reveal of identity). `Puppeteers.cs:22-36`.
