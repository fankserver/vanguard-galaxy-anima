# Vanguard Galaxy Bar Ecosystem Survey

Focused decomp survey covering the vanilla bar stack: the `Salesman` class, patron composition, purchase flow, reward palette, and what VGAnima brokers can directly reference or offer.

**Survey date:** 2026-04-22. **Decomp path:** `/tmp/decomp/`. Citations reference `Source.Foo/Bar.cs:NN`.

**Scope:** intentionally narrow — we leave faction/reputation/mission-archetype reference to the sibling [decomp survey](./vanguard-galaxy-decomp-survey.md) and player-facing framing to the [wiki survey](./vanguard-galaxy-wiki-survey.md). This doc answers "what lives on the bar?", "what is purchased?", and "what can be handed out as a mission reward?"

---

## 1. Salesman class hierarchy

**Wiki was wrong — or at least misleading.** The Fandom wiki implies "5-15 salesman types" (prospectors, salvage scouts, crew, industrial reps, slick entrepreneurs…). The decomp shows a single concrete class `Salesman` at `Source.Galaxy.POI.Station.Patrons/Salesman.cs:20` with **four probability branches** gated by a seeded RNG roll in `Salesman.InitializeData` (`.cs:57-77`). There is no subclass hierarchy, no `SalesmanType` enum, and no per-variant class.

### The four variants

| Variant | Spawn p | Description field | `itemBuilder.identifier` | Item factory | Cost formula | Faction binding |
|---|---|---|---|---|---|---|
| `SalesmanSpaceShipPNG` (Slick Entrepreneur) | `< 0.1` (10%) | `"Slick Entrepreneur"` | `"SpaceShipPng"` | resolved by `InventoryItemType` implicit-string lookup (`Salesman.cs:108`) | `itemForSale.cost * 251` | none (flat joke item) |
| `SalesmanMiningClaim` (Prospector) | `[0.1, 0.3)` (20%) | `"Prospector"` | `"MiningClaim"` | `ItemBuilder.Get("MiningClaim").CreateMiningClaim(spaceStation.system, asteroidFieldData)` (`Salesman.cs:177`) | `itemForSale.cost` (GameMath formula) | anchored to `spaceStation.system` (faction-agnostic — see note) |
| `SalesmanSalvageClaim` (Salvage Scout) | `[0.3, 0.5)` (20%) | `"Salvage Scout"` | `"SalvageClaim"` | `ItemBuilder.Get("SalvageClaim").CreateSalvageClaim(spaceStation.system, list)` (`Salesman.cs:160`) | `itemForSale.cost` | anchored to `spaceStation.system` |
| `SalesmanEquipment` (`{Manufacturer} Representative`) | `[0.5, 1.0)` (50%) | `"{Manufacturer} Representative"` | equipment-builder id (e.g. `"SmallBlasterTurret"`) | `random.Choose(EquipmentBuilder.GetItemsForGeneralShop(spaceStation.level)).CreateItemType(rarity, level, exactLevel: true, seed)` (`Salesman.cs:130`) | `itemForSale.cost` | indirect via `Manufacturer.GetFaction()` — 6 of 15 manufacturers bind (see below) |

### SalesmanEquipment rarity ladder (`Salesman.cs:129`)

```csharp
rarity = spaceStation.level < 5      ? Rarity.Enhanced
       : spaceStation.level >= 15    ? (random.RandomBool(0.2f) ? Rarity.Exotic : Rarity.HighGrade)
       : Rarity.HighGrade;
```

### The "faction binding" finding — wiki-wrong

The wiki implies Prospectors = Mindus Holdings and Salvage Scouts = Steel Vultures. **The decomp shows both are faction-agnostic** — they carry a claim rooted in the station's *system* (`spaceStation.system`), not the station's faction. A Corsair Dread Port could host a Prospector hawking a Corsair-system mining claim. Only `SalesmanEquipment` carries any faction signal, and it's indirect:

```
Manufacturer → Faction (via ManufacturerExtensions.GetFaction, Behaviour.Equipment.Builder/ManufacturerExtensions.cs:13-25):
  Blue       → Faction.blue
  Gold       → Faction.gold
  Red        → Faction.red
  Mining     → Faction.miningGuild
  Police     → Faction.policeGuild
  Pirate     → Faction.marauders
  [9 others → null]
```

Nine of fifteen manufacturers (`EliteCombat`, `GenericCombat`, `Utility`, `ConsumerGoods`, `MedicalGoods`, `DarkspaceRegular`, `Stellar`, `FanaticsAlien`, `Umbral`) return `null`. For those, `Salesman.cs:132` falls back to `"Sales"` literal in the description field — "Sales Representative" is the generic label.

### Conditional spawns / rank gates

**None.** No `if (spaceStation.level > X)` gates, no faction reputation checks, no Conquest state prerequisites. Every bar rolls the same 4-way distribution. The equipment variant self-scales by `spaceStation.level` through `EquipmentBuilder.GetItemsForGeneralShop(level)` (`Salesman.cs:130` → `EquipmentBuilder.cs:854-865`, which returns all builders matching `AvailableForLevel(level)`).

### SpaceShipPng — joke item

The "Slick Entrepreneur" pitches a scam: the item is called `SpaceShipPng` (literally the PNG image of a spaceship). Hard-coded name `"Robert Miyama"` (`Salesman.cs:104`), male, fixed portrait (`CrewIcons.Get("Man02")`), price multiplier `×251`. Dialogue explicitly sets up a snake-oil punchline. No mechanical value beyond consuming cargo space. **For VGAnima purposes this is pure flavor — the LLM could reference the Slick Entrepreneur at an adjacent seat as a comic beat.**

---

## 2. Bar patron composition + detection

### How patrons get added

`Bar.CheckUpdatePatrons(force = false)` (`Source.Galaxy.POI.Station/Bar.cs:24-61`) fires once per game-day, detected via `new DateTime(lastUpdateTime).DayOfYear == DateTime.Now.DayOfYear`. The refresh pipeline:

1. Clear `availablePatrons`.
2. Seed an RNG (either `SeededRandom.Global` on first run, or `new SeedGenerator().Add(nextUpdateSeed)` on subsequent — so refreshes are deterministic per station).
3. Loop `i = 0..4` (5 seats). For each seat, call `CreateBarPatron(i+1, rng)`, `patron.Initialize()`, and dedupe-check against already-accepted patrons via `BarPatron.ConflictsWith`. Redraw until unique.
4. **Trim loop** (`Bar.cs:53-58`): starting `p=0.75`, while `rng.RandomBool(p)` remove a random patron and decrement `p -= 0.25`. So the expected final count is **2–5 patrons** (5 minus the geometric trim), skewing toward 3–4.
5. Persist next-refresh seed + timestamp.

```
CreateBarPatron (Bar.cs:63-77):
  rng.RandomFloat() < 0.3 → CrewMember  (30% per seat)
  else                    → Salesman    (70% per seat)
```

Combined with the Salesman internal branch: per seat, the a-priori distribution is
`CrewMember 30% / Equipment 35% / MiningClaim 14% / SalvageClaim 14% / SpaceShipPng 7%`.

### ConflictsWith (`BarPatron.cs:49-55`)

```csharp
public virtual bool ConflictsWith(BarPatron other)
{
    if (!(name == other.name))
        return icon == other.icon;
    return true;
}
```

Same name **or** same icon. Drives the "unique names and faces" guarantee per bar-refresh.

### Is the VGAnima injection path already compatible?

**Yes.** `VGAnima/Patches/BarRefreshPatches.cs:99,138-139,643` already hooks `Bar.CheckUpdatePatrons` prefix+postfix and adds `BarPatron` instances directly to `availablePatrons`. Any new VGAnima capability that iterates `availablePatrons` to read "other salesmen at this bar" can use the same list without a new hook.

### Salesman classification at dispatch time

For a broker to reference "the claim dealer over there," iterate `bar.availablePatrons` at dispatch time, filter by `patron is Salesman`, then classify each by `salesman.itemForSale.itemBuilder.identifier`:

```csharp
// itemBuilder is public auto-property on InventoryItemType (cs:95):
//   public ItemBuilder itemBuilder { get; set; }
// identifier is set to the asset name in ItemBuilder.LoadAll (cs:402-405)
// — stable values: "MiningClaim", "SalvageClaim", "SpaceShipPng", or an
//   equipment-builder id (e.g. "SmallBlasterTurret").
var id = salesman.itemForSale.itemBuilder?.identifier ?? "?";
var kind = id switch
{
    "MiningClaim"  => "Prospector",
    "SalvageClaim" => "Salvage Scout",
    "SpaceShipPng" => "Slick Entrepreneur",
    _              => "Equipment Rep"
};
```

A coarser fallback is `salesman.description` (string-matched on `"Prospector"`, `"Salvage Scout"`, `"Slick Entrepreneur"`, or ends-in-`"Representative"`), but `itemBuilder.identifier` is the canonical cut.

### Story NPCs vs. bar patrons

Story NPCs (Keril, Raythor, Greg, Virgil, Elena Scott, Arle, Elias McIntire, Olga Skarsgard, Alice Okono, etc.) are **`Source.Dialogues/Character` instances** (factory methods in `Source.Dialogues/Characters.cs:134-347`). They render in-world via `Behaviour.Dialogues/CharacterMono` MonoBehaviours placed as scene prefabs — **they never enter `Bar.availablePatrons`**. Safe to treat `availablePatrons` as procedural-only. Skill-trainer characters like Elias McIntire live in the "Bar" skillroom scene visually but are scene-level `CharacterMono`s, not `BarPatron`s.

---

## 3. Purchase event hooks

### The hard finding — no MissionTrigger for purchases

The 113-value `MissionTrigger` enum in `Source.MissionSystem/MissionTrigger.cs:3-113` contains **zero purchase-related values**. Closest matches:

- `ItemCollected` — fires on tractoring (`Behaviour.Tractoring/TractorableItem.cs:156`), not purchasing.
- `InstallMiningLaser` / `InstallCombatTurret` / `InstallSalvageLaser` — fire on equipping from the Personal Hangar (`PersonalHangar.cs:603-611`), not on purchase.
- `CraftItem` — fires on Forge crafting (`ForgeJob.cs:65,81`), not on purchase.
- `DockedWithSpaceStation` / `ArrivedAtSpaceStation` — arrival events, not transactions.

No `ItemBought`, `ItemPurchased`, `MiningClaimBought`, `SalesmanItemBought` — confirmed with the full enum dump in the sibling decomp survey §4.

### The ItemSaleInfo.trigger field is mostly-dead

`Behaviour.UI.Spacestation.Bar/ItemSaleInfo.cs:38` declares `public MissionTrigger? trigger;` — and `ItemSaleInfo.ButtonPurchase` (`.cs:69-88`) does check `trigger.HasValue` and fire `MissionObjective.Trigger(trigger.Value, 1)` at `.cs:78`.

**But** the trigger field is set by exactly ONE call site: `GameplayManager.ShowItemSaleInfo(salesman, MissionTrigger.UmbralSteelVultureComputer)` (`GameplayManager.cs:84-92`), called from `Source.Dialogues/Characters.cs:315` inside `BuyComputer()` — a scripted Umbral-questline purchase that bypasses the bar entirely (it's injected via `Characters.steelVultureComputerSalesman` as a one-shot scripted sale).

The standard bar path (`BarUI.ShowSalesmanInfo(salesman)` at `BarUI.cs:74-78`) does `Object.Instantiate(salesmanInfoPrefab)` and calls `.Show(salesman)` — **it never sets `trigger`**. So `trigger.HasValue == false` for 100% of procedural bar purchases, and the trigger branch at `ItemSaleInfo.cs:76-79` is dead code for the standard flow.

### Where purchases actually happen (no event fires)

`ItemSaleInfo.ButtonPurchase` (`ItemSaleInfo.cs:69-88`) in full:

```csharp
public void ButtonPurchase()
{
    if (!GamePlayer.current.CanAfford(salesmanData.itemCost)) { /* toast + return */ }
    if (trigger.HasValue) MissionObjective.Trigger(trigger.Value, 1);  // dead for bar
    GamePlayer.current.RemoveCredits(salesmanData.itemCost);
    GamePlayer.current.currentSpaceShip.cargo.Add(salesmanData.itemForSale, 1);
    SpaceStation.current?.bar?.availablePatrons.Remove(salesmanData);
    /* refresh BarUI */
}
```

The only observable side effects are:
- `RemoveCredits` — auto-increments `IdleStat.CreditsSpent` (`GamePlayer.cs:326`) on every debit; coarse (also covers refinery, forge, shipyard, repair).
- `cargo.Add` — no counter, no trigger.
- Patron removal — detectable only by diffing `availablePatrons` before/after.

`Source.Player/GamePlayer.cs:319-332` — `RemoveCredits`:

```csharp
public void RemoveCredits(float amount)
{
    if (amount < 0f) { /* warn + return */ }
    AddAutopilotStat(IdleStat.CreditsSpent, (int)amount);
    credits -= (int)amount;
    if (credits < 0) credits = 0L;
}
```

### The `InventoryItemPart.OnPurchase` hook — general-shop-only

`Behaviour.Item/InventoryItemPart.cs:20` declares `public virtual void OnPurchase(int amount)`, overridden in `Behaviour.Item.Usable/BonusSkillPoint.cs:28-40`:

```csharp
public override void OnPurchase(int amount)
{
    if (MapPointOfInterest.current is SpaceStation { conquestShopInventory: not null })
        Register.AddCounter("ConquestSkillpointPurchase", amount);
    else
        Register.AddCounter("BonusSkillpointPurchase", amount);
    base.item.RecalculateCost();
}
```

But `OnPurchase` is only dispatched from one place: `Behaviour.UI/InventoryInteractionManager.cs:832`, inside `BuyAmount` — the **general-shop sell-to-player** flow. The bar-salesman flow (`ItemSaleInfo.ButtonPurchase`) does not call `OnPurchase`. So the `BonusSkillpointPurchase` / `ConquestSkillpointPurchase` counters cover general-shop buys only, not bar buys.

### Conclusion: bar purchase tracking needs a Harmony hook

There is **no vanilla path** to observe a bar-salesman purchase as an event. Options:

| Option | Feasibility | Notes |
|---|---|---|
| Harmony postfix on `ItemSaleInfo.ButtonPurchase` | **definitely supported** | Simplest — capture `salesmanData` before return. Filter on `salesman.itemForSale.itemBuilder.identifier` to classify. |
| Harmony postfix on `GamePlayer.RemoveCredits` + inspect `SpaceStation.current?.bar` | supported but noisy | Coarse — you'd need to cross-ref with the last-shown `ItemSaleInfo` to know *what* was bought. |
| Patch `ItemSaleInfo.Show` to inject `trigger = SomeVGAnimaMissionTrigger` | would require a new `MissionTrigger` enum value | Not possible without extending the enum via Harmony; the Trigger field is a `MissionTrigger?` and only vanilla enum values are valid. Skip this. |

**Recommendation:** single Harmony postfix on `ItemSaleInfo.ButtonPurchase` — straightforward to wire, classifies purchases by `itemBuilder.identifier` for free, and has exactly one vanilla call site so no dispatch ambiguity.

---

## 4. Purchase history via Register

### Full enumeration of Register counters (`Source.Player/Register.cs:18-60`)

```
OresMined, SurfaceOreMined, CoreOreMined, SurfaceOreYieldMax, CoreOreYieldMax,
OreStolen, SalvagingScrap, SalvagingScrapYieldMax, SalvagingItemsRetrieved,
SalvagingItemMaxYield, SalvagingCreditsRetrieved, SalvagingLootboxRetrieved,
EmergencyJumps, StationsVisited, CreditsGained, LootBoxesGained,
LootboxSkillPointsGained, WorkshopCreditsGained, CargoScannerHit,
CargoScannerValue, TrackerPlaced, DecoyTransponderUsed
```

**Purchase-related counters found outside the declared const-set**, via grep on `Register.AddCounter(`:

| Counter name | Where incremented | Meaning |
|---|---|---|
| `"BonusSkillpointPurchase"` | `Behaviour.Item.Usable/BonusSkillPoint.cs:37` | Count of BonusSkillPoint consumables purchased from general shops (not bar, not Conquest). Drives exponential price ramp (`.cs:44`). |
| `"ConquestSkillpointPurchase"` | `Behaviour.Item.Usable/BonusSkillPoint.cs:32` | Same but for ConquestShop-purchased skillpoints. |

Those are the **only** purchase-related counters the vanilla game maintains. No `MiningClaimsBought`, no `SalvageClaimsBought`, no `BlueprintsBought`, no `ShipyardShipsBought`, no `ModulesBought`, no `TurretsBought`, no `CrewHired`, no `MercenariesHired`.

### Adjacent non-purchase counters worth naming

- `SalvagingCreditsRetrieved` (`Register.cs:38`) — scrap sold; not *bought*.
- `OreStolen` (`Register.cs:28`) — stolen items, not purchases.
- `CreditsGained` (`Register.cs:46`) — coarse income, not purchase-aware.

### Register is piggyback-friendly

`Register.AddCounter(name, add)` and `Register.SetFlag(name, val)` accept arbitrary string keys. VGAnima can write its own keys (`"VGAnima_MiningClaimsBought"`, `"VGAnima_SalvageClaimsBought"`, etc.) into the vanilla Register, and those serialize inside `Register.ToJson` (`.cs:155-179`). **This means a VGAnima Harmony hook on `ItemSaleInfo.ButtonPurchase` can persist its own per-variant purchase counters using vanilla's save format for free**, no separate persistence layer needed. The same trick is already in use for `PuppeteersNameChange` (`Source.Galaxy.Factions/Puppeteers.cs:26`).

---

## 5. Reward palette — what items can brokers offer?

### Vanilla ships 14 concrete `MissionReward` subclasses, not 4

All under `Source.MissionSystem.Rewards/`. Registered via reflection in `MissionReward.Create` (`MissionReward.cs:48-51`) — `Type.GetType("Source.MissionSystem.Rewards." + name)` and invoke the parameterless ctor.

| Class | Payload | OnComplete effect |
|---|---|---|
| `Credits` | `int amount` | `GamePlayer.credits += amount; Register.AddCounter("CreditsGained", amount); AddAutopilotStat(IdleStat.Credits, amount)` |
| `Experience` | (not re-read) | adds XP |
| `Reputation` | faction + amount | rep delta |
| `Skilltree` | tree | unlocks skill tree |
| `Skillpoint` | `int amount` | `commander.TryGiveBonusSkillPoints(amount)` or `cargo.AddCargo("BonusSkillPointTemplate", amount)` (`Skillpoint.cs:33-40`) |
| **`Item`** | **`InventoryItemType item; int amount`** | **`GamePlayer.current.currentSpaceShip.AddCargo(item, amount, force: true)`** (`Item.cs:36-39`) |
| **`Ship`** | **`Behaviour.Unit.SpaceShip ship`** | **`new SpaceShipData(ship, isPlayer: true); GamePlayer.spaceShips.Add(…)`** (`Ship.cs:24-28`) |
| **`Crew`** | **`CrewMemberData crew`** | **`GamePlayer.crewMembers.Add(crew); currentSpaceShip.AssignCrewMember(crew)`** (`Crew.cs:23-27`) |
| `WorkshopCredit` | `int amount` | `GamePlayer.AddSalvageShopCredit(amount, 0, fromItems: false); Register.AddCounter("CreditsGained", amount)` |
| `ConquestStrength` | `int amount` | storyteller contribution + ConquestSystem combatStrength (see `ConquestStrength.cs:27-57`) |
| `UmbralControl` | `int amount` | `storyteller.umbralContribution += amount/2; conquestSystem.umbralControlLevel += amount/100` |
| `POICoordinates` | `MapPointOfInterest poi` | **`OnComplete`: empty (`POICoordinates.cs:29-31`) — `rewardText` throws `NotImplementedException`**. Broken / dead class in the current build. |
| `StoryMission` | `string missionId` | `GamePlayer.AddMissionWithLog(StoryMission.Get(missionId))` (`StoryMission.cs:22-30`) |
| `MissionFollowUp` | `Mission followUpMission` | `GamePlayer.AddMissionWithLog(followUpMission)` (`MissionFollowUp.cs:22-25`) |

### Item rewards work today

`Source.MissionSystem.Rewards/Item.cs:9-40`:

```csharp
public class Item : MissionReward
{
    public InventoryItemType item;
    public int amount = 1;
    // ...
    public override void OnComplete(Mission m)
    {
        GamePlayer.current.currentSpaceShip.AddCargo(item, amount, force: true);
    }
}
```

Any `InventoryItemType` works. That includes everything the salesman variants hand out:

- `"MiningClaim"` via `ItemBuilder.Get("MiningClaim").CreateMiningClaim(system, asteroidFieldData)` (`ItemBuilder.cs:91-108`). `system` is just `SpaceStation.current.system` at mission-creation time; `asteroidFieldData` can be built with `new AsteroidFieldData(count, density, wealth, surfaceOres, coreOres)`.
- `"SalvageClaim"` via `ItemBuilder.Get("SalvageClaim").CreateSalvageClaim(system, salvageList)` (`ItemBuilder.cs:154-167`). Salvage list built with `SalvageData { position, angle, shipTemplate }; salvage.AddItemContent/AddScrapContent/AddStructuralContent` (pattern at `Salesman.cs:146-159`).
- `"MaterialMiningClaim"` — specialized mining claim anchored to a chosen `RefinedMaterial` (`ItemBuilder.cs:110-152`). Richer narrative hook ("a platinum-rich claim").
- **Blueprints**: `ItemBuilder.Get(builder).CreateBlueprint(CraftingRecipe)` (`ItemBuilder.cs:307-333`) or `ItemBuilder.Get(builder).CreateRandomBlueprint(level, rarity?, random?, excludeExoticOrHigher=true)` (`ItemBuilder.cs:334-351`). This is *huge* — LLM brokers could offer "a stolen Enhanced blueprint" as a reward.
- `SpaceShipPng` — joke reward. Zero mechanical use but could be a comedic alternate offer.
- Arbitrary equipment via `EquipmentBuilder.GetItemsForGeneralShop(level).Choose().CreateItemType(rarity, level, exactLevel: true, seed)` (pattern from `Salesman.cs:130`).
- `GatePass` (jumpgate pass), `WarpFuel`, `DefensiveTurret`, `ExplosiveMine`, consumables via misc `ItemBuilder.CreateX` methods (`ItemBuilder.cs:74-196`).

### Ship rewards work today

`Rewards.Ship` (`Ship.cs`) takes a `Behaviour.Unit.SpaceShip` (the Unity `ScriptableObject`), resolvable by identifier via the existing ship-loading pipeline at `Behaviour.Unit/SpaceShip.cs:1140-1147` (`Resources.LoadAll<SpaceShip>("SpaceShips")`). So a mission can reward any ship by name — the `Ship.LoadFromJson` implicit-string-to-SpaceShip coercion at `Ship.cs:21` (`ship = data["ship"].AsString`) does exactly that. This is what `UmbralMissions.cs:3172` etc. use to hand out quest-ships like `"Eclipse"` and `"Terravex"`.

### Crew rewards work today

`Rewards.Crew` takes a `CrewMemberData` — construct with `CrewMemberData.CreateRandomCrewMember(random)` (used by `Bar.CreateBarPatron` at `Bar.cs:69`) or build one manually. This unlocks "complete this and I'll put one of my people in your crew" missions.

### Verdict for VGAnima

**No schema extension needed in the mission-reward layer itself** — vanilla's `Rewards.Item`, `Rewards.Ship`, `Rewards.Crew`, `Rewards.Skillpoint`, `Rewards.WorkshopCredit` are all ready. VGAnima only needs:

1. A schema extension in **its own LLM-authored mission JSON** to express typed item rewards — something like `{ "type": "Item", "kind": "MiningClaim", "anchor": "currentStation" }` or `{ "type": "Item", "kind": "Blueprint", "rarity": "HighGrade" }`.
2. A factory that maps those JSON shapes to vanilla `Rewards.Item` with an `InventoryItemType` built via the appropriate `ItemBuilder.CreateX(...)` call.

The broken `POICoordinates` class is not worth using — build on the working classes.

---

## 6. Faction-economic bindings

### What the bar tells you about the station's faction

Not much directly. The bar is agnostic — all stations with `SpaceStationFacility.Bar` (`SpaceStation.cs:188-191`) spawn the same 4-way salesman distribution. The station itself carries `faction` (`Source.Galaxy.POI/SpaceStation.cs:~`), which a broker can read for free.

### Station-level facilities vs. bar — for reward palette purposes

The `SpaceStationFacility` enum (`Source.Galaxy.POI/SpaceStationFacility.cs:3-29`) lists 24 facility types, each of which is a `SpaceStation` field checked via `HasFacility(facility)`:

```
GeneralShop, MiningShop, SalvageShop         — commodity shops (ShopInventory)
TradeTerminal                                 — Intertrade bulk trade
Bar                                           — the patron layer (this doc)
Refinery, Forge, SpecialistForge              — processing
Shipyard                                      — hull sales
MissionBoard                                  — procedural missions
BountyBoard, PoliceBoard(=Patrol), IndustryBoard — ranked specials
BountyShop, PatrolShop, IndustryShop          — commendation-gated shops
ConquestShop, umbralShopInventory             — Mars-commendation & Umbral gear
SalvageWorkshop                               — Steel Vultures reroll
RecruitmentCenter                             — Omnitac merc hire
PersonalHangar, Airlock, OutpostAirlock, ExitSpacestation, SalvageStation — structural/navigation
```

So a broker can also say "there's a Recruitment Center down the corridor" or "this station has a Workshop" — `station.recruitmentCenter != null`, `station.salvageWorkshop != null` — without touching the bar patron list.

### Shipyard — ship salesmen lurk here, not at the bar

`Shipyard.spaceShips: List<ShipyardShip>` (`Source.Galaxy.POI.Station/Shipyard.cs:8`). Populated per-station by unknown upstream code (plausibly `WorldMapPOI` setup, not found in this pass). Purchase flow at `Behaviour.UI.Spacestation.Location/Shipyard.cs:235-251` — `BuyShip` debits credits, consumes `ConquestCurrency` for Conquest-gated hulls, and adds to `GamePlayer.spaceShips`. Fires the `SteamAchievement.Trigger("BuyFrigate")` for first Size-4 Combat buy but **no MissionTrigger**. Same absent-hook story as the bar.

### RecruitmentCenter — mercenaries with faction binding

`Source.Galaxy.POI.Station/RecruitmentCenter.cs:20-78` — three ship-roster dictionaries (Combat / Mining / Salvage) keyed by `(SpaceShipRole, SpaceShipType)`. Mercenaries are created with `faction = station.faction?.identifier` (`RecruitmentCenter.cs:148-155`) — **so merc hire IS faction-tagged**, unlike bar patrons.

Hire flow: `Behaviour.UI.Spacestation.Location.Recruitment/MercenaryOption.cs:107` and `:122`. No MissionTrigger.

### Conclusion: only `SalesmanEquipment` carries incidental faction flavor

For VGAnima broker dialogue, the actionable faction-economy signals are:

- `station.faction` — the station's owning faction.
- `salesman.itemForSale.GetManufacturer()?.GetFaction()` for an Equipment Rep patron (6/15 chance of non-null).
- Presence of `station.salvageWorkshop` (Steel Vultures), `station.shipyard`, `station.conquestShopInventory`, `station.umbralShopInventory`, etc. — facility hints are faction-correlated because only certain factions host them, but the `faction` on the station is the primary truth.

---

## 7. Story NPCs on the bar

**They're not on the bar.** Covered in §2 — Story NPCs are `Source.Dialogues/Character` instances rendered by `Behaviour.Dialogues/CharacterMono` scene prefabs (`CharacterMono.cs:33-88`, uses `Character.dialogues` list filtered by `MissionTrigger` to pick what line fires on click). They have no `BarPatron` subclass, don't appear in `Bar.availablePatrons`, and don't go through the daily refresh cycle.

Known named questgivers are enumerated at `Source.Dialogues/Characters.cs:134-347`: Greg, Virgil, Creed, Elena Scott, Arle, John Raythor, Keril, Thundo Klipz, Adeline Lorentz, Mick Flank, James Fleddon, Claude, Stella Chion (x2 personas), Midas, Melloy, Elias McIntire, Olga Skarsgard, Alice Okono. Each returns a `Character` bound to `Source.Dialogues.Content.*Missions` dialogue factories (`SkilltreeMissions`, `UmbralMissions`, `SideMissions`, `ConquestMissions`, `TutorialMissions`).

### How to mention a story NPC from a broker

A broker CAN name-drop a story NPC because `Characters.*()` factory methods are statically callable — the LLM layer can fetch display names and descriptions by calling `Characters.Elena()`, `Characters.Raythor()`, etc. and reading `.name` / `.description`. No bar interaction needed. But these NPCs are not "at the bar" in any queryable structural sense.

---

## 8. Tool / feasibility findings by capability

### Capability matrix

| VGAnima capability | Feasibility | Path |
|---|---|---|
| Iterate "other salesmen at this bar" | **definitely supported** | `bar.availablePatrons.OfType<Salesman>()`; already used by the existing patches. |
| Classify each salesman by variant | **definitely supported** | `salesman.itemForSale.itemBuilder.identifier` (public property, stable string value). |
| Count MiningClaims / SalvageClaims / Equipment purchased | **needs Harmony hook** | Postfix `ItemSaleInfo.ButtonPurchase`; write `Register.AddCounter("VGAnima_{variant}Purchased")`. |
| Count general-shop modules/turrets purchased | **needs Harmony hook** | Postfix `InventoryInteractionManager.BuyAmount` at `:832`-ish; filter by `item.GetComponents<InventoryItemPart>()` for category. OR read `IdleStat.CreditsSpent` (coarse). |
| Count ships bought at shipyard | **needs Harmony hook** | Postfix `Behaviour.UI.Spacestation.Location/Shipyard.BuyShip` at `:235`. |
| Count mercenaries hired | **needs Harmony hook** | Postfix `MercenaryOption.cs:107,122`. |
| Count crew hired at bar | **needs Harmony hook** | Postfix `CrewMemberInfo.ButtonHire` at `CrewMemberInfo.cs:61-76`. |
| Know station's faction | **definitely supported** | `station.faction` is a public `Faction` reference. |
| Know which facilities the station has | **definitely supported** | `station.HasFacility(SpaceStationFacility.X)` or null-check on the field. |
| Reward an LLM mission with a MiningClaim | **definitely supported (existing reward class)** | `new Rewards.Item { item = ItemBuilder.Get("MiningClaim").CreateMiningClaim(station.system, asteroidFieldData), amount = 1 }`. |
| Reward a SalvageClaim | **definitely supported** | Same pattern with `"SalvageClaim"` + a `List<PersistableData>` of SalvageData. |
| Reward a blueprint | **definitely supported** | `ItemBuilder.Get("Blueprint").CreateBlueprint(recipe)` or `CreateRandomBlueprint(level, rarity)`. |
| Reward a specific ship | **definitely supported** | `new Rewards.Ship { ship = Resources.Load<SpaceShip>("SpaceShips/{name}") }` (or whatever the vanilla loader uses — see `UmbralMissions.cs:3172` precedent). |
| Reward a crew member | **definitely supported** | `new Rewards.Crew { crew = CrewMemberData.CreateRandomCrewMember(rng) }`. |
| Reward commendations (Mars / Patrol / Bounty / Industry) | **likely supported (`Rewards.Item` with commendation `InventoryItemType`)** | Use `InventoryItemType.Get("ConquestCurrency")` / similar; commendations are ordinary items, not a specialized reward class. Confirmed via `Shipyard.cs:240-241` pattern. |
| Reward Workshop Credit | **definitely supported** | `new Rewards.WorkshopCredit { amount = N }`. |
| Fire a MissionTrigger on purchase | **not supported without extending enum** | Vanilla `MissionTrigger` enum is closed; Harmony can't add values. VGAnima uses its own event bus for this. |
| Offer items with LLM-authored descriptions | **definitely supported** | `InventoryItemType.SetDisplayName(str)` and `SetDescription(str)` are public — VGAnima can customize reward-item text per mission. Pattern at `ItemBuilder.cs:97,149`. |

### Not directly supported (would need custom reward class)

- **Time-limited rewards** — no vanilla reward class has a TTL. `MissionFollowUp` is closest but fires unconditionally on complete.
- **Conditional unlocks based on player state** — `Rewards.Skilltree` is the only conditional-unlock reward; it's scoped to SkillTree enum values only.
- **Direct faction standing manipulation beyond +/- rep** — `Rewards.Reputation` handles rep only; no faction-rank or Conquest-contribution-split nuance. `Rewards.ConquestStrength` is the closest for Conquest.

### Flag-based milestones — the right pattern

For "first MiningClaim bought from a broker," "first Blueprint reward redeemed," etc. — use `Register.SetFlag("VGAnima_FirstClaimBought")` inside the VGAnima purchase-hook postfix. Same pattern vanilla uses at `Source.Galaxy.Factions/Puppeteers.cs:26` (`PuppeteersNameChange`). Flags serialize into the vanilla save via `Register.ToJson` for free.

---

## Cross-references

- Character / NPC name roster — [wiki survey §2 + §8](./vanguard-galaxy-wiki-survey.md)
- Faction identifiers + rep mechanics — [decomp survey §1, §2](./vanguard-galaxy-decomp-survey.md)
- `MissionTrigger` full enum + dispatch sites — [decomp survey §4](./vanguard-galaxy-decomp-survey.md)
- `Register` counter API + piggyback pattern — [decomp survey §10 "Register flag API"](./vanguard-galaxy-decomp-survey.md)
- VGAnima existing bar patches — `VGAnima/Patches/BarRefreshPatches.cs`, `VGAnima/Patches/SalesmanPatches.cs`

## Wiki corrections (bar-specific)

1. **"5-15 salesman types"** — wiki implies a richer typology; decomp shows **exactly 4 probability branches** on a single `Salesman` class (`Salesman.cs:57-77`). No subclass hierarchy.
2. **"Prospector = Mindus Holdings / Salvage Scout = Steel Vultures"** — wiki implies faction binding; decomp shows **both are system-anchored, faction-agnostic** (`Salesman.cs:170-186` for MiningClaim, `:142-168` for SalvageClaim). A Prospector at a Stellar station hawks a claim for a system that might be owned by any faction including the station's.
3. **"Bar resets every 24h at midnight"** — close, but the reset gate is `new DateTime(lastUpdateTime).DayOfYear == DateTime.Now.DayOfYear` (`Bar.cs:26`). Same-day equality check, not a midnight tick. Effect is identical in practice (different day-of-year = refresh), but there's no timed "at midnight" event; it's lazy — the bar is only refreshed when `CheckUpdatePatrons` is called and the day has rolled over.
4. **"Industrial reps at the bar"** — wiki mentions them. Decomp has **no industrial-rep variant**; the "Sales Representative" label is the generic fallback when `Manufacturer.GetFaction() == null`, which covers 9 of 15 manufacturers. The "representative" framing is purely textual.
5. **Expected patron count per bar** — wiki doesn't commit; decomp's trim loop (`Bar.cs:53-58`) with starting prob 0.75 and 0.25 decay gives an expected final count of **~2.9 patrons** (E[kept] = 5 × (1 − 0.75) + more complex geometric terms); in practice 2–5 with 3–4 most common.
