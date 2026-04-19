# VGAnima — Bar patrons that pitch real missions

A BepInEx plugin for **Vanguard Galaxy** that converts vanilla bar salesmen into mission-pitchers. They speak a short pitch (voiced by [VGTTS](https://github.com/…) if installed) and post a real game mission to the station's mission board for you to accept.

v0.1 ships with static templated pitch text. v0.2 swaps in LLM-generated dialogue.

## Install

1. Install [VGTTS](https://nexusmods.com/…) (optional but recommended — without it the pitch is silent).
2. Drop `VGAnima.dll` into `<game>/BepInEx/plugins/VGAnima/`.
3. Launch the game. A `vganima` entry appears in `BepInEx/LogOutput.log` on boot.

## Build from source

Prerequisites:

- Sibling checkout at `../vanguard-galaxy/` (VGTTS — we reuse its publicized `Assembly-CSharp.dll`).
- `dotnet` SDK on PATH, or `/tmp/dnsdk/dotnet/dotnet`.

```bash
make build            # compiles to VGAnima/bin/Debug/netstandard2.1/VGAnima.dll
make test             # runs unit tests
make deploy           # copies DLL into BepInEx/plugins/VGAnima/
make clean            # removes bin/ obj/ dist/
```

## Config (`BepInEx/config/vganima.cfg`)

| Section | Key | Default | Purpose |
|---|---|---|---|
| `General` | `Enabled` | `true` | master toggle |
| `General` | `MissionChance` | `1.0` | per-salesman conversion probability |
| `General` | `MissionTypes` | `Courier` | comma-separated generator IDs |
| `LLM` | `Backend` | `static` | v0.1 only supports `static` |
| `LLM` | `Endpoint` | `https://api.openai.com/v1` | (v0.2) |
| `LLM` | `ApiKey` | _(empty)_ | (v0.2) never logged |
| `LLM` | `Model` | `gpt-4o-mini` | (v0.2) |

## Manual E2E smoke test (v0.1 acceptance)

Run this every time you change patch behavior. Takes ~5 minutes in-game.

1. **Deploy:** `make deploy`
2. **Launch the game.** Tail the log: `tail -f '<game>/BepInEx/LogOutput.log' | grep vganima`
3. **Expect at boot:**
   - `[vganima] VGTTS detected: yes` (if VGTTS is installed)
   - `[vganima] MissionTypes: [Courier]  Chance: 1  Backend: static`
   - `Vanguard Galaxy Anima v0.1.0 loaded (3 patches)`
4. **Load a save, dock at any space station with a bar, enter the bar.**
5. **Expect in the log** (one per salesman):
   - `[vganima] Injected mission '<name>' onto board at '<station>'`
   - `[vganima] Converted salesman '<name>' — 3 pitch lines, voice=kokoro:12` (or `:9`)
6. **Click a converted salesman.** You should see our 3 pitch lines (the third mentions the board). VGTTS voices them if installed. Dialogue closes with no "buy this item" UI.
7. **Open the mission board.** Confirm the injected mission is listed.
8. **Accept the mission.** Fly, complete it, collect reward. Normal vanilla flow.
9. **Wait for the bar to refresh** (next in-game day or trigger via console), re-enter bar:
   - Rolled-off patrons evicted: `[vganima] Evicted rolled-off patron '<name>'`
   - New salesmen convert again (if `MissionChance` roll succeeds).

## Troubleshooting

- **No `[vganima]` lines in log** — plugin didn't load. Check that `VGAnima.dll` is in `BepInEx/plugins/VGAnima/` and that BepInEx itself is installed (look for `BepInEx/LogOutput.log`).
- **`VGTTS detected: no`** — VGTTS isn't installed or failed to load. Dialogue still works silently.
- **Salesman pitch text appears but mission isn't on the board** — `MissionGenerator.Get("Courier")` returned null. Check the `MissionTypes` config value matches a real generator ID.
- **Game throws on dialogue open** — our `SalesmanPatches` prefix hit an unexpected shape. Disable it via `Enabled = false` in config and report the stack trace from `BepInEx/LogOutput.log`.

## Architecture

See `docs/superpowers/specs/2026-04-20-vganima-design.md`.
