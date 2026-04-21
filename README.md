# VGAnima — Bar brokers that pitch LLM-authored missions

A BepInEx plugin for **Vanguard Galaxy** that turns bar patrons into mission brokers. An OpenAI-compatible LLM authors the full mission on the fly — dialogue, objectives, rewards, mission steps, even on-map combat POIs — all wrapped around the game's vanilla `StoryMission` + `Mission` subsystem. VGAnima handles authoring; the engine handles mechanics.

VGTTS voices the dialogue if installed.

## Install

1. Install [VGTTS](https://www.nexusmods.com/) (optional — without it the dialogue runs silent).
2. Drop `VGAnima.dll` into `<game>/BepInEx/plugins/VGAnima/`. That's the only file the plugin ships (the game supplies `BepInEx`, `HarmonyX`, `Newtonsoft.Json` — no runtime deps to side-load).
3. Edit `BepInEx/config/vganima.cfg` (auto-generated on first launch — see below) and set an LLM endpoint.
4. Launch the game. A `Vanguard Galaxy Anima` boot line shows up in `BepInEx/LogOutput.log`.

## Build from source

Prerequisites:

- Sibling checkout of the VGTTS repo at `../vanguard-galaxy-tts/` (we symlink its publicized `Assembly-CSharp.dll`).
- `dotnet` SDK on PATH, or a pre-staged install at `/tmp/dnsdk/dotnet/dotnet`.

```bash
make build             # compiles VGAnima/bin/Debug/netstandard2.1/VGAnima.dll
make test              # runs the full xUnit suite
make deploy            # copies the DLL into <game>/BepInEx/plugins/VGAnima/
make clean             # removes bin/ obj/ dist/
```

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
- `fleet` — primary ship name/level/hull/shield, **hardpoint loadout flags** (`has_combat_loadout` / `has_mining_loadout` / `has_salvage_loadout` read via the game's `SpaceShipData.HasLoadout` check), cargo-used percentage, stored ships (up to 10), crew (up to 10).
- `location` — current station + faction + facilities, system, sector, quadrant, connected systems (up to 8, 2-jump radius).
- `factions` — all 18 vanilla factions keyed by identifier, each carrying display name (e.g. `Marauders` → *Corsair Syndicate*), reputation value, and relation band (`friendly` / `neutral` / `hostile`, matching vanilla `FactionData.IsEnemy` — hostile iff `at_war` or `rep < -500`).
- `reward_clamps` — numeric bounds for credit / XP / reputation amounts.
- `mission_guidance` — **pre-computed ranked archetype weights + forbidden list + rationale**. See below.
- `missions` — active story IDs, recent archive tail, ladder levels.
- `story_arcs_active`, `waypoints`, `time`, `broker` (name, gender, seed, station alignment).

Cargo contents are **not** included — a broker NPC can't see into the player's hold. Ship exterior state (hull damage, mass/loading) is surfaced.

**2. Archetype pre-scoring.** `MissionGuidanceBuilder` weights five mission archetypes by signal aggregation *before* the LLM sees the context. The LLM reads our conclusion, not scattered raw signals.

| Archetype | Maps to | Signals |
|---|---|---|
| `combat` | `ClearPoi` (preferred) or `KillEnemies` | hardpoints, spec Offense/Defense/Drones, combat titles, active bounty/patrol missions, damaged ship, hostile-neighbor system |
| `gather` | `CollectItemTypes` Ore / RefinedProduct | hardpoints, spec Mining/Industrial/Engineering, miner title, industry ladder, active mining missions, Refinery+Forge facilities |
| `salvage` | `CollectItemTypes` Salvage / Junk | hardpoints, spec Salvaging, active salvage missions, SalvageWorkshop facility |
| `deliver` | `TriggerObjective` travel (± `CollectItemTypes` TradeGoods) | spec Economy/Engineering/Industrial, merchant title, full cargo, Shipyard, Economy story arc, varied connected systems |
| `escort` | `ProtectUnit` + travel `TriggerObjective` | spec Defense/Leadership, hostile-neighbor system |

Archetypes are forbidden (weight zeroed) when impossible: combat + escort are forbidden when no faction is hostile.

**3. LLM call.** `HttpLlmClient` POSTs to `<BaseUrl>/chat/completions` with a strict system prompt asking for a `vganima/mission/v1` JSON object: 3-5 pitch lines, 1-2 check-in lines, 2-4 payout lines, and a `mission` block (name, description, completion text, source faction, 1-3 steps with 1-2 objectives each, 1-5 rewards).

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

**6. Registration + rehydration.** `LlmMissionAssigner` registers the finished Mission into `StoryMission.allMissions` with a globally-unique storyId (`vganima_llm_<station-guid>_<broker-seed>_<nonce>`). The broker is then added to the bar roster.

On save/reload within the same session, seed-prefixed brokers without registry entries get their dialogue re-fetched from the LLM. **Cross-session persistence is not implemented** — the StoryMission factory is in-memory only; a mission accepted in one play session and reloaded across a game restart throws `KeyNotFoundException`. This is a known limitation; see the roadmap.

**7. Broker interaction.** `SalesmanPatches` dispatches on the mission's state:
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
- **Every broker pitches combat (or gather, or...)** — check the `mission_guidance` block in the user prompt dump. The ranked weights show why a specific archetype was picked. Weights are derived from player signals; adjust your fleet loadout / specialization / titles if the skew is unexpected.
- **Reload a save and the mission disappears** — known limitation. LLM-authored missions live in the `StoryMission` registry for the session's remainder only; cross-session persistence is on the roadmap.
- **Broker name changes after save/reload** — only the Salesman's seed is persisted by vanilla `BarPatron.ToJson`; our in-memory `_name` override is lost on load and the seeded-random regenerates (*"The Mission Broker"* → *"Shawn Jenkins"* etc.). The storyId assignment survives within a session.

## Docs

- [`docs/vanilla-reference.md`](docs/vanilla-reference.md) — mechanics knowhow (reward formulas, faction model, POI lifecycle, procedural generator multipliers, clamp rationale). Standalone reference, no decompile paths required.
- [`docs/superpowers/specs/`](docs/superpowers/specs/) — versioned design specs.
- [`docs/superpowers/plans/`](docs/superpowers/plans/) — step-by-step implementation plans, one per milestone.

## Roadmap

Current milestone **v2-mission** shipped: full-mission authoring with `vganima/mission/v1` schema, ClearPoi combat POIs, ranked archetype scoring, hardpoint-based capability signals, faction identifier/display-name split.

Next up:

- **Persistence** — serialize LLM-authored missions into the vanilla save stream so they survive game restarts. (Spec §12; highest priority for v1.1.)
- **Activity history** — track player's observable interactions at each station (refinery use, workshop use, trade terminal, prior broker deals) as context for the LLM. The narrative-honest alternative to cargo peeking.
- **Fleet capability profile** — surface stored-ship loadouts too, not just primary, so brokers understand "this player could swap into a mining rig" when weighting archetypes.
- **Prompt caching / cost controls** — shared system-prompt cache across dispatches.
- **Multi-broker arcs** — cross-broker callbacks and multi-mission storylines.
