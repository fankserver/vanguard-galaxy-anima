# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## What this is

`VGAnima` is a BepInEx 5.x plugin for the game *Vanguard Galaxy*. It injects LLM-authored broker NPCs into station bars. The plugin builds a rich context snapshot of game state, calls an OpenAI-compatible chat endpoint, validates the response against a strict schema, and converts the validated JSON into a live vanilla `Mission` registered through the game's `StoryMission` system. See `README.md` for user-facing behavior.

## Build, test, deploy

The `Makefile` is authoritative. The raw `dotnet` commands work too, but `make build` also refreshes symlinks to external DLLs (see "Dependencies on sibling checkouts" below).

```bash
make build              # symlinks libs + builds VGAnima.dll
make test               # full xUnit suite
make deploy             # copies built DLLs to BepInEx/plugins/VGAnima/ (WSL path)
make clean

# Running a single test:
dotnet test VGAnima.Tests/VGAnima.Tests.csproj --filter "FullyQualifiedName~MissionRecordArchetypeTests"
dotnet test VGAnima.Tests/VGAnima.Tests.csproj --filter "DisplayName~Build_MultipleMissionsInSystem"
```

### Host-test runtime reference

Run `make refresh-test-asm` once against the inspected installed game, then `make test`. Tests use a separate ignored, publicized but unstripped runtime reference; production retains its stripped compile reference. The former five PlaceholderMission/BrokerStateDetector failures came from throw-only stripped constructors/getters, not failed assertions. They now pass without assertion or production-code changes. Baseline: 450 passing / 17 skipped / 0 failing. This does not enable Unity-native calls or replace in-game qualification.

### Tests target net8.0 but may run on net10

`VGAnima.Tests/runtimeconfig.template.json` sets `"rollForward": "LatestMajor"` so the test host runs under whatever .NET is installed. If you see `You must install or update .NET to run this application`, the runtimeconfig didn't make it into the test bin folder — `make clean && make test`.

## Dependencies on sibling checkouts

Three DLLs are **symlinked** into `VGAnima/lib/` by the Makefile, not committed:

- `Assembly-CSharp.dll` — owner-local stripped/publicized reference generated with `make refresh-asm`; `make link-asm` checks source/reference hashes. See `docs/current-game-compatibility.md` for the exact inspected build. Never commit the generated DLL or private receipts.
- `VGModAPI.Abstractions.dll` — compile-only reference; API 0.1.8–0.1.x is a hard runtime dependency with mission events enabled. `make link-api`; override `VGAPI_DLL` for isolated worktrees. Never deploy the API assembly inside Anima's folder.
- `VGMissionJournal.dll` — typed soft-dep on the sibling mod. Resolved from `../vanguard-galaxy-missionjournal/VGMissionJournal/bin/Release/...`. The game loads it as its own plugin at runtime; we only need compile-time types. `make link-missionjournal`.

If `VGMissionJournal.dll` is missing or stale (API drift), the soft-dep lookup in `VgMissionJournalBridge` falls back to an empty-query stub at runtime, but *compilation* will fail. Rebuild the sibling first.

## Architecture — the broker injection pipeline

The end-to-end flow when the player walks into a bar:

1. **`BarRefreshPatches`** (Harmony postfix on the bar-roster-rebuild method) — triggers a refresh and decides whether to dispatch an LLM broker.
2. **`ContextGatherer`** — composes `LlmContext` (see `Llm/LlmContext.cs`) from `GameStateView`, `MissionGuidanceBuilder`, `JournalContextBuilder`, `RegionallyKnownBuilder`, `BarEcosystemBuilder`, `AccessibleDestinationsBuilder`. Serialized with Newtonsoft.Json (explicit `[JsonProperty("snake_case")]` attributes control wire shape).
3. **`HttpLlmClient`** — POSTs the context + a strict system prompt to an OpenAI-compatible endpoint.
4. **`ResponseValidator` + `MissionBlockValidator`** — JSON schema check, whitelist enforcement, reward clamps, ASCII-only dialogue. Any failure drops the broker entirely; no fallback.
5. **`MissionFactoryFromJson`** — intent-dispatched: each validated `LlmIntent` (7 in the whitelist) maps to concrete vanilla objective + POI + ship-composition shapes. The LLM never names a vanilla class.
6. **`LlmMissionAssigner`** — registers the `Mission` into vanilla's `StoryMission.allMissions`, pushes a `PersistedEntry` into `PersistedBrokerRegistry`, adds the broker to the bar roster.

### Mission lifecycle

`Persistence/MissionEventObserver` consumes witnessed API events, not native return values. It tracks session-local occurrence GUIDs per owned provider definition; acceptance updates the registry, restoration only associates existing definitions, and the last witnessed terminal occurrence removes a definition. This is not a persistent event-history match. Direct lifecycle patches are removed; native save/load/factory/lookup hooks remain. See `docs/mission-events.md` for fault handling and legacy sidecar limitations.

### Journal / history split (important)

There are **two** sources of mission history the LLM sees:

- **In-flight (offered + accepted)** missions live in VGAnima's own `PersistedBrokerRegistry`, persisted to a sidecar `<save>.save.vganima.json` (schema **v4**). `SaveWritePatch` + `SaveLoadPatch` flush/rehydrate around vanilla save ops.
- **Resolved (completed/failed/abandoned)** missions live in the sibling mod **VGMissionJournal** and are queried through `VgMissionJournalBridge` (typed soft-dep). VGAnima used to keep its own completed-mission log; that was retired in MJ-T4 because VGMissionJournal records *all* terminations (vanilla + VGAnima) in a richer form.

`JournalContextBuilder` composes the four prompt windows (`local` / `network` / `rumors` / `active`) by combining both sources; the reach model in `MagnitudeReachFormula` decides which resolved records the current broker plausibly knows about (distance-attenuated with age + magnitude + fame).

### Two separate "archetype" concepts — don't conflate them

- **Pitched side** (`MissionArchetypes` constants, `ArchetypeInferrer` is gone; `IntentWhitelist.Archetypes`, `MissionBlockValidator`, `MissionGuidanceBuilder`) — single-label archetype per intent. Legitimate because we control the intent whitelist. Six values: `combat / mining / salvage / trade / deliver / escort`. `MissionGuidanceBuilder` ranks these server-side before the LLM sees the prompt; the validator forbids intents whose archetype set touches a forbidden archetype.
- **Historical side** (`MissionRecordArchetype.ObjectiveTags`) — **multi-label** list of canonical objective tags drawn from vanilla's own vocabulary (`kill_enemies` / `protect_unit` / `mine_ore` / `collect_salvage` / `haul_goods` / `collect_items` / `travel`). No priority ladder, no single-label collapse. A defended-salvage mission surfaces as `[collect_salvage, kill_enemies]`.

If you're tempted to add a priority to the historical side, re-read `MissionRecordArchetype`'s doc comment and the commit that replaced the ladder (`cee518b`). The ladder was the problem, not a solution.

### The Mining-class quirk

Vanilla's `Mining` objective class is a misnomer — it's actually a polymorphic "gather N of category X" class, with `itemCategory` as the designed discriminator (Ore / Salvage / TradeGoods / RefinedProduct). Vanilla's own `Salvage : Mining` is a cosmetic subclass (overrides display text only). `MissionFactoryFromJson.BuildGather` emits the real `Salvage` subclass for `gather_salvage` so journal records come through as `Type="Salvage"` without needing the `itemCategory` peek; the peek still runs for legacy records and for `haul_goods` (TradeGoods has no subclass).

## Conventions worth knowing

- **Nullable reference types** are enabled project-wide (`<Nullable>enable</Nullable>`). Annotate accordingly.
- **Newtonsoft.Json, not System.Text.Json.** Mono's VTable faulted on `Utf8JsonWriter` on Unity 6000.2 — see the comment in `VGAnima.csproj`. Don't re-introduce STJ without reading it.
- **BepInEx / HarmonyX / UnityEngine / Newtonsoft are `IncludeAssets="compile"`** in the plugin project — they must not ship in the deployed DLL (the game provides them). The test project pulls runtime copies because xUnit needs them live.
- **`InternalsVisibleTo VGAnima.Tests`** — tests touch `internal` types directly. Prefer `internal` over `public` for anything that doesn't need to be part of a public API.
- **Hardcoded German Steam path in Makefile's `deploy` target** — `GAME_DIR` assumes WSL + Windows Steam default location. Adjust locally if your layout differs (don't commit).
- **Don't commit `VGAnima/lib/*.dll`** — they're symlinks to sibling checkouts.

## Docs map

- `README.md` — user-facing install / usage / architecture overview. Keep in sync with behavior changes.
- `docs/vanilla-reference.md` — vanilla game mechanics reference (reward formulas, faction model, POI lifecycle, objective types, multipliers). The authoritative source for "what does vanilla actually do."
- `docs/current-game-compatibility.md` — current inspected compilation target and personnel bindings.
- `docs/vanguard-galaxy-decomp-survey.md` — historical identifiers harvested from an older `Assembly-CSharp.dll`; verify against the current target before reuse.
- `docs/vanguard-galaxy-wiki-survey.md` — display-name + lore counterpart.
- `docs/vanguard-galaxy-bar-ecosystem-survey.md` — bar patron / salesman taxonomy.
- `docs/npc-interaction-ideas.md`, `docs/special-quest-ideas.md` — design sketches, forward-looking (not authoritative for shipped behavior).

## Decompiling vanilla

`ilspycmd` (global tool) is available for spot-checking vanilla classes:

```bash
DOTNET_ROLL_FORWARD=LatestMajor ilspycmd VGAnima/lib/Assembly-CSharp.dll -t Source.MissionSystem.Objectives.Mining
DOTNET_ROLL_FORWARD=LatestMajor ilspycmd VGAnima/lib/Assembly-CSharp.dll -l c | grep Salvage
```

Use it before guessing at vanilla behavior — the decomp is the ground truth.
