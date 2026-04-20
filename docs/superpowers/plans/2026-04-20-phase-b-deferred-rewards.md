# Phase B — Deferred Rewards via Broker — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Reroute mission reward payout from the game's automatic completion to the broker NPC — broker intercepts `Mission.ClaimRewards`, snapshots the rewards, and pays them out when the player returns. Brokers stay pinned in the bar while a mission is active.

**Architecture:** One Harmony prefix on `Mission.ClaimRewards` snapshots `mission.rewards` into `ConversionRecord.PendingRewards` and clears the game's list — vanilla continues with an empty list (pays zero, still removes mission, still fires completion notification). A new `BrokerState.ReadyToCollect` covers the "mission resolved, rewards pending" phase; the broker's onComplete calls `reward.OnComplete()` on each snapshotted reward. `Bar.CheckUpdatePatrons` gains pin-and-restore behavior so brokers with active missions survive daily rollover. Save/load of `PendingRewards` is out of scope (deferred to Phase C); README documents the caveat.

**Tech Stack:** Existing — C# 11, netstandard2.1, BepInEx, HarmonyX, xUnit.

**Spec:** `docs/superpowers/specs/2026-04-20-phase-b-deferred-rewards-design.md`
**Notes:** `docs/superpowers/notes/2026-04-20-mission-lifecycle-extensions.md` (Phase B)

---

## File layout

| File | Change |
|---|---|
| `VGAnima/Cache/ConversionRecord.cs` | **Modify** — add nullable `PendingRewards` property |
| `VGAnima/Cache/ConversionRegistry.cs` | **Modify** — add `FindByValue(predicate)` |
| `VGAnima.Tests/Cache/ConversionRegistryTests.cs` | **Modify** — add FindByValue tests |
| `VGAnima/Pitch/BrokerState.cs` | **Modify** — add `ReadyToCollect` enum value |
| `VGAnima/Pitch/StaticPitchProvider.cs` | **Modify** — new state + revised ReadyToClaim |
| `VGAnima.Tests/Pitch/StaticPitchProviderTests.cs` | **Modify** — add ReadyToCollect fact + revised ReadyToClaim |
| `VGAnima/Patches/BrokerStateDetector.cs` | **Modify** — return `ReadyToCollect` when PendingRewards pending |
| `VGAnima/Patches/MissionRewardsPatches.cs` | **Create** — `Mission.ClaimRewards` prefix |
| `VGAnima/Patches/SalesmanPatches.cs` | **Modify** — `ClaimAndPay`, `PayPending`, wire state switch |
| `VGAnima/Patches/BarRefreshPatches.cs` | **Modify** — pinning via `_pinnedBrokers` + `IsActive` |
| `VGAnima/Plugin.cs` | **Modify** — `PatchAll(MissionRewardsPatches)` |
| `README.md` | **Modify** — save-timing caveat |

---

## Task 1: Add `PendingRewards` to `ConversionRecord`

**Files:**
- Modify: `VGAnima/Cache/ConversionRecord.cs`

- [ ] **Step 1: Replace the file contents**

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
///     <item>The <see cref="Pitched"/> flag (Phase A) distinguishing "never
///       talked" from "mission cycled through and got rewarded".</item>
///     <item>The <see cref="PendingRewards"/> snapshot (Phase B), populated by
///       the <c>Mission.ClaimRewards</c> prefix and drained by the broker's
///       payout dialogue. Null = not intercepted yet, empty = paid, non-empty
///       = awaiting broker payout.</item>
///   </list>
/// Not a record (was one originally) because <c>Pitched</c> and
/// <c>PendingRewards</c> must be mutable.</summary>
internal sealed class ConversionRecord
{
    public Mission Mission { get; }
    public IReadOnlyList<(string Speaker, string Text)> WarmedLines { get; }
    public SpaceStation Station { get; }
    public bool Pitched { get; set; }
    public List<MissionReward>? PendingRewards { get; set; }

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

- [ ] **Step 2: Verify build + tests**

Run: `make build && make test`
Expected: clean build; existing 20 tests still pass (ConversionRecord shape unchanged for existing ctor calls and property reads — new property is nullable with default null).

- [ ] **Step 3: Commit**

```bash
git add VGAnima/Cache/ConversionRecord.cs
git -c user.name="Florian Kinder" -c user.email="florian.kinder@fankserver.com" commit -m "feat: ConversionRecord.PendingRewards for deferred payout"
```

---

## Task 2: Add `FindByValue` to `ConversionRegistry` (TDD)

**Files:**
- Modify: `VGAnima/Cache/ConversionRegistry.cs`
- Modify: `VGAnima.Tests/Cache/ConversionRegistryTests.cs`

- [ ] **Step 1: Add failing tests**

Append to `VGAnima.Tests/Cache/ConversionRegistryTests.cs` (inside the existing `ConversionRegistryTests` class):

```csharp
    [Fact]
    public void FindByValue_ReturnsNull_WhenEmpty()
    {
        var reg = new ConversionRegistry<FakePatron, string>();
        Assert.Null(reg.FindByValue(s => s == "anything"));
    }

    [Fact]
    public void FindByValue_ReturnsMatch_WhenPredicateMatches()
    {
        var reg = new ConversionRegistry<FakePatron, string>();
        var a = new FakePatron();
        var b = new FakePatron();
        reg.Register(a, "alpha");
        reg.Register(b, "beta");

        Assert.Equal("beta", reg.FindByValue(s => s == "beta"));
    }

    [Fact]
    public void FindByValue_ReturnsNull_WhenNoMatch()
    {
        var reg = new ConversionRegistry<FakePatron, string>();
        reg.Register(new FakePatron(), "alpha");
        Assert.Null(reg.FindByValue(s => s == "omega"));
    }

    [Fact]
    public void FindByValue_ReturnsNullForReclaimedKeys_AfterGC()
    {
        // Smoke test that FindByValue doesn't throw when the table has
        // entries whose weak keys have been reclaimed. Not deterministic —
        // tolerate the result being either null or "alpha"; just must not throw.
        var reg = new ConversionRegistry<FakePatron, string>();
        reg.Register(new FakePatron(), "alpha");
        System.GC.Collect();
        System.GC.WaitForPendingFinalizers();
        var result = reg.FindByValue(s => s == "alpha");
        Assert.True(result is null or "alpha", $"unexpected: {result}");
    }
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `make test`
Expected: compile error — `ConversionRegistry` has no `FindByValue` method.

- [ ] **Step 3: Add `FindByValue` to `ConversionRegistry`**

Replace the file contents:

```csharp
using System;
using System.Runtime.CompilerServices;

namespace VGAnima.Cache;

/// <summary>Maps a game object (typically <c>BarPatron</c>) to an arbitrary
/// record via <see cref="ConditionalWeakTable{TKey, TValue}"/> so entries are
/// reclaimed when the game releases the patron. Thread-safety comes from the
/// underlying table.</summary>
internal sealed class ConversionRegistry<TKey, TValue>
    where TKey   : class
    where TValue : class
{
    private readonly ConditionalWeakTable<TKey, TValue> _table = new();

    public void Register(TKey key, TValue value) => _table.AddOrUpdate(key, value);

    public bool TryGet(TKey key, out TValue value) => _table.TryGetValue(key, out value!);

    public void Remove(TKey key) => _table.Remove(key);

    /// <summary>Reverse lookup: find the first value matching the predicate.
    /// O(N) in the number of live entries (ConditionalWeakTable drops GC'd keys
    /// from iteration automatically). Returns null if nothing matches.</summary>
    public TValue? FindByValue(Func<TValue, bool> predicate)
    {
        foreach (var kv in _table)
            if (predicate(kv.Value)) return kv.Value;
        return null;
    }
}
```

- [ ] **Step 4: Run tests to verify they pass**

Run: `make test`
Expected: all tests pass (24 total — 20 previous + 4 new FindByValue).

- [ ] **Step 5: Commit**

```bash
git add VGAnima/Cache/ConversionRegistry.cs VGAnima.Tests/Cache/ConversionRegistryTests.cs
git -c user.name="Florian Kinder" -c user.email="florian.kinder@fankserver.com" commit -m "feat: ConversionRegistry.FindByValue for reverse lookup"
```

---

## Task 3: Add `ReadyToCollect` to `BrokerState` enum

**Files:**
- Modify: `VGAnima/Pitch/BrokerState.cs`

- [ ] **Step 1: Replace the file contents**

```csharp
namespace VGAnima.Pitch;

/// <summary>The broker's dialogue state, derived from game-observable data
/// plus in-memory bits on <c>ConversionRecord</c> (Pitched, PendingRewards).
/// Not persisted — recomputed from scratch after reload (Phase C will add
/// persistence).</summary>
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

    /// <summary>Mission complete and ready to wrap up via the broker. Phase B
    /// onComplete triggers <c>Mission.ClaimRewards</c> (which our prefix
    /// intercepts) and pays out in the same click.</summary>
    ReadyToClaim,

    /// <summary>Phase B. Mission already resolved by the game (Courier
    /// auto-complete or player clicked Complete at the board), rewards
    /// snapshotted to <c>ConversionRecord.PendingRewards</c>, broker owes
    /// payout on the next visit.</summary>
    ReadyToCollect,

    /// <summary>Mission cycled through and rewards have been delivered.
    /// Broker wraps up and departs.</summary>
    Done,
}
```

- [ ] **Step 2: Verify build + tests**

Run: `make build && make test`
Expected: clean; StaticPitchProvider's switch has a `_` fallback so adding an enum value doesn't break compile. Tests pass.

- [ ] **Step 3: Commit**

```bash
git add VGAnima/Pitch/BrokerState.cs
git -c user.name="Florian Kinder" -c user.email="florian.kinder@fankserver.com" commit -m "feat: BrokerState.ReadyToCollect for Phase B payout state"
```

---

## Task 4: Update `StaticPitchProvider` for `ReadyToCollect` + revised `ReadyToClaim` (TDD)

**Files:**
- Modify: `VGAnima/Pitch/StaticPitchProvider.cs`
- Modify: `VGAnima.Tests/Pitch/StaticPitchProviderTests.cs`

- [ ] **Step 1: Update tests**

Replace the tests file with:

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
    public void ReadyToClaim_NudgesToBroker()
    {
        // Phase B revised ReadyToClaim: broker handles everything, no board redirect.
        var provider = new StaticPitchProvider();
        var result = provider.PitchForState(Ctx(), BrokerState.ReadyToClaim);
        Assert.NotEmpty(result.Lines);
        // Phase A previously said "head to the board"; Phase B replaces with
        // broker-centric wording. No "board" keyword in the revised lines.
        Assert.DoesNotContain(result.Lines, l => l.ToLowerInvariant().Contains("board"));
    }

    [Fact]
    public void ReadyToCollect_MentionsPayment()
    {
        var provider = new StaticPitchProvider();
        var result = provider.PitchForState(Ctx(), BrokerState.ReadyToCollect);
        Assert.NotEmpty(result.Lines);
        Assert.Contains(result.Lines, l => l.ToLowerInvariant().Contains("pay"));
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
    [InlineData(BrokerState.ReadyToCollect)]
    [InlineData(BrokerState.Done)]
    internal void AllStates_UseAsciiOnlyPunctuation(BrokerState state)
    {
        var provider = new StaticPitchProvider();
        var result = provider.PitchForState(Ctx(), state);
        foreach (var line in result.Lines)
            foreach (var ch in line)
                Assert.True(ch < 128,
                    $"Non-ASCII char U+{(int)ch:X4} '{ch}' in {state} line: \"{line}\"");
    }
}
```

- [ ] **Step 2: Run tests to verify failures**

Run: `make test`
Expected: failures — `ReadyToClaim` test fails (existing text contains "board") + `ReadyToCollect` has no arm yet (fallback returns "Captain." with no "pay").

- [ ] **Step 3: Update the provider**

Replace the file contents:

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
            "Ready to wrap this one up, Captain?",
            "Step over when you're ready and we'll close it out.",
        }),
        BrokerState.ReadyToCollect => new PitchResult(new[]
        {
            "Good work out there, Captain.",
            "Here's your pay. Everything you were owed.",
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

- [ ] **Step 4: Run tests to verify pass**

Run: `make test`
Expected: all tests pass. Theory now covers 6 states (one more than before).

- [ ] **Step 5: Commit**

```bash
git add VGAnima/Pitch/StaticPitchProvider.cs VGAnima.Tests/Pitch/StaticPitchProviderTests.cs
git -c user.name="Florian Kinder" -c user.email="florian.kinder@fankserver.com" commit -m "feat: Phase B pitch lines — ReadyToCollect + broker-centric ReadyToClaim"
```

---

## Task 5: Update `BrokerStateDetector` for `ReadyToCollect`

**Files:**
- Modify: `VGAnima/Patches/BrokerStateDetector.cs`

- [ ] **Step 1: Replace the file contents**

```csharp
using Source.Player;
using VGAnima.Cache;
using VGAnima.Pitch;

namespace VGAnima.Patches;

/// <summary>Computes the current <see cref="BrokerState"/> from
/// game-observable state plus the in-memory <see cref="ConversionRecord.Pitched"/>
/// and <see cref="ConversionRecord.PendingRewards"/> bits (cleared on plugin
/// reload, not persisted to the savegame). No side effects.</summary>
internal static class BrokerStateDetector
{
    public static BrokerState Detect(ConversionRecord record)
    {
        if (record == null) return BrokerState.Initial;
        var mission = record.Mission;
        var station = record.Station;

        // Player has the mission in their active list — either still working
        // on it or ready to claim rewards (via broker, not the board).
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

        // Mission absent from both board and player missions. Three sub-cases
        // distinguished by Pitched + PendingRewards:
        //   Pitched + rewards pending → ReadyToCollect (Phase B: broker owes payout)
        //   Pitched + no rewards      → Done           (payout delivered OR vanilla auto-paid)
        //   !Pitched                   → Initial        (first contact)
        if (record.Pitched)
        {
            if (record.PendingRewards != null && record.PendingRewards.Count > 0)
                return BrokerState.ReadyToCollect;
            return BrokerState.Done;
        }
        return BrokerState.Initial;
    }
}
```

- [ ] **Step 2: Verify build + tests**

Run: `make build && make test`
Expected: clean; 25 tests pass (existing behavior unchanged, new arm only fires when `PendingRewards` is non-null non-empty — currently never set, so detector still yields same outputs as Phase A).

- [ ] **Step 3: Commit**

```bash
git add VGAnima/Patches/BrokerStateDetector.cs
git -c user.name="Florian Kinder" -c user.email="florian.kinder@fankserver.com" commit -m "feat: detector returns ReadyToCollect when rewards pending"
```

---

## Task 6: Create `MissionRewardsPatches` — `Mission.ClaimRewards` prefix

**Files:**
- Create: `VGAnima/Patches/MissionRewardsPatches.cs`

- [ ] **Step 1: Write the patch class**

```csharp
using System.Collections.Generic;
using HarmonyLib;
using Source.MissionSystem;

namespace VGAnima.Patches;

/// <summary>
/// Prefix on <see cref="Mission.ClaimRewards"/> for missions owned by a
/// VGAnima broker. Snapshots <c>mission.rewards</c> into the record's
/// <c>PendingRewards</c> and clears the list BEFORE vanilla iterates. Vanilla
/// then loops over an empty list — no <c>reward.OnComplete()</c> calls, no
/// payout. The rest of <c>ClaimRewards</c> (RemoveMission, notification,
/// per-step OnMissionTurnedIn) runs normally, so the game still marks the
/// mission resolved. The broker pays the snapshotted rewards later via
/// <c>SalesmanPatches.PayPending</c>.
///
/// Missions NOT owned by us pass through unchanged.
/// </summary>
[HarmonyPatch(typeof(Mission))]
internal static class MissionRewardsPatches
{
    [HarmonyPrefix]
    [HarmonyPatch(nameof(Mission.ClaimRewards))]
    private static void ClaimRewards_Prefix(Mission __instance)
    {
        try
        {
            if (Plugin.Instance is not { } plugin) return;

            // Reverse-lookup the ConversionRecord this mission belongs to.
            var record = plugin.Registry.FindByValue(r => ReferenceEquals(r.Mission, __instance));
            if (record == null) return;                // not our mission
            if (record.PendingRewards != null) return; // already snapshotted (idempotent)

            record.PendingRewards = new List<MissionReward>(__instance.rewards);
            __instance.rewards.Clear();

            Plugin.Log.LogInfo(
                $"[vganima] Intercepted reward payout for '{__instance.name}' — " +
                $"{record.PendingRewards.Count} rewards deferred to broker");
        }
        catch (System.Exception ex)
        {
            Plugin.Log.LogError($"[vganima] ClaimRewards_Prefix threw: {ex}");
        }
    }
}
```

- [ ] **Step 2: Verify build**

Run: `make build`
Expected: clean. The patch isn't yet registered in Plugin.Awake (Task 9 does that) — it's a passive class sitting in the tree.

- [ ] **Step 3: Run existing tests**

Run: `make test`
Expected: 25 tests still pass — no test exercises this patch directly.

- [ ] **Step 4: Commit**

```bash
git add VGAnima/Patches/MissionRewardsPatches.cs
git -c user.name="Florian Kinder" -c user.email="florian.kinder@fankserver.com" commit -m "feat: Mission.ClaimRewards prefix snapshots VGAnima rewards"
```

---

## Task 7: Wire `SalesmanPatches` with `ClaimAndPay` + `PayPending`

**Files:**
- Modify: `VGAnima/Patches/SalesmanPatches.cs`

- [ ] **Step 1: Replace the file contents**

```csharp
using System;
using System.Collections.Generic;
using Behaviour.Dialogues;
using Behaviour.Util;
using HarmonyLib;
using Source.Dialogues;
using Source.Galaxy.POI.Station.Patrons;
using Source.MissionSystem;
using VGAnima.Cache;
using VGAnima.Pitch;

namespace VGAnima.Patches;

/// <summary>
/// Intercepts <see cref="Salesman.InteractWithPatron"/> for brokers in the
/// registry. Rebuilds dialogue lines per click from
/// <see cref="BrokerStateDetector"/> so the broker's speech tracks the
/// player's progress on the pitched mission. Per-state onComplete:
///   Initial        → post mission to the board
///   ReadyToClaim   → call ClaimRewards (triggers our prefix) + pay
///   ReadyToCollect → pay the pre-snapshotted rewards
///   Done           → depart the bar
///   others         → no-op
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
            if (lines.Count == 0) return true;

            Action onComplete = state switch
            {
                BrokerState.Initial        => () => PostMission(record),
                BrokerState.ReadyToClaim   => () => ClaimAndPay(record),
                BrokerState.ReadyToCollect => () => PayPending(record),
                BrokerState.Done           => () => Depart(__instance, record),
                _                          => NoOp,
            };

            Plugin.Log.LogDebug(
                $"[vganima] '{__instance.name}' dialogue state={state}, {lines.Count} line(s)");

            Singleton<DialogueManager>.Instance.StartDialogue(lines, onComplete);
            return false;
        }
        catch (Exception ex)
        {
            Plugin.Log.LogError($"[vganima] InteractWithPatron_Prefix threw: {ex}");
            return true;
        }
    }

    /// <summary>Posts the broker's mission to the station board on the first
    /// dialogue close. Idempotent; also marks <see cref="ConversionRecord.Pitched"/>.</summary>
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

    /// <summary>Called when the player clicks the broker in ReadyToClaim state.
    /// Triggers the mission's ClaimRewards (our prefix intercepts and
    /// snapshots PendingRewards) then immediately pays them. One click covers
    /// the full broker-direct completion path.</summary>
    private static void ClaimAndPay(ConversionRecord record)
    {
        try
        {
            record.Mission.ClaimRewards();   // our Mission prefix fires here
            PayPending(record);
        }
        catch (Exception ex)
        {
            Plugin.Log.LogError($"[vganima] ClaimAndPay threw: {ex}");
        }
    }

    /// <summary>Called when the player clicks the broker in ReadyToCollect
    /// state (or immediately after ClaimAndPay's ClaimRewards step).
    /// Iterates <see cref="ConversionRecord.PendingRewards"/> and calls
    /// <c>OnComplete()</c> on each — this is what actually pays out credits,
    /// XP, items, etc. Clears the list when done.</summary>
    private static void PayPending(ConversionRecord record)
    {
        try
        {
            if (record.PendingRewards == null) return;
            var count = record.PendingRewards.Count;
            foreach (var reward in record.PendingRewards)
            {
                try { reward.OnComplete(); }
                catch (Exception ex)
                {
                    Plugin.Log.LogError(
                        $"[vganima] Reward.OnComplete threw for '{reward.GetType().Name}': {ex}");
                }
            }
            record.PendingRewards.Clear();
            Plugin.Log.LogInfo(
                $"[vganima] Paid {count} rewards for '{record.Mission.name}'");
        }
        catch (Exception ex)
        {
            Plugin.Log.LogError($"[vganima] PayPending threw: {ex}");
        }
    }

    /// <summary>Removes the broker from the bar's roster and drops their TTS
    /// cache after the Done dialogue closes. Keeps the registry entry so any
    /// re-click before BarUI refreshes still routes through our prefix
    /// (same Done dialogue) instead of falling through to vanilla
    /// ShowSalesmanInfo. Registry entry GCs naturally once BarUI destroys the
    /// broker's prefab on its next RefreshPatrons call.</summary>
    private static void Depart(Salesman patron, ConversionRecord record)
    {
        try
        {
            if (Plugin.Instance is not { } plugin) return;

            var bar = record.Station?.bar;
            var removed = bar != null && bar.availablePatrons.Remove(patron);

            foreach (var (speaker, text) in record.WarmedLines)
                plugin.Vgtts.DropCache(speaker, text);

            Plugin.Log.LogInfo(
                $"[vganima] Broker '{patron.name}' departed after mission completion " +
                $"(removed from roster: {removed})");
        }
        catch (Exception ex)
        {
            Plugin.Log.LogError($"[vganima] Depart threw: {ex}");
        }
    }

    private static readonly Action NoOp = static () => { };
}
```

- [ ] **Step 2: Verify build + tests**

Run: `make build && make test`
Expected: clean; 25 tests still pass. The `MissionRewardsPatches` class from Task 6 exists but isn't PatchAll'd yet (Task 9) — `ClaimAndPay`'s `record.Mission.ClaimRewards()` call will fall through to pure vanilla and auto-pay until then, which is fine because nothing exercises it end-to-end in this task.

- [ ] **Step 3: Commit**

```bash
git add VGAnima/Patches/SalesmanPatches.cs
git -c user.name="Florian Kinder" -c user.email="florian.kinder@fankserver.com" commit -m "feat: broker ClaimAndPay + PayPending for Phase B payout"
```

---

## Task 8: Extend `BarRefreshPatches` with broker pinning

**Files:**
- Modify: `VGAnima/Patches/BarRefreshPatches.cs`

- [ ] **Step 1: Add the pin-and-restore logic**

Find the existing `BarRefreshPatches` class. Inside it, alongside the existing `_snapshots` field, **add** a second field:

```csharp
    /// <summary>Phase B: brokers whose mission is still active (either in
    /// <see cref="Source.Player.GamePlayer.missions"/> or with
    /// <see cref="ConversionRecord.PendingRewards"/> pending). Snapshotted in
    /// the prefix, re-inserted in the postfix so daily rollover doesn't delete
    /// the quest-giver mid-mission.</summary>
    private static readonly ConditionalWeakTable<Bar, List<BarPatron>> _pinnedBrokers = new();
```

**Replace** the existing `CheckUpdatePatrons_Prefix` body with:

```csharp
    [HarmonyPrefix]
    [HarmonyBefore("vgtts")]
    [HarmonyPatch(nameof(Bar.CheckUpdatePatrons))]
    private static void CheckUpdatePatrons_Prefix(Bar __instance)
    {
        _snapshots.AddOrUpdate(__instance, new List<BarPatron>(__instance.availablePatrons));

        if (Plugin.Instance is not { } plugin) return;
        var pinned = new List<BarPatron>();
        foreach (var p in __instance.availablePatrons)
        {
            if (!plugin.Registry.TryGet(p, out var record)) continue;
            if (IsActive(record)) pinned.Add(p);
        }
        if (pinned.Count > 0) _pinnedBrokers.AddOrUpdate(__instance, pinned);
    }
```

**Replace** the existing `CheckUpdatePatrons_Postfix` body with:

```csharp
    [HarmonyPostfix]
    [HarmonyAfter("vgtts")]
    [HarmonyPatch(nameof(Bar.CheckUpdatePatrons))]
    private static void CheckUpdatePatrons_Postfix(Bar __instance)
    {
        try
        {
            // Restore pinned brokers to the roster before eviction + injection run,
            // so idempotency sees them and EvictRolledOff's diff doesn't include them.
            if (_pinnedBrokers.TryGetValue(__instance, out var pinned))
            {
                _pinnedBrokers.Remove(__instance);
                foreach (var p in pinned)
                    if (!__instance.availablePatrons.Contains(p))
                        __instance.availablePatrons.Add(p);
            }

            EvictRolledOff(__instance);
            InjectMissionBroker(__instance);
        }
        catch (Exception ex)
        {
            Plugin.Log.LogError($"[vganima] CheckUpdatePatrons_Postfix threw: {ex}");
        }
    }
```

**Add** the `IsActive` helper inside the class (near `InjectMissionBroker`):

```csharp
    /// <summary>A broker is "active" if their mission is still in the player's
    /// missions list, OR their rewards have been snapshotted (awaiting broker
    /// payout). Active brokers survive daily bar rollover.</summary>
    private static bool IsActive(ConversionRecord record)
    {
        if (Source.Player.GamePlayer.current?.missions?.Contains(record.Mission) == true) return true;
        if (record.PendingRewards != null && record.PendingRewards.Count > 0) return true;
        return false;
    }
```

- [ ] **Step 2: Verify build + tests**

Run: `make build && make test`
Expected: clean; 25 tests still pass.

- [ ] **Step 3: Commit**

```bash
git add VGAnima/Patches/BarRefreshPatches.cs
git -c user.name="Florian Kinder" -c user.email="florian.kinder@fankserver.com" commit -m "feat: pin active brokers across daily bar rollover"
```

---

## Task 9: Register `MissionRewardsPatches` in `Plugin.Awake`

**Files:**
- Modify: `VGAnima/Plugin.cs`

- [ ] **Step 1: Add the PatchAll call**

Find the `_harmony.PatchAll(typeof(BarRefreshPatches));` line in `Plugin.Awake`. **Add** the MissionRewardsPatches registration immediately after it.

The relevant block should go from:

```csharp
        _harmony = new Harmony(PluginGuid);
        _harmony.PatchAll(typeof(SalesmanPatches));
        _harmony.PatchAll(typeof(BarRefreshPatches));
        _harmony.PatchAll(typeof(BarUIDebugPatches));
        _harmony.PatchAll(typeof(BarPatronImageDebugPatches));
```

to:

```csharp
        _harmony = new Harmony(PluginGuid);
        _harmony.PatchAll(typeof(SalesmanPatches));
        _harmony.PatchAll(typeof(BarRefreshPatches));
        _harmony.PatchAll(typeof(MissionRewardsPatches));
        _harmony.PatchAll(typeof(BarUIDebugPatches));
        _harmony.PatchAll(typeof(BarPatronImageDebugPatches));
```

- [ ] **Step 2: Verify build + tests**

Run: `make build && make test`
Expected: clean; 25 tests pass.

- [ ] **Step 3: Commit**

```bash
git add VGAnima/Plugin.cs
git -c user.name="Florian Kinder" -c user.email="florian.kinder@fankserver.com" commit -m "feat: register MissionRewardsPatches in PatchAll"
```

---

## Task 10: README — save-timing caveat

**Files:**
- Modify: `README.md`

- [ ] **Step 1: Add the caveat section**

Open `README.md`. Find the "Troubleshooting" section. Immediately **before** it, insert a new section:

```markdown
## Known limitation — save during mid-mission payout

When a mission completes, the reward payout is deferred to the broker (v0.2+). If you **save the game while rewards are pending** (mission resolved but broker hasn't paid yet), those rewards are lost on reload: the broker's pending-reward snapshot lives only in memory.

**Practical impact:** if you deliver a courier run and then save before returning to the broker, the snapshotted rewards evaporate.

**Workaround:** always talk to the broker and collect your pay before saving. Phase C will persist pending rewards to `BepInEx/cache/VGAnima/` so this limitation goes away.

```

- [ ] **Step 2: Commit**

```bash
git add README.md
git -c user.name="Florian Kinder" -c user.email="florian.kinder@fankserver.com" commit -m "docs: README caveat about save-during-payout losing rewards"
```

---

## Task 11: Manual E2E verification

No code change. Run the tests in-game.

- [ ] **Step 1: Deploy**

Close the game if running. Then:

```bash
make deploy
```

Expected: `Deployed VGAnima.dll to <plugin-dir>/VGAnima/` on stdout.

- [ ] **Step 2: Clean bar state**

Load a save **without** a pre-existing broker (day-refreshed, or a save from before Phase B testing). If the bar still has a Phase A broker, advance in-game time to clear it.

- [ ] **Step 3: Courier path (auto-complete at destination)**

1. Dock, enter bar, click broker → `state=Initial, 3 line(s)` in log + `Posted mission '[VGA] …' onto board`.
2. Open mission board, accept the `[VGA]`-tagged Courier mission.
3. Fly to destination station, trade cargo (mission auto-completes).
4. **Expect in log:** `[vganima] Intercepted reward payout for '[VGA] …' — N rewards deferred to broker`.
5. **Expect:** credits/XP have NOT hit your inventory yet (check the UI counters).
6. Return to source station, re-enter bar. Broker should still be there (pinned).
7. Click broker → `state=ReadyToCollect, 2 line(s)` + "Good work out there, Captain. / Here's your pay…". On close: `[vganima] Paid N rewards for '[VGA] …'`.
8. **Expect:** credits/XP now hit inventory.
9. Click broker again → `state=Done, 2 line(s)`, "Thanks for the work, Captain. / Safe travels out there." On close: `[vganima] Broker '…' departed after mission completion (removed from roster: True)`.
10. Exit bar UI and re-enter → broker is gone.

- [ ] **Step 4: Non-Courier via broker (if a non-Courier mission is offered)**

1. Find a station where the broker offers a non-Courier mission type (BountyHunt, ClearSalvageField, etc. — depends on `Cfg.MissionTypes`). If `MissionTypes=Courier` only, skip this step or temporarily add others to the config.
2. Accept from board, complete objectives. Return to broker.
3. Click broker → `state=ReadyToClaim, 2 line(s)` + "Ready to wrap this one up…".
4. On dialogue close: one log entry with BOTH `Intercepted …` AND `Paid …` — our prefix fires on `record.Mission.ClaimRewards()` in `ClaimAndPay`, then `PayPending` runs immediately.
5. Credits/XP hit inventory.
6. Click broker again → `state=Done`, depart.

- [ ] **Step 5: Non-Courier via mission board**

1. Accept a non-Courier mission at the station, complete objectives.
2. Open the mission board, click "Complete" on the `[VGA]` mission row.
3. **Expect:** "Mission Completed" notification from the game, log `[vganima] Intercepted reward payout …`, but NO credits/XP yet.
4. Return to the bar, click broker → `state=ReadyToCollect` + payout.
5. Credits/XP hit inventory.
6. Next click → Done → depart.

- [ ] **Step 6: Broker pinning**

1. Accept a mission from the broker (long one — not instant-complete).
2. Advance in-game time past a daily bar rollover (wait ~1 in-game day).
3. Re-enter the bar. Broker should still be present. Log should show no `[vganima] Evicted rolled-off patron` entry for them.
4. The bar's other patrons may have rolled over (vanilla salesmen replaced). Ours stays.
5. `InjectMissionBroker`'s idempotency prevents a second broker spawning alongside the pinned one.

- [ ] **Step 7: Save/load caveat check**

1. Accept a Courier mission, complete it, observe `Intercepted …` log. Do NOT talk to broker yet.
2. Save the game.
3. Reload the save.
4. **Expect:** no broker for the completed mission (registry in-memory, cleared on reload). No credits/XP in inventory. This is the documented limitation — confirm the README entry matches.

---

## Acceptance

All of:

- `make build` — 0 warnings, 0 errors after every task.
- `make test` — all xUnit tests pass (baseline 20 + 4 new FindByValue + 1 new ReadyToCollect + 1 extra theory row; plus revised ReadyToClaim assertion — ~26 total).
- Task 11 steps 3 (Courier), 4 (non-Courier broker path), 5 (non-Courier board path), 6 (pinning) all behave per spec.
- Step 7 confirms the save/load limitation matches the README caveat.

## Out-of-scope for Phase B

- Saving `PendingRewards` + `Pitched` to disk (Phase C: `BepInEx/cache/VGAnima/<save-guid>.json`).
- Follow-up mission chains via `MissionFollowUp` reward type (Phase C).
- Moving the broker to the Airlock (declined — Airlock is for main-story NPCs).
- LLM-generated dialogue variation (v0.2).
