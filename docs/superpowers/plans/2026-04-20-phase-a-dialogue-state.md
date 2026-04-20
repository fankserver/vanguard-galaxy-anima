# Phase A — Mission-Aware Broker Dialogue

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** The broker's pitch lines change based on game-observable mission state (board listing / player accepted / completion ready / rewarded) so the NPC feels alive through the mission lifecycle. No new persistence — all state derived from the game.

**Architecture:** Add a `BrokerState` enum computed from `station.missionBoard.availableMissions`, `GamePlayer.current.missions`, and `Mission.CanClaimRewards()`. `IPitchProvider` gains a `PitchForState(ctx, state)` method; `StaticPitchProvider` returns different line sets per state. `SalesmanPatches.InteractWithPatron_Prefix` rebuilds dialogue lines fresh on every click using the current state, so the broker's speech tracks reality. `ConversionRecord` gains a mutable `Pitched` flag (the only way to distinguish "never talked to him yet" from "mission cycled through and got rewarded"). `BarRefreshPatches` pre-warms VGTTS for all 5 state variants up-front so voice is instant regardless of which state the player encounters.

**Tech Stack:** Existing — C# 11, netstandard2.1, BepInEx, HarmonyX, xUnit.

**Notes reference:** `docs/superpowers/notes/2026-04-20-mission-lifecycle-extensions.md` (Phase A section).

---

## File layout

| File | Change |
|---|---|
| `VGAnima/Pitch/BrokerState.cs` | **Create** — enum with 5 states |
| `VGAnima/Pitch/IPitchProvider.cs` | **Modify** — add `PitchForState(ctx, state)`; keep `Pitch(ctx)` as Initial shortcut |
| `VGAnima/Pitch/StaticPitchProvider.cs` | **Modify** — implement `PitchForState` for each state |
| `VGAnima/Cache/ConversionRecord.cs` | **Modify** — convert record to class with mutable `Pitched` flag |
| `VGAnima/Patches/BrokerStateDetector.cs` | **Create** — static helper computing current state from game state |
| `VGAnima/Patches/SalesmanPatches.cs` | **Modify** — use state-specific dialogue in prefix; set `Pitched=true` on post |
| `VGAnima/Patches/BarRefreshPatches.cs` | **Modify** — warm TTS for all 5 state variants |
| `VGAnima.Tests/Pitch/StaticPitchProviderTests.cs` | **Modify** — add per-state assertions |

---

## Task 1: Add `BrokerState` enum

**Files:**
- Create: `VGAnima/Pitch/BrokerState.cs`

- [ ] **Step 1: Write the enum**

```csharp
namespace VGAnima.Pitch;

/// <summary>Phase A: the broker's dialogue varies by the player's progress on
/// the pitched mission. All states are derived from game-observable data
/// (mission board roster, player missions, CanClaimRewards) plus one mutable
/// bit on <c>ConversionRecord.Pitched</c> to distinguish "never talked" from
/// "mission cycled through and got rewarded".</summary>
internal enum BrokerState
{
    /// <summary>Player has not yet finished the initial pitch dialogue.
    /// Broker recites the pitch + posts the mission on close.</summary>
    Initial,

    /// <summary>Mission is on the station's board; player has not accepted it.
    /// Broker nudges: "it's still up on the board".</summary>
    Waiting,

    /// <summary>Mission is in <see cref="Source.Player.GamePlayer.missions"/>
    /// but <see cref="Source.MissionSystem.Mission.CanClaimRewards"/> is false.
    /// Broker asks how it's going.</summary>
    InProgress,

    /// <summary>Player has accepted and completed the mission; rewards are
    /// ready to claim at the mission board.</summary>
    ReadyToClaim,

    /// <summary>Mission has left the board and the player's mission list —
    /// they claimed the reward. Broker wraps up politely.</summary>
    Done,
}
```

- [ ] **Step 2: Verify build**

Run: `make build`
Expected: clean, 0 warnings, 0 errors.

- [ ] **Step 3: Commit**

```bash
git add VGAnima/Pitch/BrokerState.cs
git -c user.name="Florian Kinder" -c user.email="florian.kinder@fankserver.com" commit -m "feat: add BrokerState enum for Phase A dialogue state"
```

---

## Task 2: Extend `IPitchProvider` with `PitchForState`

**Files:**
- Modify: `VGAnima/Pitch/IPitchProvider.cs`

- [ ] **Step 1: Replace the interface body**

Full file content:

```csharp
namespace VGAnima.Pitch;

internal interface IPitchProvider
{
    /// <summary>Initial pitch (shortcut for <c>PitchForState(ctx, Initial)</c>).
    /// Kept for the existing call site in BarRefreshPatches; new code should
    /// call <see cref="PitchForState"/> with an explicit state.</summary>
    PitchResult Pitch(PatronContext ctx);

    /// <summary>State-aware pitch — lines vary by the player's progress on
    /// the pitched mission.</summary>
    PitchResult PitchForState(PatronContext ctx, BrokerState state);
}
```

- [ ] **Step 2: Verify build (will fail — StaticPitchProvider doesn't implement PitchForState yet)**

Run: `make build`
Expected: error CS0535 (`StaticPitchProvider` does not implement `PitchForState`). That's the gate for Task 3.

---

## Task 3: Implement `PitchForState` in `StaticPitchProvider` (TDD)

**Files:**
- Modify: `VGAnima/Pitch/StaticPitchProvider.cs`
- Modify: `VGAnima.Tests/Pitch/StaticPitchProviderTests.cs`

- [ ] **Step 1: Add failing tests for each state**

Replace `VGAnima.Tests/Pitch/StaticPitchProviderTests.cs` with:

```csharp
using VGAnima.Pitch;
using Xunit;

namespace VGAnima.Tests.Pitch;

public class StaticPitchProviderTests
{
    private static PatronContext Ctx(bool isMale = true) =>
        new("Test Broker", isMale, Station: null!, Mission: null!);

    [Fact]
    public void Pitch_EqualsInitialState()
    {
        var provider = new StaticPitchProvider();
        var direct = provider.Pitch(Ctx());
        var viaState = provider.PitchForState(Ctx(), BrokerState.Initial);
        Assert.Equal(direct.Lines, viaState.Lines);
    }

    [Fact]
    public void Initial_ContainsBoardReference()
    {
        var provider = new StaticPitchProvider();
        var result = provider.PitchForState(Ctx(), BrokerState.Initial);
        Assert.True(result.Lines.Count >= 3, $"Expected >=3 lines, got {result.Lines.Count}");
        Assert.Contains(result.Lines, l => l.ToLowerInvariant().Contains("board"));
    }

    [Fact]
    public void Waiting_ReferencesBoard()
    {
        var provider = new StaticPitchProvider();
        var result = provider.PitchForState(Ctx(), BrokerState.Waiting);
        Assert.NotEmpty(result.Lines);
        Assert.Contains(result.Lines, l => l.ToLowerInvariant().Contains("board"));
    }

    [Fact]
    public void InProgress_AsksAboutProgress()
    {
        var provider = new StaticPitchProvider();
        var result = provider.PitchForState(Ctx(), BrokerState.InProgress);
        Assert.NotEmpty(result.Lines);
    }

    [Fact]
    public void ReadyToClaim_PromptsReport()
    {
        var provider = new StaticPitchProvider();
        var result = provider.PitchForState(Ctx(), BrokerState.ReadyToClaim);
        Assert.NotEmpty(result.Lines);
        // Should point the player back at the mission board to claim.
        Assert.Contains(result.Lines, l => l.ToLowerInvariant().Contains("board"));
    }

    [Fact]
    public void Done_Thanks()
    {
        var provider = new StaticPitchProvider();
        var result = provider.PitchForState(Ctx(), BrokerState.Done);
        Assert.NotEmpty(result.Lines);
        Assert.Contains(result.Lines, l => l.ToLowerInvariant().Contains("thank"));
    }

    [Theory]
    [InlineData(BrokerState.Initial)]
    [InlineData(BrokerState.Waiting)]
    [InlineData(BrokerState.InProgress)]
    [InlineData(BrokerState.ReadyToClaim)]
    [InlineData(BrokerState.Done)]
    public void AllStates_UseAsciiOnlyPunctuation(BrokerState state)
    {
        // The game's pixel16 font lacks em-dashes (U+2014), smart quotes, and
        // most extended Latin — they render as spaces. Enforce ASCII across
        // the whole pitch library.
        var provider = new StaticPitchProvider();
        var result = provider.PitchForState(Ctx(), state);
        foreach (var line in result.Lines)
            foreach (var ch in line)
                Assert.True(ch < 128,
                    $"Non-ASCII char U+{(int)ch:X4} '{ch}' in {state} line: \"{line}\"");
    }
}
```

- [ ] **Step 2: Run tests to verify they fail (method undefined)**

Run: `make test`
Expected: compile error on `PitchForState` (not implemented in `StaticPitchProvider`).

- [ ] **Step 3: Replace StaticPitchProvider with the multi-state version**

Full file content for `VGAnima/Pitch/StaticPitchProvider.cs`:

```csharp
namespace VGAnima.Pitch;

/// <summary>v0.1 pitch provider — produces templated pitch lines without
/// touching the network. Text is intentionally ASCII-only: the game's pixel16
/// font drops em-dashes and smart quotes to spaces.</summary>
internal sealed class StaticPitchProvider : IPitchProvider
{
    public PitchResult Pitch(PatronContext ctx) => PitchForState(ctx, BrokerState.Initial);

    public PitchResult PitchForState(PatronContext ctx, BrokerState state) => state switch
    {
        BrokerState.Initial => new PitchResult(new[]
        {
            "Captain, I've got a run that needs a steady hand.",
            "Nothing fancy. Cargo haul to a neighbour system, decent pay, fair turnaround.",
            "I've posted the request on the station board. Grab it if you're in.",
        }),
        BrokerState.Waiting => new PitchResult(new[]
        {
            "It's still up on the board, Captain.",
            "Take a look when you get a moment.",
        }),
        BrokerState.InProgress => new PitchResult(new[]
        {
            "Still working on that run?",
            "Come back when it's done and we'll settle up.",
        }),
        BrokerState.ReadyToClaim => new PitchResult(new[]
        {
            "Ready to report in, Captain?",
            "Head over to the mission board and wrap it up.",
        }),
        BrokerState.Done => new PitchResult(new[]
        {
            "Thanks for the work, Captain.",
            "Safe travels out there.",
        }),
        _ => new PitchResult(new[] { "Captain." }),
    };
}
```

- [ ] **Step 4: Run tests to verify they pass**

Run: `make test`
Expected: all tests pass (original 13 + 7 new state tests + 5 theory rows = varies).

- [ ] **Step 5: Commit**

```bash
git add VGAnima/Pitch/IPitchProvider.cs VGAnima/Pitch/StaticPitchProvider.cs VGAnima.Tests/Pitch/StaticPitchProviderTests.cs
git -c user.name="Florian Kinder" -c user.email="florian.kinder@fankserver.com" commit -m "feat: state-aware pitch lines in StaticPitchProvider"
```

---

## Task 4: Convert `ConversionRecord` to a class with mutable `Pitched`

**Files:**
- Modify: `VGAnima/Cache/ConversionRecord.cs`

- [ ] **Step 1: Replace the file contents**

Full file content:

```csharp
using System.Collections.Generic;
using Source.Galaxy.POI;
using Source.MissionSystem;

namespace VGAnima.Cache;

/// <summary>Everything the broker lifecycle needs:
///   <list type="bullet">
///     <item>The injected <see cref="Mission"/> — removed from the board on patron rolloff.</item>
///     <item>Warmed TTS lines — dropped from VGTTS cache on rolloff.</item>
///     <item>The <see cref="SpaceStation"/> for mission-board access.</item>
///     <item>A <see cref="Pitched"/> flag, flipped on the first dialogue close.
///       Used to distinguish "never talked to broker" (Initial state) from
///       "mission cycled through, rewarded" (Done state) — both of which have
///       the mission absent from both board and player missions.</item>
///   </list>
/// Not a record (was one originally) because <c>Pitched</c> must be mutable.</summary>
internal sealed class ConversionRecord
{
    public Mission Mission { get; }
    public IReadOnlyList<(string Speaker, string Text)> WarmedLines { get; }
    public SpaceStation Station { get; }
    public bool Pitched { get; set; }

    public ConversionRecord(
        Mission mission,
        IReadOnlyList<(string Speaker, string Text)> warmedLines,
        SpaceStation station)
    {
        Mission = mission;
        WarmedLines = warmedLines;
        Station = station;
    }
}
```

- [ ] **Step 2: Verify build + tests still pass**

Run: `make build && make test`
Expected: clean build; all existing tests pass. The record-to-class change is ABI-compatible at the call sites (construction + property reads).

- [ ] **Step 3: Commit**

```bash
git add VGAnima/Cache/ConversionRecord.cs
git -c user.name="Florian Kinder" -c user.email="florian.kinder@fankserver.com" commit -m "refactor: ConversionRecord class with mutable Pitched flag"
```

---

## Task 5: Add `BrokerStateDetector`

**Files:**
- Create: `VGAnima/Patches/BrokerStateDetector.cs`

Compile-only verification — Mission/GamePlayer types require the game runtime.

- [ ] **Step 1: Write the detector**

```csharp
using Source.Player;
using VGAnima.Cache;
using VGAnima.Pitch;

namespace VGAnima.Patches;

/// <summary>Computes the current <see cref="BrokerState"/> from
/// game-observable state plus the persisted <see cref="ConversionRecord.Pitched"/>
/// bit. No side effects.</summary>
internal static class BrokerStateDetector
{
    public static BrokerState Detect(ConversionRecord record)
    {
        if (record == null) return BrokerState.Initial;
        var mission = record.Mission;
        var station = record.Station;

        // Player has the mission in their active list — either still working on
        // it or ready to claim rewards.
        var playerMissions = GamePlayer.current?.missions;
        if (playerMissions != null && playerMissions.Contains(mission))
        {
            return mission.CanClaimRewards() ? BrokerState.ReadyToClaim : BrokerState.InProgress;
        }

        // Mission sitting on this station's board, player hasn't accepted yet.
        var board = station?.missionBoard;
        if (board != null && board.availableMissions != null &&
            board.availableMissions.Contains(mission))
        {
            return BrokerState.Waiting;
        }

        // Mission neither on the board nor in the player's missions. Without
        // the Pitched flag we can't tell "first contact" from "mission cycled
        // through, rewarded" — both look the same to game state.
        return record.Pitched ? BrokerState.Done : BrokerState.Initial;
    }
}
```

- [ ] **Step 2: Verify build**

Run: `make build`
Expected: clean compile.

- [ ] **Step 3: Commit**

```bash
git add VGAnima/Patches/BrokerStateDetector.cs
git -c user.name="Florian Kinder" -c user.email="florian.kinder@fankserver.com" commit -m "feat: add BrokerStateDetector computing state from game data"
```

---

## Task 6: Rewire `SalesmanPatches` to state-aware dialogue

**Files:**
- Modify: `VGAnima/Patches/SalesmanPatches.cs`

- [ ] **Step 1: Replace the whole file**

Full file content:

```csharp
using System;
using System.Collections.Generic;
using Behaviour.Dialogues;
using Behaviour.Util;
using HarmonyLib;
using Source.Dialogues;
using Source.Galaxy.POI.Station.Patrons;
using VGAnima.Cache;
using VGAnima.Pitch;

namespace VGAnima.Patches;

/// <summary>
/// Intercepts <see cref="Salesman.InteractWithPatron"/> for brokers in the
/// registry. Rebuilds dialogue lines on every click from
/// <see cref="BrokerStateDetector"/> so the broker's speech tracks the
/// player's progress on the pitched mission. The onComplete handler is
/// state-specific — only the Initial state posts the mission to the board.
/// </summary>
[HarmonyPatch(typeof(Salesman))]
internal static class SalesmanPatches
{
    [HarmonyPrefix]
    [HarmonyPatch(nameof(Salesman.InteractWithPatron))]
    private static bool InteractWithPatron_Prefix(Salesman __instance)
    {
        try
        {
            if (Plugin.Instance is not { } plugin) return true;
            if (!plugin.Registry.TryGet(__instance, out var record)) return true;

            var state = BrokerStateDetector.Detect(record);

            var patronCtx = new PatronContext(
                __instance.name, __instance.isMale, record.Station, record.Mission);
            var pitch = plugin.PitchProvider.PitchForState(patronCtx, state);

            var lines = new List<DialogueLine>(pitch.Lines.Count);
            foreach (var text in pitch.Lines)
            {
                if (string.IsNullOrWhiteSpace(text)) continue;
                var character = new Character(__instance.name).WithPortret(__instance.icon);
                lines.Add(DialogueLine.cDL(character, text));
            }
            if (lines.Count == 0) return true;  // nothing to show; fall through to vanilla

            Action onComplete = state == BrokerState.Initial
                ? () => PostMission(record)
                : NoOp;

            Plugin.Log.LogDebug(
                $"[vganima] '{__instance.name}' dialogue state={state}, {lines.Count} line(s)");

            Singleton<DialogueManager>.Instance.StartDialogue(lines, onComplete);
            return false;  // skip vanilla
        }
        catch (Exception ex)
        {
            Plugin.Log.LogError($"[vganima] InteractWithPatron_Prefix threw: {ex}");
            return true;
        }
    }

    /// <summary>Posts the broker's mission to the station board on the first
    /// dialogue close. Idempotent — subsequent calls are no-ops. Also flips
    /// <see cref="ConversionRecord.Pitched"/> so the state machine can tell
    /// Initial apart from Done after the mission fully cycles.</summary>
    private static void PostMission(ConversionRecord record)
    {
        try
        {
            record.Pitched = true;
            var board = record.Station?.missionBoard;
            if (board == null) return;
            if (board.availableMissions.Contains(record.Mission)) return;

            board.availableMissions.Add(record.Mission);
            Plugin.Log.LogInfo(
                $"[vganima] Posted mission '{record.Mission.name}' onto board at '{record.Station!.name}'");
        }
        catch (Exception ex)
        {
            Plugin.Log.LogError($"[vganima] PostMission threw: {ex}");
        }
    }

    private static readonly Action NoOp = static () => { };
}
```

- [ ] **Step 2: Verify build + tests**

Run: `make build && make test`
Expected: clean; tests still pass.

- [ ] **Step 3: Commit**

```bash
git add VGAnima/Patches/SalesmanPatches.cs
git -c user.name="Florian Kinder" -c user.email="florian.kinder@fankserver.com" commit -m "feat: broker dialogue reacts to mission state"
```

---

## Task 7: Warm TTS cache for all 5 state variants in `BarRefreshPatches`

**Files:**
- Modify: `VGAnima/Patches/BarRefreshPatches.cs:~170-200` (the pitch-warming block inside `InjectMissionBroker`)

- [ ] **Step 1: Replace the warming block**

Find the block that currently builds `warmedPairs` from `pitch.Lines` (just the Initial pitch). Replace the current content:

```csharp
        // Build pitch + override dialogueLines.
        var patronCtx = new PatronContext(newPatron.name, newPatron.isMale, station, mission);
        var pitch = plugin.PitchProvider.Pitch(patronCtx);
        var dialogueLines = new List<DialogueLine>(pitch.Lines.Count);
        foreach (var text in pitch.Lines)
        {
            if (string.IsNullOrWhiteSpace(text)) continue;
            var character = new Character(newPatron.name).WithPortret(newPatron.icon);
            dialogueLines.Add(DialogueLine.cDL(character, text));
        }
        Traverse.Create(newPatron).Field<List<DialogueLine>>("dialogueLines").Value = dialogueLines;

        // VGTTS: register voice + warm pitch lines in the background.
        var voice = newPatron.isMale ? ProceduralMaleVoice : ProceduralFemaleVoice;
        plugin.Vgtts.RegisterVoice(newPatron.name, voice);
        var warmedPairs = pitch.Lines
            .Select(text => (Speaker: newPatron.name, Text: text))
            .ToList();
```

with:

```csharp
        // Build the Initial pitch + override dialogueLines so the patron's
        // static field holds our text (SalesmanPatches rebuilds lines fresh
        // per click, but VGTTS's BarPatron.Initialize postfix reads the field
        // to warm synth — we want it seeing our text, not the vanilla variant).
        var patronCtx = new PatronContext(newPatron.name, newPatron.isMale, station, mission);
        var initialPitch = plugin.PitchProvider.PitchForState(patronCtx, BrokerState.Initial);
        var dialogueLines = new List<DialogueLine>(initialPitch.Lines.Count);
        foreach (var text in initialPitch.Lines)
        {
            if (string.IsNullOrWhiteSpace(text)) continue;
            var character = new Character(newPatron.name).WithPortret(newPatron.icon);
            dialogueLines.Add(DialogueLine.cDL(character, text));
        }
        Traverse.Create(newPatron).Field<List<DialogueLine>>("dialogueLines").Value = dialogueLines;

        // VGTTS: register voice + warm EVERY state's lines so switching state
        // mid-session doesn't pay live-synth cost on the first utterance of
        // that state. ~11 lines total across 5 states — well under a second.
        var voice = newPatron.isMale ? ProceduralMaleVoice : ProceduralFemaleVoice;
        plugin.Vgtts.RegisterVoice(newPatron.name, voice);
        var warmedPairs = new List<(string Speaker, string Text)>();
        foreach (BrokerState state in Enum.GetValues(typeof(BrokerState)))
        {
            var statePitch = plugin.PitchProvider.PitchForState(patronCtx, state);
            foreach (var text in statePitch.Lines)
            {
                if (string.IsNullOrWhiteSpace(text)) continue;
                warmedPairs.Add((newPatron.name, text));
            }
        }
```

- [ ] **Step 2: Verify build + tests**

Run: `make build && make test`
Expected: clean build; all tests pass.

- [ ] **Step 3: Commit**

```bash
git add VGAnima/Patches/BarRefreshPatches.cs
git -c user.name="Florian Kinder" -c user.email="florian.kinder@fankserver.com" commit -m "feat: pre-warm TTS for all broker dialogue states"
```

---

## Task 8: Manual E2E verification

No code change — structured smoke test to run after deploy.

- [ ] **Step 1: Deploy**

Close the game if running. Then:

```bash
make deploy
```

Expected: `Deployed VGAnima.dll to <plugin-dir>/VGAnima/` on stdout.

- [ ] **Step 2: Clean bar state (required)**

Phase A won't show different dialogue states for a broker whose mission was already posted in a prior session. Advance in-game time to the next day so `Bar.CheckUpdatePatrons` clears the roster and re-rolls. Alternatively start from a save made before Phase A's broker was injected.

- [ ] **Step 3: Verify Initial → Waiting transition**

1. Launch, dock at a station, open bar → broker is present.
2. Click broker. Dialogue shows the 3-line Initial pitch ending in "...posted on the station board. Grab it if you're in."
3. Log shows: `[vganima] 'NAME' dialogue state=Initial, 3 line(s)` then `Posted mission '[VGA] …' onto board`.
4. Click broker again (without opening the mission board). Dialogue shows the 2-line Waiting pitch: "It's still up on the board, Captain. / Take a look when you get a moment."
5. Log shows: `[vganima] 'NAME' dialogue state=Waiting, 2 line(s)`.

- [ ] **Step 4: Verify Waiting → InProgress → ReadyToClaim**

1. Open the mission board, accept the `[VGA]`-tagged mission.
2. Click broker. Dialogue: "Still working on that run? / Come back when it's done and we'll settle up."
3. Log: `state=InProgress`.
4. Fly to the destination, complete the mission objective.
5. Without claiming rewards at the mission board, click broker. Dialogue: "Ready to report in, Captain? / Head over to the mission board and wrap it up."
6. Log: `state=ReadyToClaim`.

- [ ] **Step 5: Verify Done**

1. Return to the mission board, claim the rewards via the vanilla "Complete" button.
2. Click broker. Dialogue: "Thanks for the work, Captain. / Safe travels out there."
3. Log: `state=Done`.

- [ ] **Step 6: Verify voice**

VGTTS should voice every state's lines with no live-synth lag (all were warmed at injection). If a state's first utterance is silent for ~1s then speaks, TTS warming didn't cover that state — investigate.

---

## Acceptance

All of:

- `make build` — 0 warnings, 0 errors.
- `make test` — all xUnit tests pass (13 original + new state tests).
- Manual E2E (Task 8 steps 3–6) — all five states produce the expected dialogue lines, logs confirm the state machine.
- Existing functionality unaffected: mission still posts on Initial dialogue close, still appears on board, still accepts via the vanilla board UI, still pays rewards via vanilla completion.

## Out-of-scope for Phase A

- Reward suppression (that's Phase B).
- NPC pinning while mission is active (Phase B).
- Save-state persistence beyond the `Pitched` flag in memory (Phase C).
- Follow-up mission chains (Phase C).
- LLM-generated dialogue variation (v0.2).

Per the notes doc: revisit those only after Phase A has been played with and feels right.
