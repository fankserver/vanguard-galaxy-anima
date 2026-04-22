# Vanguard Galaxy Wiki Survey

A catalog of player-facing mechanics, factions, locations, and events drawn from the Fandom wiki, oriented toward building a broker-memory / player-journal system for VGAnima.

**Survey date:** 2026-04-22 (wiki as of patch 0.8.0.11).
**Method:** Fetched page wikitext via the MediaWiki `action=parse` API for ~60 pages. See "Sources" at the bottom for the complete list of cited pages — each one can be read at `https://vanguard-galaxy.fandom.com/wiki/<PageName>`.

---

## 1. Player Activities

The game exposes four core "activity types" that frame everything else, plus a handful of specialized modes. Triggers are nearly always location-based (a POI, a station, a mission accepted) or item-mediated (consumables/keys).

- **Combat** (ship-to-ship, station assault, POI clearing). *Triggers:* enemy POI entered, mission accepted, reputation sufficiently hostile. *Journal value:* where/whom the player has fought, how often, and how hard.
- **Mining** (asteroid fields: surface + core layers). *Triggers:* asteroid field entered, Mining Claim consumed, Rogue Comet random POI. *Journal value:* identifies "miner" archetype; flags Motherlode visits; logs Crystal drops (exotic crafting).
- **Salvage** (wreck fields: surface + structural; Items layer for loot). *Triggers:* wreck entered, Salvage Claim consumed, Battle Wreckage random POI, post-combat wrecks from Combat Salvager skill. *Journal value:* flags Graveyard visits, Workshop usage, legendary-gear farming via Locator Beacon.
- **Trade** (bulk goods via Intertrade Network terminals, station-to-station arbitrage). *Triggers:* Trade Terminal interaction, Freighter cargo loaded. *Journal value:* identifies "trader" archetype; aggregate profit.
- **Smuggling / piracy** — special Umbral-enabled variant: scan foreign ships with *Cargo Scanner Bot*, mask with *Decoy Transponder*, track with *Tracking Tag Bot*, destroy, loot. *Journal value:* morally distinct activity; identifies "pirate" playstyle.
- **Refining** (Refinery service): ores/scrap → refined materials. Global material storage. *Journal value:* volume refined = industrial commitment.
- **Crafting** (Forge service): blueprint + refined materials → turrets, modules, consumables, ammo, boosters, quest items. *Journal value:* blueprints learned, exotic (purple, crystal-input) items crafted.
- **Workshop operations** (Steel Vultures only): reroll substats, extract blueprints, swap aspects, level-up items, upgrade rarity. *Journal value:* hardcore min-max signal.
- **Delivery / escort / bribery / dialog missions** — standard mission-board missions where a station asks the player to move cargo, escort a ship, or pay off a contact.
- **Patrol, Bounty Hunt, Industrial Ops** — three "Special Missions" at Canisec / Orsanon Security / Forge Industries respectively (see §9).
- **Umbral Missions** — daily repeatables unlocked after infecting Conquest stations (see §9).
- **Exploring / "Distress Calls"** — Frontier random POIs; ambush or genuine help.
- **Station defense / assault** — clearing hostile stations (War Zones, Dread Ports, Meridia bases).
- **Making Peace / Declaring War** — player-driven faction state changes via the Reputation UI and Embassy "conciliatory missions."
- **Hiring mercenaries** — Omnitac Agency stations; C/M/S mercenary types.
- **Opening Locked Containers** — Steel Vultures service; gambling for modules, skill points, more containers.

Sources: [Missions](https://vanguard-galaxy.fandom.com/wiki/Missions), [Combat](https://vanguard-galaxy.fandom.com/wiki/Combat), [Mining](https://vanguard-galaxy.fandom.com/wiki/Mining), [Salvage](https://vanguard-galaxy.fandom.com/wiki/Salvage), [Trade](https://vanguard-galaxy.fandom.com/wiki/Trade), [Refinery](https://vanguard-galaxy.fandom.com/wiki/Refinery), [Forge](https://vanguard-galaxy.fandom.com/wiki/Forge), [Workshop](https://vanguard-galaxy.fandom.com/wiki/Workshop), [Consumables](https://vanguard-galaxy.fandom.com/wiki/Consumables).

---

## 2. Factions

The wiki lists 16 factions (not 18). Display names only — **no internal identifier strings are exposed on the wiki; pull those from the game's code/assets**. Rep scales identically for all: positive ladder Neutral → Cordial → Friendly → Respected → Distinguished → (Exalted, currently unreachable); negative ladder Neutral → Wary → Hostile → Despised → Hated → Absolute Threat. Distinguished = 30,000 rep; max hostile = −50,000. Rep losses from attacking: −200 small / −800 large per ship destroyed. [Faction Reputation](https://vanguard-galaxy.fandom.com/wiki/Faction_Reputation).

**Major (drive Conquest / Faction War):**
- **Stellar Industries** — megacorporation, Roman-themed ship names, firepower focus. Ships: Stellar Industries brand. *HQ lead:* Victor Hale (CCO). *Embassy:* Adeline Lorentz.
- **Luminate Combine** — "cybercratic cult," worships the Oracle and drone-swarm. Ships: Spirit Design (drone-heavy, religious names). *HQ lead:* Triane Solis. *Embassy:* Arle. *Frontier:* Oron (Machine Shepherd).
- **Kolyatov Collective** — socialist dictatorship (the Kolyatov dynasty profits). Ships: RedStar Forge (armor-heavy, Soviet names). *HQ lead:* Mikhail Kolyatov (High Commissar). *Embassy:* Keril. *Frontier:* Sergio Weisartz.
- **Darkspace Compact** — original owners of Ara Martis + Penumbra; invaded by the three majors; at civil war with Meridia's Chosen. Ships: Obrix (vertically oriented). *Embassy:* Midas (smuggler, calls player "Milky").

**Minor (can break into Conquest with player aid):**
- **Mindus Holdings** — mining conglomerate. Owns Motherlode gate keys. Ships: Mindus Manufacturing (tool-themed). *Embassy:* Waldo Everson. *Frontier:* Elias McIntire.
- **Steel Vultures** — salvage clan. Owns Graveyard gate keys + Workshop service + Locked Container opening. *Embassy:* Lynn Bree. *Frontier:* Alice Okono, Thundo Klipz, Mick Flank.
- **Stranded** — survivors of the Great Gate malfunction, trapped in Galaxy's Edge until the player's prologue reconnects them. Ranks use "Bloodbound / Pact Binder / Steward" flavor. *Embassy:* Mirthe Coman. *Prologue characters:* Greg, Virgil, Creed, Elena Scott.
- **Corsair Syndicate** — pirates; default antagonist through Frontier. Owns Dread Port gate keys. Ships: Marade Wharf (chop-shop / stolen, pirate names). *Embassy:* Horace the Red.
- **Umbral Reach** — mysterious hacker faction; infects stations, spreads infection across gate lanes, offers daily missions + hidden shop once infected. *Employer:* Mysterious Employer. *Spymaster:* Stella Chion.

**Pacifists / pacifiers (no Conquest participation, specialized services):**
- **Intertrade Network** — bulk-goods trade terminals. *Frontier:* Margot Cash (Economy skill), Edar Thopter (Fast-Lane Travel).
- **Forge Industries** — refining/crafting, issues Industrial Ops. No mission board (Industrial Ops is the only rep source).
- **Canisec** — police; runs Patrol missions; owns gate defenses. *Frontier:* Sergeant Ogolin (Defense skill), Etienne Briggs (Patrol intro), Gabriel Ramos (Conquest gate pass). *Conquest:* Cade Callahan.
- **Orsanon Security** — bounty hunters; runs Bounty Hunt missions. *Frontier:* Olga Skarsgard (Combat skill), Amalia Rodriguez (Bounty intro).
- **Omnitac Agency** — mercenary hire. *Frontier:* Brenda Diamond. *Embassy:* Anton Havel.

**Unknown / inscrutable:**
- **Meridia's Chosen** — hostile fanatics, no communication, no embassy; cannot be befriended. Becomes default antagonist in Conquest. Ships: Obrix.
- **Void Drifters** — nomad smugglers; no Conquest role. Ships: Kharon Forgeworks (1 known vessel, the Eclipse). *Characters:* James Fleddon, Claude.

**Conquest reputation** is separate and grants flavored ranks per faction (see §7). Sources: [Factions](https://vanguard-galaxy.fandom.com/wiki/Factions), all faction pages linked above, [Conquest Reputation](https://vanguard-galaxy.fandom.com/wiki/Conquest_Reputation).

---

## 3. Locations & Geography

Hierarchy: **Galaxy (Canis Majoris) → Sector → Subsector → System**. Three sectors explorable:

- **Galaxy's Edge** — prologue tutorial sector. Levels 1–10. Fixed locations: *Driftlight Station* (Ravon) with Greg; *Point Station* (Orbitan) with Virgil; *Genesis Station* (Balam) with Creed + Elena Scott. Connected to Frontier only via the player-rebuilt gate.
- **Frontier** — sandbox sector, first half of Canis Majoris. Levels 10–30. Subsectors are **procedurally generated** — no canonical names. Contains special procedural system types:
  - **Motherlode systems** — dense asteroid fields, Mindus-gate-keyed.
  - **Graveyard systems** — dense wrecks, Steel-Vultures-gate-keyed.
  - **Dread Ports** — Corsair pirate strongholds, key looted from combat POI.
  - **War Zones** — two factions at war; no rep loss fighting there.
  - Each type rewards a bonus Skill Point for first clear.
- **Conquest (Ara Martis)** — Levels 30–60. **Preset subsectors** with stable names:
  - **Lux Arctos** (N), **Lux Magna** (M), **Lux Australis** (S) — the three embassy subsectors (golden-outlined stations).
  - **Ara Martis** — the main conquest subsector, ~100 systems, no refineries/forges.
  - **Penumbra** — Darkspace Compact territory, gate-keyed at level 55.
  - **Lucifer** — Meridia's Chosen homeland, effectively unreachable.

**Point-of-Interest types** (`Navigation`): Gates, Stations, Asteroid fields, Salvage fields, Combat zones, Quest POIs. **Random POIs:** Rogue Comet (mining, ~45min TTL), Battle Wreckage (salvage, ~45min TTL), Corsair Warband (combat, ~45min TTL), Distress Call (travel-triggered mini-encounter).

**Station types** (by faction-service profile): friendly faction station, neutral, hostile; Conquest Station (flippable ownership, Fleet Power tracked); Headquarters Station (silver outline, per-faction); Embassy Station (gold outline, in Lux subsectors); Motherlode/Graveyard non-conquest stations; Umbral-infected station (unlocks Umbral Shop + daily).

*Journal value per location:* subsector identity (Lux/Ara Martis/Penumbra is named), first-visit to typed systems (Motherlode/Graveyard/Dread Port/War Zone) is a durable milestone. Individual Frontier systems are procedural — a broker can only say "you've been operating in the [subsector-named] region," not "you've been to X system" (unless the game records the procedural name).

**Gap:** Frontier subsector names are procedurally generated and never enumerated on the wiki. Conquest subsector names are fixed but individual system names within them are not listed.

Sources: [Navigation](https://vanguard-galaxy.fandom.com/wiki/Navigation), [Conquest Zone](https://vanguard-galaxy.fandom.com/wiki/Conquest_Zone), [Stations](https://vanguard-galaxy.fandom.com/wiki/Stations), [Great Gate](https://vanguard-galaxy.fandom.com/wiki/Great_Gate).

---

## 4. Items & Economy

**Item categories:** Turrets (combat red / mining blue / salvage yellow), Modules (reactor, engine, scanner, tractor beam, hull kit, armor, shield, drone bay, torpedo bay), Boosters, Consumables, Ammo, Refined Materials, Junk, Ores, Scrap.

**Rarities:** White (Standard), Green (Enhanced), Blue (High Grade), Purple (Exotic — crystal-required), Orange (Legendary — no recipes, drop-only).

**Ore types** (8): Titanium, Oxide, Silicon, Tungsten, Carbon, Iridium, Platinum, Astatine. Ore asteroids: Cerrax, Neonine, Orminite, Tachyline, Torvenite, Xylenite, Zorinite, Amberic, Baryth, Caldras, Drakitium, Lithar, Onexel, Argenthyte, Dromium, Halcyonite, Xenorite. [Mining](https://vanguard-galaxy.fandom.com/wiki/Mining).

**Scrap types:** Fragment → Shard → Scrap → Chunk → Slab. **Structural salvage drops:** Titanium Plate, Tungsten Carbide, Graphite Wafers, Oxide Canister, Skeleton Frame, Hull Beams, Bulkhead, Computing Unit. Byproducts from "Clean Retrieval" skill: Circuit Board, Conduit Cell, Durable Alloy, Universal Catalyst, Substable Wiring. [Salvage](https://vanguard-galaxy.fandom.com/wiki/Salvage).

**Crystals** — rare mining byproduct, required for Exotic (purple) Forge crafting.

**Blueprints** — per-rarity (need 4 to craft white/green/blue/purple of the same item). Sources: combat loot, salvage loot, Locked Containers, Forge Industries *Industry Shop* (purple blueprints), Workshop extraction.

**Currencies:**
- **Credits** — universal.
- **Workshop Credit** — Steel Vultures Workshop currency; gained via Workshop daily request or x4-price turn-ins.
- **Commendations / Tokens:**
  - **Patrol Commendation** (Canisec)
  - **Bounty Commendation** (Orsanon)
  - **Industry Commendation** (Forge Industries)
  - **Mars Commendation** (Conquest-wide; any participating faction; required to buy size-5+ ships and from Conquest Shops / Umbral Shops).
- **Dog Tags** — looted combat consumable; right-click at a commendation-issuing station to trade for its tokens.
- **Gate Keys / Gate Passes** — consumable items that unlock specific gates; jettison from cargo near gate.

**Bulk Goods** — Intertrade-exclusive trade-terminal items (Liquid Alloys, Body Armors, Artificial Organs, Simreality Headsets mentioned as examples). Station-local price ranges with 20-minute price ticks.

*Journal value for items/economy:* total credits earned + by activity split; Crystal count (exotic-craft signal); blueprint count; token reserves (indicates late-game grind progress); Metafiber possession (main-story item).

---

## 5. Ships & Loadouts

**Classes by size:**
- **Size 1–2 (Small)**: Cutter, Gunship, Mining Skiff, Hewer, Salvage Skiff, Scow, Courier, Ferry.
- **Size 3–4 (Medium)**: Corvette, Frigate, Dredger, Breaker, Scrapper, Wrecker, Hauler, Freighter.
- **Size 5 (Large)**: Destroyer, Harvester, Reclaimer, Carrack.

**Role bonuses:** Combat hulls = +30% Combat Power + 3% crit; Mining / Salvage hulls = +30% Power + 20% Yield; Trade = Economy-skill driven. **Size bonuses** add weapon range.

**Modules:** Reactor, Engine, Scanner, Tractor Beam, Hull Kit (universal); Weapon hardpoints (S/M/L), Armor Plating XOR Shield Generator, Drone Bay XOR Torpedo Bay.

**Notable / unique ships** (journal-worthy because ownership is a status signal):
- **Eclipse** (Kharon Forgeworks) — quest-only Destroyer awarded for Umbral story completion.
- **Terravex** (Obrix) — quest-only Destroyer awarded for Meridia-removal campaign with Midas.
- Faction-locked Destroyers: Sparta (Stellar), Inquisitor (Luminate), Varyag/Varyag-R (Kolyatov), Hurricane/Typhoon (Akai/Orsanon), Rypper (Corsair), Maul/Sledge (Mindus Harvesters), Axcetor/Gryp (Utixon Reclaimers), Paktar (Corsair Carrack), Drometar (Utixon Carrack).

**Archetype loadouts** are player-driven: mining ship + combat drones for defense; salvage ship + combat turret; Forge's *Industrial Ops* rewards a Mining+Salvage+Combat mixed loadout.

Full roster: [Ship List](https://vanguard-galaxy.fandom.com/wiki/Ship_List). Manufacturers: [Ship Manufacturers](https://vanguard-galaxy.fandom.com/wiki/Ship_Manufacturers).

---

## 6. Crew & Companions

**Crew size is a ship stat** (1–5 per hull class). The wiki exposes very little about crew slot mechanics. Named companions recruited through story missions:
- **Elena Scott** — Stranded engineer, first crew; skills in Autopilot / Combat / Industrial. Joined after "Salvage the Derelict" prologue mission.
- **John Raythor** — Umbral-hired captain, rescued from Luminate prison; skills in Combat / Industrial. Joined after "Lost Crew."

Other characters are station-anchored personalities (questgivers, skill trainers, embassy heads, shop proprietors) rather than crew — see §2 for the full character roster by faction. The wiki has a `Category:Characters` with ~35 named NPCs.

**Gap:** No documented "crew role / slot / skill-slot" system beyond per-character skill notes; crew mechanics appear under-documented on the wiki.

---

## 7. Skilltrees & Progression

**Level caps by zone:** Prologue 10 / Frontier 30 / Conquest 60. Skill-point caps: 20 / 60 / 120. All skill points are fully redistributable. Skill loadouts are global and assignable per-ship.

**Eight skill trees:**
1. **Mining** — taught by Elias McIntire (Mindus Holdings, Frontier).
2. **Salvaging** — taught by Alice Okono (Steel Vultures, Frontier).
3. **Combat** — taught by Olga Skarsgard (Orsanon Security, Frontier).
4. **Defense** — taught by Sergeant Ogolin (Canisec, Frontier).
5. **Drones** — taught by Oron (Luminate Combine, Frontier).
6. **Economy** — taught by Margot Cash (Intertrade Network, Frontier).
7. **Industrial** — unlocked via Stellar Industries / Forge assignments (Brandon Wallace forgemaster quest).
8. **Autopilot** — related to ECHO.

Tier 4+ unlocked via **Sergio Weisartz** (Kolyatov, "Learning Ability" mission, 2 skill points).

**Bonus skill-point sources:** Motherlode shop, Graveyard shop, Dread Port destroy-first-time, War Zone destroy-first-time, HQ stations, Locked Containers.

**Mastery** — separate from skills; gained passively by doing (+0.5% per level of Mining Power, Drone Power, Combat Power, Refine/Craft speed, etc.). Capped at character level.

**Ranks** — three retirable ranks, earned through extreme-difficulty specialized missions:
- **Patrol Officer Rank** (Canisec — every 2 extreme missions = +1)
- **Bounty Hunter Rank** (Orsanon — every 1 extreme mission = +1)
- **Industrial Magnate Rank** (Forge — every 2 extreme missions = +1)

Retirement returns tokens; useful when enemies scale beyond the player.

**Conquest Reputation ranks** (per faction, flavored):
- Luminate: Seeker → Initiate → Data Acolyte → Drone Devotee → Conduit of the Swarm → **Oracle's Chosen**.
- Kolyatov: Conscript → Comrade → Comissar → Defender of Solidarity → Revolutionary Marshal → **Hero of the People's Glory**.
- Stellar: Intern → Associate → Senior Associate → Operations Manager → Executive Director → **Sector Partner**.
- Mindus: Chiseller → Pickaxe Wielder → Foreman → Chief Engineer → Ore Connoisseur → **Ultra Miner**.
- Steel Vultures: Hullpicker → Wreckhand → Steel Reclaimer → Ship Ripper → Master of Wrecks → **Scraplord**.
- Stranded: Companion → Bloodbound → Steward → Pact Binder → Covenant Captain → **Bacon of Hope** [sic].
- Darkspace: Worm → Darkspace Drifter → Fiberguard → Nightblade → Black Lance → **Canis Warlord**.
- Corsair: Raider → Cutthroat → Syndicate Corsair → Blood Reaver → Death Captain → **Tyrant**.
- Umbral: String → Infiltrator → Cloakbearer → Untracked → System Reaper → **The Hand of Umbral**.

These titles are **high-value journal flavor** — a broker greeting a "Scraplord" or "Oracle's Chosen" is doing identity-aware dialogue for free.

Sources: [Skills](https://vanguard-galaxy.fandom.com/wiki/Skills), [Skill Missions](https://vanguard-galaxy.fandom.com/wiki/Skill_Missions), [Mastery](https://vanguard-galaxy.fandom.com/wiki/Mastery), [Conquest Reputation](https://vanguard-galaxy.fandom.com/wiki/Conquest_Reputation).

---

## 8. Story Arcs & Notable Events

Full spoilers at [Story Thus Far](https://vanguard-galaxy.fandom.com/wiki/Story_Thus_Far) + [Story Missions](https://vanguard-galaxy.fandom.com/wiki/Story_Missions).

- **Prologue — Stranded on the Edge:** Great Gate malfunctioned; Captain stranded in Galaxy's Edge among the Stranded. Rebuild the local gate system, meet Greg → Virgil → Creed → Elena Scott, obtain ECHO AI core, defeat Corsair pirates, reach Frontier.
- **Story Arc I — The Constructor Drone Fleet:** Meet Mysterious Employer. Recover Command Cortex (rescue John Raythor from Luminate, track Keril to Kolyatov, salvage Cortex via Thundo Klipz). Obtain Constructor Drone Schematics from Adeline Lorentz under false pretenses. Steel Vultures provide labor (Mick Flank). Climax: "Scrap the Drones."
- **Story Arc II — The Void Drifters:** Meet James Fleddon, defend Drifter station, Claude introduces Metafiber smuggling, meet Stella Chion (posing as Stellar Manager), meet Midas. Ends with Station Assault → first Metafiber.
- **Story Arc IIIa — Darkspace Compact:** Help Midas against internal Compact rivals (Meridia's Chosen forming). Ends with wormhole defense.
- **Story Arc IIIb — Umbral Reach:** Metafiber experiments with Stella Chion. Umbral hacks its first station. Drone Assistance + clearing construction site.
- **Story Arc IV — Secondary Benefits (Conquest entry):** Meet Cade Callahan. **Irreversible branch:** player picks ONE of Stellar / Luminate / Kolyatov to side with ("Internship" / "Alignment" / "For the Collective!"). This is a **permanent identity fork**.
- **Story Arc V — Infection:** Mysterious Employer returns with Umbral Hacking Multitool. Spread Umbral infection across Ara Martis, one station at a time. Stella reveals Umbral Spymaster identity.
- **Story Arc VI — Crusade (with Midas):** Expel Meridia's Chosen from Ara Martis. Missions: "No Asteroids for Meridia," "Meridia's Elite" (reward: **Penumbra Gate Pass**), "Material Trade" (reward: **Terravex Destroyer**), "Eliminate Meridia's Chosen."

**Named characters bound to events:** Keril (Cortex-carrier), Raythor (prison rescue), Thundo Klipz, Mick Flank, Adeline Lorentz, Stella Chion (twice — Stellar, then Umbral), Midas (Crusade partner), Victor Hale / Triane Solis / Mikhail Kolyatov (Arc IV branch heads), Cade Callahan (Conquest intro).

Meta-lore: the **Oracle** of Luminate Combine is referenced but never seen; the **Great Gate**'s builders are unknown; **Umbral's true goals** and whether ECHO is itself Umbral are explicit open questions on the wiki.

---

## 9. Mission Archetypes

- **Story Missions** — fixed, one-time, spoil the main arc. See §8.
- **Skill Missions** — fixed per faction; first clear unlocks a skill tree or grants Skill Points. "Assignment" follow-ups are repeatable for standard rewards (Yearning for the Mines, Among the Vultures, Line of Defense, Drone Whisperer, Trading Guru, Escort Services, Time is an Illusion, Your Days are Numbered, Volunteer Patrol, Learning Ability).
- **Mission Board missions** — procedurally generated at almost any station; reward Credits / Rep / Items / optionally Faction Currency / Workshop Credit. Up to 20 concurrent. Board resets every 5 minutes. Types glimpsed in docs: mining missions, salvage missions, combat clear missions, delivery missions, "buy X bring Y" missions, escort missions. **Gap:** the wiki does not exhaustively catalog Mission Board subtypes.
- **Special Missions** (ranked, retireable):
  - **Patrol** (Canisec, 5 waves, hostile-faction enemies). Difficulties: Normal / Elevated / Extreme. Rewards Patrol Commendations.
  - **Bounty Hunt** (Orsanon, 5 waves ending with named target captain in a larger ship). Same difficulty ladder. Rewards Bounty Commendations. Can spawn legendary-gear wrecks requiring Locator Beacon retrieval.
  - **Industrial Ops** (Forge, station defense + mining + salvage + crafting Operational Supplies under endless enemy waves). Rewards Industry Commendations. Forge has no mission board — this is the only way to build Forge rep.
- **Umbral Missions** — once-per-day repeatables at infected Conquest stations. Typically boost station infection +35–45% *and* pay thousands of Mars Commendations + millions of Credits per mission.
- **Conquest missions** — support missions (mining/salvage/delivery/combat) that accrue **Fleet Power** for the chosen faction and Mars Commendations for the player. Active support = attacking neighboring faction stations to zero Reinforcements.
- **Conciliatory missions** — at enemy Embassy while player's rep is negative; grants 1000–1500 rep per mission until neutral → "Make Peace."

Sources: all `Missions / Skill Missions / Patrol / Bounty Hunt / Industrial Ops / Umbral Missions` wiki pages.

---

## 10. Rare / Flavor Systems

- **Crystals** — mining byproduct; only path to crafting Exotic (purple) gear. [Mining](https://vanguard-galaxy.fandom.com/wiki/Mining).
- **Dog Tags** — combat-drop consumable; right-click at Canisec / Orsanon / Forge station (shop closed) → trade for their commendations.
- **Locator Beacon** — deployable consumable marking a location; required to come back for oversized cargo or legendary-gear wrecks.
- **Cargo Scanner Bot / Tracking Tag Bot / Decoy Transponder** — Umbral-shop consumables enabling stealth piracy. Decoy Transponder also prevents rep-loss/gain when fighting in Conquest.
- **Umbral Hacking Tool** — infects Conquest stations. First one is guaranteed; later ones bought from Umbral Shop.
- **Locked Containers** — Steel-Vultures-opened random loot (Credits / ores / refined / modules / turrets / **Bonus Skill Points** / more containers). Certain skills trigger container drops (*Smugglers Stash* mining, *LB-RTR Bot* salvage).
- **Treasure POIs** — wiki doesn't document specific ones by name, but legendary gear can be harvested from bounty-hunt + patrol Extreme-difficulty wrecks.
- **Fast-Lane Travel** — Edar Thopter unlocks via "Time is an Illusion"; 700% interstellar speed, skip intermediate systems.
- **Gate Keys / Gate Passes** — per-gate consumables for Motherlodes, Graveyards, Dread Ports, War Zones, Penumbra, Conquest Embassy↔HQ gates.
- **Fuel Cells (Plasma 200% / Ion 300%)** — toggle speed boosts, crafted in Forge or bought.
- **Metafiber** — story-critical substance from Darkspace; cause of the invasion; obtained as reward in Arc II.
- **Bonus Skill Points** — consumable items that reinvest in the skill-point cap; accumulated even over cap for future content.
- **Workshop Daily Request** — same across all Workshops; material requested pays 6x in Workshop Credit.
- **Environmental hazards** — Radiation, Cold, Heat, Mines, Corrosion (+ implied others). Hazard Protection Boosters halve remaining exposure per stack (no full immunity).
- **Aspects** — modular item enchantments (Critical Attenuation, Extended Drone Bay, Gamma Ward, Hardened Plating, Microgenerators, Microthruster Array, Operational Reserves, Reload Subroutine, Shielding Subsystem, Solar Powered, Structural Supports — green. Crisis Protocol, Firestarter, Frozen Core, Hardened Exterior, Oversized Drone Bay, Rangefinder, Repair Nanites, Punch Through, Weakpoint Scan — purple). Extractable at Workshop (destroys source item).
- **Home Station** — any station can be bookmarked; ECHO + autopilot uses it as anchor.
- **Bar** — resets every 24h at midnight; hosts recruitable officers, prospectors (mining claims), salvage scouts (salvage claims), industrial reps, and "Slick Entrepreneurs" (rare high-value items).

---

## 11. Interesting Events to Remember (The Journal Payoff)

Below are the 25 event categories that most discriminate player identity and give a broker something juicy to react to. Formatted as: **event type** — *example broker line* — **journal field implications.**

1. **First sector transition** — *"So you finally made it out of the Edge. Took you long enough."* → boolean `prologue_completed`, optional timestamp.

2. **First Motherlode crack** — *"Word is you cracked a Mindus Motherlode over in the Torridus Reach. That's a drill-jockey's milestone."* → first-visit flag per special-system type, with subsector name.

3. **First Graveyard harvest** — *"A Vulture in your flight recorder says you've been picking wrecks in the proper places."* → same as above.

4. **First Dread Port destroyed** — *"I hear you kicked in a Corsair's back door. That takes stomach."* → first-kill, special combat milestone.

5. **First War Zone station destroyed** — *"You tipped the scales in [system]. Both sides noticed."* → one-offs per War Zone; faction-neutral rep-less combat signal.

6. **Reached Penumbra** (level-55 gate pass) — *"Darkspace doesn't let just anyone past the Penumbra Gate. Welcome to the deep end."* → late-game progression boolean.

7. **Conquest Arc-IV branch taken** (Stellar / Luminate / Kolyatov) — *"I heard the [Stellars / Oracle / Kolyatov] have your ear now. Changes what jobs I bring you."* → **one-time, irreversible** enum. Highest-signal event in the whole game.

8. **Umbral first station infected** — *"You planted the first worm. Brave, or reckless."* → boolean + station ID.

9. **Umbral infection milestones** (e.g. ≥5, ≥20, 100% of Ara Martis) — *"Half of Ara Martis is running on ghost code now. People are starting to feel it."* → counter thresholds.

10. **Peace declared with a faction** (first successful conciliation) — *"Making peace with the Syndicate? Didn't see that coming."* → per-faction war/peace log with timestamps.

11. **War declared on a faction** — *"You torched two of their stations last week. They're not forgetting that."* → same.

12. **Current Conquest title per faction** — *"Scraplord. Good title. Want work that lives up to it?"* → live rank per faction; brokers reference by title directly.

13. **Owns Eclipse / Terravex Destroyer** — *"Is that a Kharon hull? I've only heard rumors."* / *"A Terravex? Midas must really like you."* → hull ownership fingerprint.

14. **Rank retirement** (Patrol / Bounty / Industrial) — *"Retired from the Hunt, eh? Rank gets heavy."* → retirement count; signals burnout / re-set.

15. **Named bounty target defeated** — *"You took down [Bounty Target Name]. Their crew has opinions about that."* → last N bounty kills.

16. **Meridia's Chosen pushed out of Ara Martis** (Arc VI completion) — *"I heard you rode with Midas. The Compact owes you one."* → campaign flag + partner-NPC tag.

17. **Umbral Spymaster contact** (Stella reveals identity) — *"You've seen what Stella really is. Most folks haven't."* → lore boolean.

18. **Metafiber possession** — *"Heard a whisper you're holding a canister of the stuff. Don't let just anyone know that."* → dangerous lore token.

19. **Exotic item crafted (Crystal-gated purple)** — *"Crystal-forged gear. You're not messing around."* → craft count by rarity.

20. **Workshop blueprint count** (e.g. ≥10, ≥50, ≥100) — *"Your library's getting thick. That's a dedicated forgemaster's library."* → collection counter.

21. **Faction-currency milestones** (e.g. first 1000 Mars Commendations, first size-5 ship bought with tokens) — *"Bought a Destroyer on Mars chits. Big day."* → currency-spend events.

22. **Activity dominance per subsector** (last N hours) — *"You've been hunting Corsairs in [subsector] hard lately."* / *"Half the ore in this subsector came out of your hold."* → **sliding-window activity aggregates**, the most "broker-natural" hook.

23. **Dog Tag turn-ins** — *"You've been trading tags here. People talk."* → counter.

24. **Locked Container streak** — *"Last three Vultures openings netted you a skill point. Lucky or cheating?"* → gambling-arc flavor.

25. **Mercenary contracts (with Omnitac)** — *"Still have a Havel contractor in your flight? He speaks well of you."* → hired-crew identity.

Additional minor but fun:
- First time trading Bulk Goods at profit / loss.
- First successful Umbral-mode piracy (Cargo Scanner Bot used on a foreign cargo vessel).
- Distress Call answered vs. ignored (moral flavor).
- Home Station choice (the bar where a broker works is "your bar" if it's home).
- First Industrial Ops supply completed.

---

## Gaps in the Wiki

Explicit, so they don't become assumptions:

- **No internal identifier strings.** The wiki shows only display names (e.g. "Steel Vultures"). Internal keys must come from the mod / game data, not here.
- **Frontier system & subsector names are procedurally generated**; no canonical enumeration exists. The journal should record procedural names at generation time if it wants subsector-specific broker dialogue.
- **Conquest system names** within Ara Martis / Penumbra / Lucifer aren't listed (subsector names are fixed, system names aren't).
- **Mission Board subtype catalogue is thin.** The wiki documents only the 4 Special mission types + Skill/Story missions in detail; the wide variety of procedural board missions (escort, deliver, mine X, salvage Y, combat clear) is alluded to but not enumerated.
- **Crew mechanics are thin.** Per-ship crew numbers exist (1–5) and two named companions have skill notes, but no "role / slot / permanent bonus" system is documented.
- **Background-specific variant quests** mentioned ("a Navy Officer would … a Prospector would …") but the branches aren't enumerated.
- **No complete "Items" list.** Consumables and Boosters lists are partial; general items (modules, ammo, junk types) lack canonical tables.
- **Exalted reputation, size-5+ ship unlocks beyond those listed, Kharon Forgeworks lore, Meridia internals** — explicitly "not known yet."
- **Game Music page exists** but is a placeholder.

---

## Sources (pages fetched)

All under `https://vanguard-galaxy.fandom.com/wiki/`:

[Vanguard Galaxy Wiki](https://vanguard-galaxy.fandom.com/wiki/Vanguard_Galaxy_Wiki), [Navigation](https://vanguard-galaxy.fandom.com/wiki/Navigation), [Missions](https://vanguard-galaxy.fandom.com/wiki/Missions), [Skill Missions](https://vanguard-galaxy.fandom.com/wiki/Skill_Missions), [Story Missions](https://vanguard-galaxy.fandom.com/wiki/Story_Missions), [Umbral Missions](https://vanguard-galaxy.fandom.com/wiki/Umbral_Missions), [Bounty Hunt](https://vanguard-galaxy.fandom.com/wiki/Bounty_Hunt), [Patrol](https://vanguard-galaxy.fandom.com/wiki/Patrol), [Industrial Ops](https://vanguard-galaxy.fandom.com/wiki/Industrial_Ops), [Combat](https://vanguard-galaxy.fandom.com/wiki/Combat), [Mining](https://vanguard-galaxy.fandom.com/wiki/Mining), [Salvage](https://vanguard-galaxy.fandom.com/wiki/Salvage), [Trade](https://vanguard-galaxy.fandom.com/wiki/Trade), [Refinery](https://vanguard-galaxy.fandom.com/wiki/Refinery), [Ships](https://vanguard-galaxy.fandom.com/wiki/Ships), [Ship List](https://vanguard-galaxy.fandom.com/wiki/Ship_List), [Items](https://vanguard-galaxy.fandom.com/wiki/Items), [Captain](https://vanguard-galaxy.fandom.com/wiki/Captain), [Skills](https://vanguard-galaxy.fandom.com/wiki/Skills), [Mastery](https://vanguard-galaxy.fandom.com/wiki/Mastery), [XP](https://vanguard-galaxy.fandom.com/wiki/XP), [Credits](https://vanguard-galaxy.fandom.com/wiki/Credits), [Faction Currencies](https://vanguard-galaxy.fandom.com/wiki/Faction_Currencies), [Faction Reputation](https://vanguard-galaxy.fandom.com/wiki/Faction_Reputation), [Conquest Reputation](https://vanguard-galaxy.fandom.com/wiki/Conquest_Reputation), [Conquest Zone](https://vanguard-galaxy.fandom.com/wiki/Conquest_Zone), [Stations](https://vanguard-galaxy.fandom.com/wiki/Stations), [Ship Manufacturers](https://vanguard-galaxy.fandom.com/wiki/Ship_Manufacturers), [Damage](https://vanguard-galaxy.fandom.com/wiki/Damage), [Aspects](https://vanguard-galaxy.fandom.com/wiki/Aspects), [Drones](https://vanguard-galaxy.fandom.com/wiki/Drones), [Boosters](https://vanguard-galaxy.fandom.com/wiki/Boosters), [Consumables](https://vanguard-galaxy.fandom.com/wiki/Consumables), [Energy](https://vanguard-galaxy.fandom.com/wiki/Energy), [Power](https://vanguard-galaxy.fandom.com/wiki/Power), [Precision](https://vanguard-galaxy.fandom.com/wiki/Precision), [Factions](https://vanguard-galaxy.fandom.com/wiki/Factions), [Stranded](https://vanguard-galaxy.fandom.com/wiki/Stranded), [Umbral Reach](https://vanguard-galaxy.fandom.com/wiki/Umbral_Reach), [Darkspace Compact](https://vanguard-galaxy.fandom.com/wiki/Darkspace_Compact), [Corsair Syndicate](https://vanguard-galaxy.fandom.com/wiki/Corsair_Syndicate), [Luminate Combine](https://vanguard-galaxy.fandom.com/wiki/Luminate_Combine), [Meridia's Chosen](https://vanguard-galaxy.fandom.com/wiki/Meridia%27s_Chosen), [Kolyatov Collective](https://vanguard-galaxy.fandom.com/wiki/Kolyatov_Collective), [Stellar Industries](https://vanguard-galaxy.fandom.com/wiki/Stellar_Industries), [Steel Vultures](https://vanguard-galaxy.fandom.com/wiki/Steel_Vultures), [Void Drifters](https://vanguard-galaxy.fandom.com/wiki/Void_Drifters), [Frontier](https://vanguard-galaxy.fandom.com/wiki/Frontier), [Mindus Holdings](https://vanguard-galaxy.fandom.com/wiki/Mindus_Holdings), [Akai Armory](https://vanguard-galaxy.fandom.com/wiki/Akai_Armory), [Canisec](https://vanguard-galaxy.fandom.com/wiki/Canisec), [Custos Arms](https://vanguard-galaxy.fandom.com/wiki/Custos_Arms), [Forge Industries](https://vanguard-galaxy.fandom.com/wiki/Forge_Industries), [Intertrade Network](https://vanguard-galaxy.fandom.com/wiki/Intertrade_Network), [Kharon Forgeworks](https://vanguard-galaxy.fandom.com/wiki/Kharon_Forgeworks), [Mindus Manufacturing](https://vanguard-galaxy.fandom.com/wiki/Mindus_Manufacturing), [Omnitac Agency](https://vanguard-galaxy.fandom.com/wiki/Omnitac_Agency), [Orsanon Security](https://vanguard-galaxy.fandom.com/wiki/Orsanon_Security), [RedStar Forge](https://vanguard-galaxy.fandom.com/wiki/RedStar_Forge), [Utixon Corp](https://vanguard-galaxy.fandom.com/wiki/Utixon_Corp), [Spirit Design](https://vanguard-galaxy.fandom.com/wiki/Spirit_Design), [Workshop](https://vanguard-galaxy.fandom.com/wiki/Workshop), [Locked Containers](https://vanguard-galaxy.fandom.com/wiki/Locked_Containers), [Forge](https://vanguard-galaxy.fandom.com/wiki/Forge), [Great Gate](https://vanguard-galaxy.fandom.com/wiki/Great_Gate), [Story Thus Far](https://vanguard-galaxy.fandom.com/wiki/Story_Thus_Far), [Midas](https://vanguard-galaxy.fandom.com/wiki/Midas), [Keril](https://vanguard-galaxy.fandom.com/wiki/Keril), [Creed](https://vanguard-galaxy.fandom.com/wiki/Creed), [ECHO](https://vanguard-galaxy.fandom.com/wiki/ECHO), [Arle](https://vanguard-galaxy.fandom.com/wiki/Arle), [Oron](https://vanguard-galaxy.fandom.com/wiki/Oron), [Obrix](https://vanguard-galaxy.fandom.com/wiki/Obrix), [Virgil](https://vanguard-galaxy.fandom.com/wiki/Virgil), [Claude](https://vanguard-galaxy.fandom.com/wiki/Claude), [Horace the Red](https://vanguard-galaxy.fandom.com/wiki/Horace_the_Red), [John Raythor](https://vanguard-galaxy.fandom.com/wiki/John_Raythor), [Mysterious Employer](https://vanguard-galaxy.fandom.com/wiki/Mysterious_Employer), [Marade Wharf](https://vanguard-galaxy.fandom.com/wiki/Marade_Wharf), and individual character pages for all named NPCs under `Category:Characters`.
