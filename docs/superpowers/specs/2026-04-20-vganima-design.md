# VGAnima — Design Spec

Status: approved for implementation planning
Date: 2026-04-20
Companion handoff: `/home/fank/repo/vanguard-galaxy/docs/llm-npc-plugin-handoff.md`

## Purpose

A BepInEx plugin for Vanguard Galaxy that lets bar patrons pitch real game missions with generated narrative flavor. Sibling to VGTTS: VGTTS gives NPCs voice, VGAnima gives them a reason to speak.

v0.1 ships the full plumbing with static templated text. v0.2 swaps the pitch provider to an LLM without touching the rest of the system.

## Identity

- Plugin GUID: `vganima`
- Display name: Vanguard Galaxy Anima
- Namespace / assembly: `VGAnima` / `VGAnima.dll`
- Target framework: `netstandard2.1`
- Dependencies: BepInEx 5.4.23.2, HarmonyX 2.10.*, UnityEngine.Modules 6000.2.6, Newtonsoft.Json 13.0.3
- Publicized stub: symlinked from `../vanguard-galaxy/VGTTS/lib/Assembly-CSharp.dll`
- VGTTS coupling: **soft** — detected via reflection at runtime, no compile-time reference

## Scope

### v0.1 (walking skeleton — end-to-end with static text)

- Convert a configurable fraction of vanilla `Salesman` bar patrons into mission-pitchers.
- Generate real game missions (Courier only) via `MissionGenerator.Get("Courier").GenerateMission(level, station)`.
- Inject the generated mission into the station's `MissionBoard.availableMissions` list.
- Replace the salesman's `dialogueLines` with 3–4 templated pitch lines ending in "I posted the request on the board."
- Suppress the vanilla post-dialogue `ShowSalesmanInfo` UI for converted patrons.
- Route dialogue through VGTTS (soft-dep reflection) so pitch lines are voiced when VGTTS is installed.
- Evict injected missions and cached audio when bar patrons roll off on daily refresh.

### v0.2 (LLM swap)

- `LlmPitchProvider` replaces `StaticPitchProvider` behind the same `IPitchProvider` interface.
- `OpenAICompatibleProvider` handles the HTTP shape — works with OpenAI, Ollama, LM Studio, Groq, Together, Azure OpenAI, llama.cpp server.
- `TravelManager.JumpToSystem` postfix pre-warms pitches during warp.
- Persistent on-disk cache at `BepInEx/cache/VGAnima/` keyed by `(station_guid, patron_seat, save_seed)`.
- Fallback to `StaticPitchProvider` on LLM/network failure.

### Out of scope (both versions)

- New patron types (only convert existing `Salesman`s).
- Dialogue choice/branch UI (game has no primitive; player accepts at the mission board instead).
- Multiple mission types beyond Courier in v0.1.
- Claim-item-abuse accept flow (rejected as too hacky).
- Bundled local LLM weights (user brings own endpoint).
- Translation / localization.

## Architecture

### Module layout

```
VGAnima/
├── Plugin.cs                        # BepInPlugin entry, Awake composition root
├── Config/
│   └── AnimaConfig.cs               # typed ConfigEntry accessors
├── Missions/
│   ├── MissionContext.cs            # station, level, faction, patron
│   ├── IMissionSource.cs            # Mission? Generate(MissionContext)
│   └── VanillaMissionSource.cs      # wraps MissionGenerator.GenerateMission
├── Pitch/
│   ├── PatronContext.cs             # npcName, isMale, station, mission
│   ├── PitchResult.cs               # IReadOnlyList<string> lines
│   ├── IPitchProvider.cs
│   └── StaticPitchProvider.cs       # templated strings for v0.1
├── Llm/                             # scaffolded, unused in v0.1
│   ├── ILlmProvider.cs
│   └── OpenAICompatibleProvider.cs  # v0.2
├── Tts/
│   └── VgttsBridge.cs               # reflection soft-dep → TtsController.Instance
├── Patches/
│   ├── BarPatronPatches.cs          # Initialize postfix [Priority(First)]
│   ├── SalesmanPatches.cs           # InteractWithPatron prefix (skip ShowSalesmanInfo)
│   └── BarRefreshPatches.cs         # Bar.CheckUpdatePatrons prefix+postfix
├── Cache/
│   └── ConversionRegistry.cs        # ConditionalWeakTable<BarPatron, ConversionRecord>
├── VGAnima.csproj
├── lib/                             # gitignored; symlinks created by `make link-asm`
└── README.md                        # user-facing install notes
```

### Key interfaces

```csharp
internal interface IMissionSource {
    Mission? Generate(MissionContext ctx);         // null = skip this patron
}

internal interface IPitchProvider {
    PitchResult Pitch(PatronContext ctx);          // sync in v0.1
}

internal interface ILlmProvider {                   // scaffolded for v0.2
    Task<string> CompleteAsync(string systemPrompt, string userPrompt, CancellationToken ct);
}

internal sealed record ConversionRecord(
    Mission Mission,
    IReadOnlyList<(string Speaker, string Text)> WarmedLines,
    SpaceStation Station);
```

### Composition root (Plugin.Awake)

```csharp
var missionTypes = Cfg.MissionTypes.Value.Split(',')
    .Select(s => s.Trim()).Where(s => s.Length > 0).ToArray();
_missionSource  = new VanillaMissionSource(missionTypes);
_pitchProvider  = new StaticPitchProvider();       // v0.2 swaps to LlmPitchProvider
_vgtts          = new VgttsBridge();               // no-ops if VGTTS absent
_registry       = new ConversionRegistry();
```

Patches access dependencies via `Plugin.Instance` singleton.

## Runtime flow

### Happy path

1. `Bar.CheckUpdatePatrons` runs, vanilla creates N `BarPatron`s including `Salesman`s.
2. For each `Salesman`, our `BarPatron.Initialize` postfix runs at `[HarmonyPriority(Priority.First)]`. Idempotent — bails if patron is already in `ConversionRegistry`.
3. `MissionChance` probability roll per salesman. Fail → vanilla flow untouched.
4. Build `MissionContext` from `salesman.spaceStation`.
5. `VanillaMissionSource.Generate(ctx)` returns a real `Mission` (null → skip conversion).
6. `station.missionBoard.availableMissions.Add(mission)` — unconditional append, no cap check, no replacement.
7. `StaticPitchProvider.Pitch(patronCtx)` returns 3–4 templated lines.
8. Replace `salesman.dialogueLines` via `Traverse` with the pitch lines.
9. Record `ConversionRecord(mission, warmedLines, station)` in registry.
10. VGTTS's `BarPatron.Initialize` postfix runs after ours, reads our replaced `dialogueLines`, warms the new text.
11. Player clicks patron → `Salesman.InteractWithPatron` runs.
12. Our `Salesman.InteractWithPatron` **prefix** sees the patron is in the registry; constructs its own `Dialogue` with a no-op `onComplete` (suppressing `ShowSalesmanInfo`), calls `DialogueManager.Instance.StartDialogue(...)`, returns `false` to skip the original method.
13. Dialogue plays (VGTTS speaks). Player walks to mission board, sees the injected mission, accepts via vanilla `MissionBoard.AcceptMission`.
14. Mission completes via the vanilla mission system. `Mission.ClaimRewards()` credits the player.

### Eviction

- `Bar.CheckUpdatePatrons` prefix: snapshot `availablePatrons`.
- `Bar.CheckUpdatePatrons` postfix: diff snapshot vs new list. For each patron that rolled off:
  - Look up its `ConversionRecord` in registry.
  - If the injected mission is still in `missionBoard.availableMissions`, `Remove()` it.
  - For each `WarmedLines` entry, `VgttsBridge.DropCache(speaker, text)`.
  - Remove registry entry.

### Save/load

- Registry is in-memory only; empty after load.
- On the first `BarPatron.Initialize` after load, each patron is re-converted. `MissionGenerator` is seeded from station state so the regenerated mission matches the one the player saw pre-save.
- Injected mission may not be in `availableMissions` post-load (the list deserializes without our injection). Re-injection happens in step 6 above.

### Mission board refresh

- `MissionBoard.RegenerateMissions` fires on its timer, calls `availableMissions.Clear()` then refills. Our injected mission is wiped.
- Next time the player opens the bar UI, each salesman's `Initialize` fires again. Idempotency check (step 2) sees the existing `ConversionRecord` and **re-adds the same `Mission` instance** if it's no longer on the board.

## Configuration

BepInEx `Config.Bind` entries:

| Section | Key | Type | Default | Purpose |
|---|---|---|---|---|
| `General` | `Enabled` | bool | `true` | master toggle |
| `General` | `MissionChance` | float | `1.0` | per-salesman conversion probability; 1.0 for v0.1 E2E testing, tune later |
| `General` | `MissionTypes` | string | `"Courier"` | comma-separated generator identifiers |
| `LLM` | `Backend` | string | `"static"` | `static` (v0.1) or `openai` (v0.2) |
| `LLM` | `Endpoint` | string | `"https://api.openai.com/v1"` | OpenAI-compatible base URL |
| `LLM` | `ApiKey` | string | `""` | never logged |
| `LLM` | `Model` | string | `"gpt-4o-mini"` | model identifier |

## VGTTS integration

### Bridge contract (soft dependency)

`Tts/VgttsBridge.cs` looks up `VGTTS.Audio.TtsController` by type name via `AccessTools.TypeByName`. If absent, all methods no-op. If present, invokes via `MethodInfo` (HarmonyX's `AccessTools` bypasses access modifiers on VGTTS's `internal` members).

Methods used:

```csharp
bool IsAvailable { get; }
void RegisterVoice(string speaker, string voice);
Task WarmCacheAsync(string speaker, string text, CancellationToken ct);
void DropCache(string speaker, string text);
```

`TtsController.Instance` is already `public static` on the VGTTS side, so retrieval works without reflection access-check bypass; only the internal instance methods need `AccessTools`.

### Voice routing

Procedural voice defaults match VGTTS's `BarPatronPatches`:
- Male: `kokoro:12` (am_echo)
- Female: `kokoro:9` (af_sarah)

VGAnima calls `RegisterVoice(salesman.name, voice)` before `WarmCacheAsync`. This matches the voice VGTTS's own `BarPatronPatches` would have picked, so mod ordering doesn't change the sound.

### Text normalization

Pitch text passed to VGTTS raw. VGTTS's `TextNormalizer.ForTts` handles normalization internally. We do **not** double-normalize.

## Harmony ordering

- `BarPatron.Initialize` postfix: `[HarmonyPriority(Priority.First)]` — runs before VGTTS's Normal-priority postfix.
- `Salesman.InteractWithPatron` prefix: default priority; VGTTS doesn't patch this method.
- `Bar.CheckUpdatePatrons` prefix: `HarmonyBefore("vgtts")` — snapshot before VGTTS.
- `Bar.CheckUpdatePatrons` postfix: `HarmonyAfter("vgtts")` — evict after VGTTS evicts.

## Injection policy

- Mission injection is **unconditional `Add()`**. No cap check. If the board is briefly over its natural cap, the next `RegenerateMissions` timer tick resolves it.
- Bar patron slots: untouched in v0.1 (conversion happens in-place on existing salesmen).

## Build & deploy

Makefile mirrors `/home/fank/repo/vanguard-galaxy/Makefile`:

- `make link-asm` — symlinks `lib/Assembly-CSharp.dll` to the VGTTS copy
- `make build` — `dotnet build VGAnima/VGAnima.csproj -c Debug`
- `make deploy` — copies `VGAnima.dll` to `<game>/BepInEx/plugins/VGAnima/`
- `make package` — produces `dist/VGAnima-v<version>/` for distribution
- `make clean` — removes `bin/`, `obj/`, `dist/`

Game install path resolved via WSL mount: `/mnt/c/Program Files (x86)/Steam/steamapps/common/Vanguard Galaxy`.

## Risks & mitigations

| Risk | Mitigation |
|---|---|
| `Salesman.InteractWithPatron` prefix-replace may break unknown side effects | Prefix only triggers when patron is in `ConversionRegistry`; vanilla salesmen untouched |
| `MissionGenerator.Get("Courier")` signature unverified | First implementation-task spike: `inspect.dll MissionGenerator Get` + `GenerateMission` to confirm exact signatures |
| VGTTS not installed | Bridge no-ops; dialogue shows silently, no crash |
| VGTTS installed but `TtsController.Instance == null` (Kokoro bundle missing) | Bridge null-checks `_controller`, no-ops |
| Save load drops registry | Re-conversion on next `Initialize`; seeded `MissionGenerator` reproduces the same mission |
| Third-party mod also patches `BarPatron.Initialize` | `[HarmonyPriority(First)]` + documented conflict list |
| `DialogueLine.character` shape unverified | Implementation uses `character = null` per VGTTS `BarPatronPatches` pattern; verify in spike |
| API key exposure | Never logged; stored in user's BepInEx config only |

## Testing strategy

Tiered since the game can't run in CI:

1. **Compile check.** `dotnet build` produces `VGAnima.dll` against the publicized stub. CI-runnable.
2. **Unit tests** (xUnit, no game runtime):
   - `StaticPitchProvider` template substitution across gender / mission-type combinations
   - `VgttsBridge` reflection fallback (both "VGTTS type absent" and "VGTTS present, Instance null" cases)
   - v0.2: `OpenAICompatibleProvider` HTTP contract (mock server)
   - v0.2: `PromptBuilder` output shape stability (snapshot test on prompt string)
3. **Manual E2E smoke test** (documented in README, run by user):
   - Build + deploy
   - Launch game, dock, enter bar
   - Observe salesman with templated pitch, close dialogue
   - Open mission board, observe injected Courier mission
   - Accept, fly, complete, verify reward credited
   - Check `BepInEx/LogOutput.log` for `[vganima]` trace at each stage
4. **Regression check** (VGTTS co-installed): confirm voice lines up with text, no priority race, bar refresh evicts both caches.

## Logging conventions

Match VGTTS's log style, prefixed `[vganima]`:

- **Info** — lifecycle (Awake loaded, VGTTS detected y/n, patron converted, mission injected)
- **Debug** — per-patron detail (MissionChance roll outcome, eviction diff)
- **Warning** — injection skipped due to null station/mission, VGTTS bridge method missing
- **Error** — unexpected exceptions; caught at patch boundary so Harmony never explodes

## Deliverable

### v0.1 acceptance criteria

Player with VGAnima + VGTTS installed:

1. Docks at a station, enters the bar.
2. Finds a salesman saying (voiced by VGTTS): *"Captain — got a run needs doing. Haul some supplies from Alpha Station to Ceres Outpost, pays 3,200 credits. I posted the request on the board if you're in."*
3. Closes dialogue, walks to mission board.
4. Sees `Courier: Haul Supplies to Ceres Outpost` listed.
5. Accepts via vanilla mission board UI.
6. Completes mission. Gets paid. Nothing breaks.

### v0.2 acceptance criteria

Same flow, but pitch text is LLM-generated from the same concrete mission parameters, pre-warmed during warp, with `StaticPitchProvider` fallback on network failure.
