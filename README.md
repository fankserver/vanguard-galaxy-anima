# VGAnima — Bar brokers that pitch LLM-authored jobs

A BepInEx plugin for **Vanguard Galaxy** that turns bar patrons into mission brokers. Each broker gets a rich player + world snapshot handed to an OpenAI-compatible LLM, which authors the broker's dialogue on the fly (pitch, check-in, payout). Missions themselves run on the game's vanilla `StoryMission` + `Mission` subsystem — the LLM handles words, the engine handles mechanics.

VGTTS voices the dialogue if installed.

## Install

1. Install [VGTTS](https://www.nexusmods.com/) (optional — without it the dialogue runs silent).
2. Drop `VGAnima.dll` into `<game>/BepInEx/plugins/VGAnima/`. That's the only file the plugin ships (the game supplies `BepInEx`, `HarmonyX`, `Newtonsoft.Json` — no runtime deps to side-load).
3. Edit `BepInEx/config/vganima.cfg` (auto-generated on first launch — see below) and set an LLM endpoint.
4. Launch the game. A `vganima` boot line shows up in `BepInEx/LogOutput.log`.

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

1. **Bar injection.** When the player docks, `BarRefreshPatches` looks for a converted patron slot. If the LLM is enabled and `MissionChance` rolls through, an async pipeline fires:
   - `ContextGatherer` snapshots the player (level, credits, fleet, cargo, faction reputations, active missions, waypoints, story arcs) and the world (current station + facilities, connected systems, time).
   - `HttpLlmClient` POSTs the snapshot to `<BaseUrl>/chat/completions` with a strict system prompt asking for `{ schema, pitch, check_in, payout }` lines.
   - `ResponseValidator` enforces the `vganima/story/v1` schema — wrong schema value, extra keys, non-ASCII text, bad line counts all reject and the broker is silently skipped.
   - On success, a Salesman is minted with a seed-prefixed name (`vganima-broker-<station-guid>-N`) and the validated dialogue cached inside its `ConversionRecord`.
2. **Talk to the broker.** `SalesmanPatches` picks a `BrokerState` based on the mission's position in `GamePlayer.missionsArchive` / `GetActiveStoryMission`:
   - `Initial` → play the `pitch` block, on dialogue close call `AddMissionWithLog` to activate the vanilla mission (the static `Jobsite Survey` test template for now).
   - `InProgress` → play `check_in`.
   - `ReadyToClaim` → play `payout`, fire `CompleteMission` (rewards land), then the broker departs in the same click.
   - `Done` → one last farewell and the broker departs.
3. **Rehydration.** On every bar re-open we spot seed-prefixed brokers that have no registry entry (save/load, bar refresh), fire the LLM again on each, and rebuild their record asynchronously.

Cache lifetime is strictly per-broker; when the broker is evicted the dialogue dies with it. No cross-broker reuse, no cross-session persistence.

## Failure matrix

Every LLM failure path is logged and **no broker is injected** — there's no static fallback.

| What went wrong | Log level | Marker |
|---|---|---|
| `Llm.Enabled=false` or `BaseUrl` blank | Debug | `LLM disabled; skipping broker injection` |
| HTTP timeout | Warning | `LLM timeout after Ns; skipping broker at '<station>'` |
| HTTP non-2xx | Warning | `LLM returned <status>; skipping broker at '<station>'` |
| Network exception | Warning | `LLM request failed: <msg>; skipping broker` |
| Malformed JSON | Warning | `LLM response failed validation: content is not valid json ...` |
| Schema mismatch | Info | `LLM response failed validation: <field> <rule>; skipping` |
| Unclassified | Error | Full stack trace |

## Troubleshooting

- **No `Vanguard Galaxy Anima` lines in log** — plugin didn't load. Check `VGAnima.dll` is in `BepInEx/plugins/VGAnima/` and BepInEx itself logs in `BepInEx/LogOutput.log`.
- **Boot log shows `LLM enabled: no`** — set `Llm.Enabled=true` AND `Llm.BaseUrl=...` in `vganima.cfg`. Both must be filled.
- **Broker never appears** — check the boot log confirmed `LLM enabled: yes`, then watch for the LLM dispatch line: `Dispatching LLM for broker at '<station>'`. If that line is missing the probability roll failed (`MissionChance` < 1.0) or a vanilla NPC is hogging the seat budget.
- **`FileNotFoundException: System.Text.Json`** — you're running an older build that shipped STJ. Re-deploy the current `VGAnima.dll` — current builds use `Newtonsoft.Json` (bundled by the game) and no longer need STJ.
- **Broker name changes after save/reload** — known limitation. Only the Salesman's seed is persisted by vanilla `BarPatron.ToJson`; our in-memory `_name` override is lost on load and the seeded-random regenerates ("The Mission Broker" → "Shawn Jenkins" etc.). The storyId assignment survives.

## Roadmap

The LLM output schema is versioned (`vganima/story/v1`). Future slices extend the schema one concern at a time:

- **v2** — `theme` tag; LLM picks the mission template from an expanded whitelist.
- **v3** — `objective` object; LLM chooses a `MissionTrigger` + required amount.
- **v4** — `rewards`; LLM picks clamped amounts from whitelisted reward types.
- **v5** — `turn_in` POI; LLM can route turn-in to other stations.
- **v6+** — multi-mission chains, cross-session persistence, prompt caching.

See `docs/superpowers/notes/2026-04-20-vganima-llm-authored-missions-future.md` for the broader Option C design sketch.
