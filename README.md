# VGAnima — Bar brokers that pitch LLM-authored missions

A BepInEx plugin for **Vanguard Galaxy** that turns bar patrons into mission brokers. An OpenAI-compatible LLM authors the full mission on the fly — dialogue, objectives, rewards, mission steps, even on-map combat POIs — all wrapped around the game's vanilla `StoryMission` + `Mission` subsystem. VGAnima handles authoring; the engine handles mechanics.

VGTTS voices the dialogue if installed.

## Install

1. Install **VGModAPI 0.1.25–0.1.x** separately (currently a development build), and enable `[Missions] Enabled = true` in `BepInEx/config/vgmodapi.cfg`. Mission events remain experimental: use disposable saves until qualified. Optionally also enable `[Travel] Enabled = true` — without it Anima records no system visits and omits `regionally_known` from prompts (see [travel events](docs/travel-events.md)). VGTTS is optional; without it dialogue runs silent.
2. Drop `VGAnima.dll` into `<game>/BepInEx/plugins/VGAnima/`. Do not copy game DLLs or the API's assemblies into this folder; the API owns its installation.
3. Edit `BepInEx/config/vganima.cfg` (auto-generated on first launch — see below) and set an LLM endpoint.
4. Launch the game. A `Vanguard Galaxy Anima` boot line shows up in `BepInEx/LogOutput.log`.

## Build from source

Prerequisites:

- Inspected game installation and `assembly-publicizer`: run `make refresh-asm` and `make refresh-test-asm` once to generate private compile/test references. See [current compatibility](docs/current-game-compatibility.md).
- Release builds of sibling VGModAPI (0.1.25) and VGMissionJournal. Override `VGAPI_DLL` / `VGMISSIONJOURNAL_DLL` for isolated worktrees.
- `dotnet` SDK on PATH, or a pre-staged install at `/tmp/dnsdk/dotnet/dotnet`.

```bash
make build             # compiles VGAnima/bin/Debug/netstandard2.1/VGAnima.dll
make test              # runs the full xUnit suite
make deploy            # copies the DLL into <game>/BepInEx/plugins/VGAnima/
make clean             # removes bin/ obj/ dist/
```

## Mission and travel events, and save data

System visits use witnessed API travel arrivals. Visits are counted from the actual arrival location of a jumpgate or wormhole leg, not from a requested destination: placements, requests, departures, cancellations, route completion and in-system arrivals never count. The API's travel group is opt-in and off by default; while it is unavailable or stopped, nothing is recorded, existing history is preserved, and `regionally_known` is omitted rather than pitched from stale counts. Mission authoring and the load safeguards do not depend on it. See [travel events](docs/travel-events.md).

Version 0.3.0 replaced five direct mission lifecycle hooks with witnessed API events. It updates only Anima-owned provider definitions; restored missions do not invent new acceptance. Repeated live instances retain their definition until the last observed terminal outcome. Native missions are neither read nor mutated in these callbacks.

Missing/disabled/incompatible API prevents startup. Later capability loss stops authoring, observation and save writes until restart, while retaining load/lookup safeguards for existing content; late LLM results cannot publish into another session. See [the event contract and limits](docs/mission-events.md).

This is **not** an Anima save-data migration. Versions 3 and 4 of `.save.vganima.json` are readable; writes use version 5 with managed-bar cleanup metadata. Factory/lookup hooks and best-effort save/quit behavior remain. They are not exact-snapshot API-managed storage and carry no cross-file atomicity or failed-save rollback guarantee. Back up the vanilla save and paired sidecar together. No prompt schema or mission economics changed.

## Config (`BepInEx/config/vganima.cfg`)

| Section | Key | Default | Purpose |
|---|---|---|---|
| `General` | `Enabled` | `true` | Master toggle. When false VGAnima does nothing. |
| `General` | `MissionChance` | `1.0` | Per-patron conversion probability (`0.0..1.0`). |
| `Llm` | `Enabled` | `false` | Master switch for LLM-authored broker dialogue. When false, no broker is injected anywhere. |
| `Llm` | `BaseUrl` | _(empty)_ | OpenAI-compatible endpoint base, e.g. `https://host/v1`. Blank disables LLM dispatch. |
| `Llm` | `Model` | `qwen` | Model identifier passed in the chat completions request body. |
| `Llm` | `TimeoutSeconds` | `60` | Per-call timeout. On expiry the call is cancelled and no broker is injected. Dispatch is fire-and-forget on a background task, so a larger value just raises the success rate — bump if your backend is slow or thinking tokens are enabled. |
| `Llm` | `ApiKey` | _(empty)_ | Optional Bearer token. Never logged in cleartext (only as `<set>`/`<empty>`). |
| `Llm` | `EnableThinking` | `false` | Passed as `chat_template_kwargs.enable_thinking` for vLLM Qwen. Harmless on other backends. |
| `Llm` | `MaxTokens` | `1200` | Token ceiling on the completion. |
| `Llm` | `Temperature` | `0.8` | Sampling temperature. Higher = more varied, lower = more deterministic. |

## How it works

**1. Context snapshot.** On bar refresh, `ContextGatherer` hands the LLM a JSON payload describing the player and world state a bar-broker NPC could plausibly know:

- `player` — level, credits, specialization (one of nine: Leadership / Mining / Drones / Engineering / Industrial / Salvaging / Economy / Offense / Defense), unlocked titles, bounty/patrol/industry ladder ranks, active mission count + cap.
- `fleet` — primary ship name/level, **hardpoint loadout flags** (`has_combat_loadout` / `has_mining_loadout` / `has_salvage_loadout` read via the game's `SpaceShipData.HasLoadout` check), stored ships (up to 10), crew (up to 10). Hull/shield %, cargo-used %, and credit balance are NOT exposed — a broker NPC can't see into a private hold or wallet, and hull/shield auto-repair to 100 on dock would make those fields misleading anyway.
- `location` — current station + faction + facilities, system, sector, quadrant, connected systems (up to 8, 2-jump radius).
- `factions` — all 18 vanilla factions keyed by identifier, each carrying display name (e.g. `Marauders` → *Corsair Syndicate*), reputation value, and relation band (`friendly` / `neutral` / `hostile`, matching vanilla `FactionData.IsEnemy` — hostile iff `at_war` or `rep < -500`).
- `reward_clamps` — numeric bounds for credit / XP / reputation amounts.
- `mission_guidance` — **pre-computed ranked archetype weights + forbidden list + rationale**. See below.
- `missions` — ladder levels (bounty/patrol/industry). Story IDs feed server-side guidance via `MissionGuidanceBuilder` but are JsonIgnored out of the prompt (machine hashes, not dialogue material).
- `journal` — per-broker view of resolved + in-flight missions in four windows:
  - `local` (events at this station), `network` (same-faction events within reach), `rumors` (distant hearsay — different faction or far away), `active` (in-flight offered/accepted VGAnima missions).
  - Resolved entries are sourced from the sibling mod **VGMissionJournal** via a soft typed dependency (`VgMissionJournalBridge`) — it logs *all* mission terminations, vanilla + VGAnima. In-flight entries stay on VGAnima's own `PersistedBrokerRegistry` (offered-but-not-yet-accepted jobs don't reach VGMissionJournal until accept).
  - Reach is distance-attenuated: gossip always travels to neighbors (1-2 jumps), further systems gate on magnitude, age penalty raises the bar over time, fame bonus (max of bounty/patrol/industry rank) pushes famous-player events farther. Each entry carries `jumps_from_here` so the broker can voice the narrative distance. See `MagnitudeReachFormula`.
  - Each entry carries an `objectives` array (multi-label tags: `kill_enemies` / `protect_unit` / `mine_ore` / `collect_salvage` / `haul_goods` / `collect_items` / `travel`) drawn from the mission's actual vanilla objective types — a defended-salvage run surfaces as `[collect_salvage, kill_enemies]` so the LLM sees both shapes rather than a lossy single-label collapse.
- `bar_ecosystem` — other salesmen currently at the bar (Prospector, Salvage Scout, Equipment Rep, etc.) so the broker can reference the rest of the room organically.
- `purchase_profile` — lifetime tallies of bar-salesman purchases (mining/salvage claims, ship PNGs, equipment) and station commodity-shop buys (mining/salvage/general/other). Signals player taste without narrating exact inventory.
- `accessible_destinations` — stations the broker can target for `deliver_to_station` / `haul_goods` intents (0-1 jumpgate hops from this system, capped at 8, ranked by distance + same-faction-as-broker).
- `regionally_known` — systems where the player has ≥ 3 recorded visits; omitted entirely while system-visit recording is unavailable or stopped. The flag is sampled at gather time, so a stop during an in-flight LLM call does not retract an already-built prompt. Each entry includes `visits`, `last_visit_days_ago`, and an optional `recent_activity` — a list of objective tags from the most-recent mission in that system (same vocabulary as the journal's `objectives`), filled only if resolved < 30 game-days ago. Enables "you've been salvaging around here, yeah?" framing; empty/absent falls back to face-only recognition.
- `location.station_condition` — single-word atmosphere tag (`war-torn` / `peaceful` / `bustling` / `frontier` / `normal`) that nudges the broker's linguistic register.
- `waypoints`, `time`, `broker` (name, gender, seed, station alignment).

Cargo contents, cargo %, credit balance, and hull/shield % are **not** included. A broker can't see into a private hold or wallet; mounted hardpoints ARE externally visible on a docked ship, so loadout flags stay.

**2. Archetype pre-scoring.** `MissionGuidanceBuilder` weights six mission archetypes by signal aggregation *before* the LLM sees the context. The LLM reads our conclusion, not scattered raw signals. Mining and salvage stay distinct (different skill trees); trade is its own archetype for commodity hauling.

| Archetype | Maps to (intents) | Signals |
|---|---|---|
| `combat` | `clear_combat_site`, defended gather variants | hardpoints, spec Offense/Defense/Drones, combat titles, active bounty/patrol missions, damaged ship, hostile-neighbor system |
| `mining` | `gather_ore`, `defended_gather_ore` | hardpoints, spec Mining/Industrial/Engineering, miner title, industry ladder, active mining missions, Refinery+Forge facilities |
| `salvage` | `gather_salvage`, `defended_gather_salvage` | hardpoints, spec Salvaging, active salvage missions, SalvageWorkshop facility |
| `trade` | `haul_goods` | spec Economy/Industrial/Engineering, merchant/trader title, industry ladder, active industrial/trade story IDs, Refinery+Forge facilities |
| `deliver` | `deliver_to_station` | spec Economy/Engineering/Industrial, merchant title, full cargo, Shipyard, varied connected systems |
| `escort` | *(not yet a VGAnima intent — reserved)* | spec Defense/Leadership, hostile-neighbor system |

Archetypes are forbidden (weight zeroed) when impossible: combat is forbidden when no faction is hostile; a forbidden archetype also blocks every intent whose archetype set touches it (e.g. forbidden combat blocks defended gather variants too).

**3. LLM call.** `HttpLlmClient` POSTs to `<BaseUrl>/chat/completions` with a strict system prompt asking for a `vganima/mission/v2` JSON object: 3-5 pitch lines, 1-2 check-in lines, 2-4 payout lines, and a `mission` block (name, description, completion text, source faction, 1-3 steps each with an intent, 1-5 rewards). Intents are the v2 narrative vocabulary — `clear_combat_site`, `gather_ore`, `gather_salvage`, `defended_gather_ore`, `defended_gather_salvage`, `deliver_to_station`, `haul_goods`. The plugin maps each intent to the mechanical shape (POI spawn, objective type, ship composition) so the LLM authors narrative, not mechanics.

**4. Validation.** `ResponseValidator` + `MissionBlockValidator` enforce:

- Strict JSON schema — unknown keys, wrong types, missing fields all reject.
- ASCII-only dialogue, line-length + count bounds.
- **Faction identifiers in mission block fields, display names in dialogue** (e.g. `enemy_faction: "Marauders"` but dialogue says *"the Corsair Syndicate"*).
- `source_faction` must be relation=friendly; `enemy_faction` must be relation=hostile.
- Reward clamps: Credits base 15..100, XP base 30..100, Reputation -500..500 (positive values must be ≥150).
- At most one combat objective per step (ClearPoi + KillEnemies in the same step is rejected).

Any validation failure → no broker is injected, full system/user/raw-response triple dumped at Info level for debugging.

**5. Mission factory.** `MissionFactoryFromJson` translates the validated block into a live vanilla `Mission`:

- `sourcePoi` = broker's station; `turnIn` = same station.
- `dynamicLevel = false`, `mission.level = station.level` — area-anchored reward scaling (mirrors vanilla `MissionGenerator`, not `SideMissions`). Activates vanilla's XP over-level penalty for over-leveled players.
- Credit / XP amounts computed via `GameMath.GetCreditsValue` + `GetExperienceRewardValue` with the station level.
- `ClearPoi` objectives spawn a `Combat` POI in the station's system via `SystemMapData.AddCombat`, seed it with `CreateUnitPayload + AddGuards`, pin to the step's `dynamicPointOfInterest`, return `KillEnemies { requiredAmount = combat.totalUnitCount }` — mirrors vanilla `BountyHunt`.

**6. Registration + rehydration.** `LlmMissionAssigner` registers the finished Mission into `StoryMission.allMissions` with a globally-unique storyId (`vganima_llm_<station-guid>_<broker-seed>_<nonce>`), and pushes a `PersistedEntry` into the in-memory `PersistedBrokerRegistry` (state=offered). The broker is then added to the bar roster.

**7. Cross-session persistence.** Each vanilla save gets a pair-named sidecar `<save>.save.vganima.json` (schema **v5**) containing:

- **In-flight missions** — full LLM-authored mission blocks, broker dialogue trees, and broker→station bindings for all `offered` + `accepted` missions.
- **Visited-systems map** — native system identifier → (display label, visit count, first/last visit game-seconds). Written by `SystemVisitObserver` from witnessed API travel arrivals (jumpgate + wormhole). Feeds `regionally_known`.

Resolved-mission history lives in the sibling mod **VGMissionJournal** (its own sidecar). VGAnima used to keep a rolling 50-entry completed-mission log in-sidecar; that was retired in v4 because VGMissionJournal records *all* mission terminations (vanilla + VGAnima) in a richer, queryable form. See `MJ-T4` notes in the commit history.

A Harmony postfix on `SaveGame.Store` flushes the in-memory registry to the sidecar after vanilla's own save succeeds; a prefix on `SaveGameFile.LoadSaveGame` reads the sidecar and registers rebuild factories *before* vanilla's mission-list deserialization runs. The `ApplicationQuit` safety net flushes pending state when the player closes the game mid-session. Orphan purge runs during the first post-load bar refresh, dropping entries whose storyIds left vanilla's mission lists or whose brokers left the bar.

Schema upgrades are transparent: v3 sidecars (before the local mission-log retirement) auto-upgrade to v4 on read — the `completed_missions` field is dropped; `entries` + `visited_systems` carry over intact; written back as v4 on the next save. v2 sidecars (before the visited-systems addition) upgrade through the same path. v1 sidecars (pre-intent-refactor, with deleted objective types) quarantine on load.

Security: sidecars go through `VGAnimaSidecarSerializationBinder`, an allowlist over the concrete `LlmIntent` / `LlmReward` subtypes. Untrusted `$type` discriminators throw on deserialization. A missing or corrupted sidecar quarantines to `<save>.save.vganima.corrupt.<timestamp>.json` and the game continues with an empty registry — the `MissionLookupPatch` placeholder prevents `KeyNotFoundException` for any orphan storyIds vanilla's save still references.

**8. Broker interaction.** `SalesmanPatches` dispatches on the mission's state:
- `Initial` → pitch dialogue, then `GamePlayer.AddMissionWithLog`.
- `InProgress` → check-in dialogue.
- `ReadyToClaim` → payout dialogue, `CompleteMission` (vanilla reward pipeline fires), broker departs.
- `Done` → farewell, broker departs.

## Failure matrix

Every failure path is logged and **no broker is injected** — there's no static fallback.

| What went wrong | Log level | Marker |
|---|---|---|
| `Llm.Enabled=false` or `BaseUrl` blank | Debug | `LLM disabled; skipping broker injection` |
| HTTP timeout | Warning | `LLM timeout after Nms (limit=Ms); skipping broker at '<station>'` |
| HTTP non-2xx | Warning | `LLM returned <status>; skipping broker at '<station>'` |
| Network exception | Warning | `LLM request failed after Nms: <msg>; skipping broker at '<station>'` |
| Malformed JSON | Info | `LLM response failed validation: content is not valid json ...` + full prompt/response dump |
| Schema / clamp mismatch | Info | `LLM response failed validation: field \`<path>\` <rule>; skipping` + dump |
| Forbidden archetype (e.g. combat with no hostile factions) | Info | `LLM response failed validation: ... refusing kill mission` + dump |
| Unclassified | Error | Full stack trace |

Successful parses also emit the full prompt/response dump at Debug level so you can see exactly what the model decided and why.

## Troubleshooting

- **No `Vanguard Galaxy Anima` lines in log** — plugin didn't load. Check `VGAnima.dll` is in `BepInEx/plugins/VGAnima/` and BepInEx itself logs in `BepInEx/LogOutput.log`.
- **Boot log shows `LLM enabled: no`** — set `Llm.Enabled=true` AND `Llm.BaseUrl=...` in `vganima.cfg`. Both must be filled.
- **Broker never appears** — check the boot log confirmed `LLM enabled: yes`, then watch for the LLM dispatch line: `Dispatching LLM for broker at '<station>'`. If that line is missing the probability roll failed (`MissionChance` < 1.0) or a vanilla NPC is hogging the seat budget (6 cap per bar). The dispatch log lists every gate that fired at Debug level.
- **Broker spawns but mission has weird rewards** — check the Debug log for `Reward[Credits]: base_value=X missionLevel=Y → amount=Z` lines. Rewards are area-level-anchored; an over-leveled player at a low-level station will see XP near 1 (vanilla anti-farm at work, not a bug). See `docs/vanilla-reference.md` for the formulas.
- **Every broker pitches combat (or mining, or salvage, or...)** — check the `mission_guidance` block in the user prompt dump. The ranked weights show why a specific archetype was picked. Weights are derived from player signals; adjust your fleet loadout / specialization / titles if the skew is unexpected.
- **Reload a save and the mission is gone** — check `BepInEx/LogOutput.log` for the load-time banner `Loaded N broker entries from <sidecar>`. If it says `No sidecar at <path>` or `Sidecar corrupted; quarantined to ...`, the paired file was missing/unreadable; affected missions auto-archive via `PlaceholderMission`. The sidecar should live alongside the vanilla save at `{persistentDataPath}/Saves/<saveName>.save.vganima.json`.
- **Broker in bar but no mission dialogue after reload** — the sidecar entry may have been orphan-purged (storyId not in vanilla's active/archived list AND broker seed not in any current bar). Check the load log for `Orphan-purged N stale entries`. This typically happens after loading an older save slot that doesn't match the sidecar.
- **`regionally_known` never shows up in the prompt dump** — the API's travel group is opt-in. Without `[Travel] Enabled = true` in `vgmodapi.cfg` the boot log warns `native travel events unavailable`, no visits are recorded, and the section is omitted on purpose. The same happens after `system-visit recording stopped` (observer failure or capability loss); recorded history is kept and returns on restart.
- **Sidecar not updating on save** — confirm the log emits `Flushed N broker entries to <sidecar>` after each save. No log line = the Harmony postfix didn't run (check plugin load order).

## Docs

- [`docs/mission-events.md`](docs/mission-events.md) — witnessed mission-provider events, ownership and failure policy.
- [`docs/travel-events.md`](docs/travel-events.md) — witnessed travel arrivals, what counts as a system visit, and the opt-in/degrade policy.
- [`docs/vanilla-reference.md`](docs/vanilla-reference.md) — mechanics knowhow (reward formulas, faction model, POI lifecycle, procedural generator multipliers, clamp rationale). Standalone reference, no decompile paths required.
- [`docs/vanguard-galaxy-decomp-survey.md`](docs/vanguard-galaxy-decomp-survey.md) — catalog of canonical identifiers drawn from the decompiled `Assembly-CSharp.dll` (factions, reputation thresholds, mission type IDs).
- [`docs/vanguard-galaxy-wiki-survey.md`](docs/vanguard-galaxy-wiki-survey.md) — display-name + lore counterpart to the decomp survey.
- [`docs/vanguard-galaxy-bar-ecosystem-survey.md`](docs/vanguard-galaxy-bar-ecosystem-survey.md) — bar patron + salesman types.

## Roadmap

Shipped milestones:
- **v2-mission** — full-mission authoring with `vganima/mission/v1` schema, ClearPoi combat POIs, ranked archetype scoring, hardpoint-based capability signals, faction identifier/display-name split.
- **Persistence** — pair-named sidecar per vanilla save; offered + accepted brokers survive rotation and restart; locked-down `TypeNameHandling` allowlist; corrupt/missing sidecars fail soft via placeholder mission.
- **Intent refactor (`vganima/mission/v2`)** — LLM authors narrative via a closed intent vocabulary (7 intents), plugin owns all mechanical shapes (POI spawns, ship compositions, objective types). Combat encounters support 6 flavors (scouting / outpost / lair / raid / cornered_remnants / swarm) with distinct narrative shapes. Purchase-profile signal tracks bar-salesman + commodity-shop purchases so brokers can tune rewards to player taste.
- **Bar ecosystem awareness** — broker sees the other salesmen at the same bar and can reference them organically; LLM-injected brokers are filtered out of their own view.
- **Accessible destinations** — `deliver_to_station` / `haul_goods` intents pick from a plugin-provided list of reachable stations, validated against the current jumpgate graph.
- **Journal continuity** — brokers read a four-window view (`local` / `network` / `rumors` / `active`) of the resolved-mission log with distance-attenuated reach: gossip always travels to neighbors, far systems only hear big events, player fame pushes events farther. Plus `regionally_known` face-recognition for regulars in a system.

Next up:
- **Fleet capability profile** — surface stored-ship loadouts too, not just primary, so brokers understand "this player could swap into a mining rig" when weighting archetypes.
- **Named-target hunt intent** — one-shot bounty-style combat missions with a named target + escape timer, as an alternative to the area-clear combat flavors.
- **Prompt caching / cost controls** — shared system-prompt cache across dispatches.
- **Multi-broker arcs** — cross-broker callbacks and multi-mission storylines.
