# VGAnima Phase B — Deferred Rewards via Broker

Status: approved for implementation planning
Date: 2026-04-20
Parent notes: `docs/superpowers/notes/2026-04-20-mission-lifecycle-extensions.md` (Phase B section)
Prior phase: `docs/superpowers/specs/2026-04-20-vganima-design.md` + `docs/superpowers/plans/2026-04-20-phase-a-dialogue-state.md`

## Purpose

Move reward payout from the game's automatic flow to the broker NPC. The broker pitches, the player completes the mission, the broker pays — not the mission board, not auto-complete. Makes the broker the single quest-giver-and-taker, matching the narrative arc users sketched in the 8-step flow.

## Non-goals

- Save/load persistence of `PendingRewards` — deferred to Phase C.
- Follow-up mission chains — deferred to Phase C.
- Moving the broker to the Airlock NPC slot — Airlock is reserved for main-story NPCs (Creed, Questgiver*) and is a single-person slot. Bar broker stays.
- LLM-generated dialogue variation — deferred to v0.2.

## Scope summary

Phase B adds to the Phase A broker:

1. **Reward suppression.** A Harmony prefix on `Mission.ClaimRewards` intercepts our missions: snapshots `mission.rewards` into `ConversionRecord.PendingRewards`, clears the list. Vanilla continues — pays zero, still removes mission from `GamePlayer.missions`, still fires "Mission Completed" notification.
2. **Broker payout.** New `BrokerState.ReadyToCollect` covers "mission resolved, rewards pending". Broker dialogue + onComplete iterates `PendingRewards` and calls `reward.OnComplete()` on each.
3. **Broker pinning.** `Bar.CheckUpdatePatrons` prefix snapshots brokers whose mission is active (`mission ∈ GamePlayer.missions` or `PendingRewards.Any()`); postfix re-inserts them so daily rollover doesn't delete the quest-giver.
4. **Unified broker-driven completion.** `ReadyToClaim` dialogue onComplete calls `mission.ClaimRewards()` + pays in one visit — player never has to use the mission-board "Complete" button. The board path still works (our prefix fires regardless of caller).

## Architecture

### Reward interception — `Mission.ClaimRewards` prefix

```csharp
[HarmonyPatch(typeof(Mission))]
internal static class MissionRewardsPatches
{
    [HarmonyPrefix]
    [HarmonyPatch(nameof(Mission.ClaimRewards))]
    private static void ClaimRewards_Prefix(Mission __instance)
    {
        if (Plugin.Instance is not { } plugin) return;
        var record = plugin.Registry.FindByMission(__instance);
        if (record == null) return;                      // not ours
        if (record.PendingRewards != null) return;        // already snapshotted

        record.PendingRewards = new List<MissionReward>(__instance.rewards);
        __instance.rewards.Clear();
        Plugin.Log.LogInfo(
            $"[vganima] Intercepted reward payout for '{__instance.name}' — " +
            $"{record.PendingRewards.Count} rewards deferred to broker");
    }
}
```

Key properties:

- **Prefix, not postfix.** We mutate `rewards` *before* vanilla iterates. Vanilla's `foreach (reward in rewards) reward.OnComplete()` loop runs over an empty list → no payout. The rest of `ClaimRewards` (`GamePlayer.RemoveMission`, completion notification, per-step `OnMissionTurnedIn`) runs normally.
- **Reverse lookup on Registry.** `ConversionRegistry<BarPatron, ConversionRecord>` gains a `FindByMission(Mission m)` helper. O(N) iteration in broker count; trivial (≤1 per bar, handful of bars in memory).
- **Idempotency guard.** `BountyMission.ClaimRewards` / `IndustryMission.ClaimRewards` override and call `base.ClaimRewards()`. Our prefix fires on the base. If any subclass path triggers twice for the same mission, the `PendingRewards != null` short-circuit prevents double-snapshot.

### New state — `ReadyToCollect`

```csharp
internal enum BrokerState
{
    Initial, Waiting, InProgress, ReadyToClaim,
    ReadyToCollect,                                    // NEW
    Done,
}
```

`BrokerStateDetector.Detect`:

```csharp
// ... existing handling for mission on board / in player missions / CanClaimRewards ...

// Mission absent from both lists → distinguish by Pitched + PendingRewards.
if (record.Pitched)
{
    if (record.PendingRewards != null && record.PendingRewards.Count > 0)
        return BrokerState.ReadyToCollect;
    return BrokerState.Done;
}
return BrokerState.Initial;
```

### Dialogue per state

| State | Lines |
|---|---|
| `Initial` | (unchanged from Phase A) |
| `Waiting` | (unchanged) |
| `InProgress` | (unchanged) |
| `ReadyToClaim` | **revised** — "Ready to wrap this one up, Captain?" / "Step over when you're ready and we'll close it out." |
| `ReadyToCollect` | **new** — "Good work out there, Captain." / "Here's your pay. Everything you were owed." |
| `Done` | (unchanged — "Thanks for the work, Captain." / "Safe travels out there.") |

ASCII-only constraint continues (enforced by existing `AllStates_UseAsciiOnlyPunctuation` theory test).

### onComplete per state — `SalesmanPatches`

```csharp
Action onComplete = state switch
{
    BrokerState.Initial        => () => PostMission(record),
    BrokerState.ReadyToClaim   => () => ClaimAndPay(record),
    BrokerState.ReadyToCollect => () => PayPending(record),
    BrokerState.Done           => () => Depart(__instance, record),
    _                          => NoOp,
};

private static void ClaimAndPay(ConversionRecord record)
{
    // Triggers our Mission.ClaimRewards prefix. After this call: mission
    // removed from GamePlayer.missions, PendingRewards populated.
    record.Mission.ClaimRewards();
    PayPending(record);
}

private static void PayPending(ConversionRecord record)
{
    if (record.PendingRewards == null) return;
    foreach (var reward in record.PendingRewards)
    {
        try { reward.OnComplete(); }
        catch (Exception ex)
        {
            Plugin.Log.LogError(
                $"[vganima] Reward.OnComplete threw for '{reward.GetType().Name}': {ex}");
        }
    }
    Plugin.Log.LogInfo(
        $"[vganima] Paid {record.PendingRewards.Count} rewards for '{record.Mission.name}'");
    record.PendingRewards.Clear();
}
```

### Broker pinning — extended `BarRefreshPatches`

```csharp
private static readonly ConditionalWeakTable<Bar, List<BarPatron>> _pinnedBrokers = new();

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

[HarmonyPostfix]
[HarmonyAfter("vgtts")]
[HarmonyPatch(nameof(Bar.CheckUpdatePatrons))]
private static void CheckUpdatePatrons_Postfix(Bar __instance)
{
    try
    {
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

private static bool IsActive(ConversionRecord record)
{
    if (GamePlayer.current?.missions?.Contains(record.Mission) == true) return true;
    if (record.PendingRewards != null && record.PendingRewards.Count > 0) return true;
    return false;
}
```

- `InjectMissionBroker`'s existing idempotency check (`any patron in registry → return`) prevents a second broker spawning alongside a pinned one.
- `EvictRolledOff` sees the pinned broker re-added to `availablePatrons` and skips eviction for it.

### `ConversionRecord` additions

```csharp
internal sealed class ConversionRecord
{
    // existing: Mission, WarmedLines, Station, Pitched
    public List<MissionReward>? PendingRewards { get; set; }
}
```

Nullable. `null` = not intercepted yet. Empty = intercepted and paid. Non-empty = intercepted, payout pending.

### `ConversionRegistry` addition

```csharp
internal sealed class ConversionRegistry<TKey, TValue> where TKey : class where TValue : class
{
    // existing: _table, Register, TryGet, Remove

    public TValue? FindByValue(Func<TValue, bool> predicate)
    {
        foreach (var kv in _table)
            if (predicate(kv.Value)) return kv.Value;
        return null;
    }
}
```

Callers use `plugin.Registry.FindByValue(r => r.Mission == mission)` — keeps the registry generic (no Mission-specific coupling).

## Transition flows

### Courier (auto-completes at destination)

```
Initial → Waiting → InProgress
  → (game completes at destination → ClaimRewards fires → our prefix snapshots → vanilla removes)
→ ReadyToCollect (broker pinned at source station)
  → (player returns, clicks broker → PayPending → Done)
→ Done
  → (next click → Depart)
```

### Non-Courier via broker

```
Initial → Waiting → InProgress → ReadyToClaim
  → (player clicks broker → ClaimAndPay → ClaimRewards fires → prefix snapshots → vanilla removes → PayPending)
→ Done (jumps straight through ReadyToCollect in one onComplete)
  → (next click → Depart)
```

### Non-Courier via mission board

```
Initial → Waiting → InProgress → ReadyToClaim
  → (player clicks "Complete" at mission board → ClaimRewards fires → prefix snapshots → vanilla removes)
→ ReadyToCollect
  → (player visits broker → PayPending → Done)
```

All three paths terminate at `Done` with rewards paid exactly once, via the broker, through `reward.OnComplete()`.

## Save/load limitation

`ConversionRecord` is in-memory only. Save behaviors:

| Scenario | Outcome |
|---|---|
| Save before any completion | Graceful — on reload our prefix is absent, vanilla auto-pays normally when mission completes. Narrative beat lost, rewards intact. |
| Save in `ReadyToClaim` | Same — `mission.rewards` still populated, prefix never fired. Vanilla auto-pays on next completion path. |
| Save in `ReadyToCollect` | **Rewards lost.** `mission.rewards` already cleared (on disk), `PendingRewards` was in memory only. |
| Save in `Done` | Harmless. |

README mitigation: *"Collect your pay from the broker promptly after completing a mission — saving before the broker pays out loses the rewards."*

Phase C adds persistence at `BepInEx/cache/VGAnima/<save-guid>.json` and closes this gap.

## Testing strategy

### Unit (xUnit, no game runtime)

- `StaticPitchProvider.PitchForState(ReadyToCollect)` — non-empty, contains "pay" or similar, ASCII-only (covered by existing theory).
- `StaticPitchProvider.PitchForState(ReadyToClaim)` — revised text passes existing assertions (non-empty, ASCII-only).
- `ConversionRegistry<TKey, TValue>.FindByValue` — using `FakePatron` sentinels with simple string values, verify lookup hits and misses.

### Manual E2E (documented in README, run after deploy)

1. **Courier path:** dock, enter bar, click broker (Initial pitch), accept mission, fly to destination, complete delivery.
   - Expected logs on completion: `[vganima] Intercepted reward payout for '[VGA] …' — N rewards deferred to broker`.
   - Expected: no credits/XP hit inventory yet.
   - Return to source station, click broker. Dialogue: "Good work out there, Captain. / Here's your pay…" Log: `[vganima] Paid N rewards for '[VGA] …'`. Credits/XP/etc. now hit inventory.
   - Next click: Done dialogue, broker departs on close.

2. **Non-Courier via broker:** complete a non-Courier `[VGA]` mission, return to broker in `ReadyToClaim` state. Click broker. Dialogue: "Ready to wrap this one up…". On close, `ClaimRewards` + payout in one call. Log sequence: `Intercepted …` → `Paid …`. Next click: Done → depart.

3. **Non-Courier via mission board:** complete objectives, click "Complete" at the mission board. Expected: completion notification fires, no rewards paid, log `Intercepted …`. Visit broker. Dialogue: "Good work out there…". Log: `Paid …`. Next click: Done → depart.

4. **Broker pinning:** accept a mission. Advance in-game time past daily bar refresh. Re-enter the bar. Broker is still present (log confirms via absence of "evicted" entry for them). New broker may or may not spawn alongside (idempotency prevents it if ours is still in the registry).

5. **Save/load guard:** save mid-`ReadyToCollect`, reload. Verify: broker is gone (in-memory registry cleared), `PendingRewards` is lost. README documents this as known limitation.

## Files touched

| File | Change |
|---|---|
| `VGAnima/Pitch/BrokerState.cs` | add `ReadyToCollect` enum value |
| `VGAnima/Pitch/StaticPitchProvider.cs` | handle new state + revise ReadyToClaim text |
| `VGAnima.Tests/Pitch/StaticPitchProviderTests.cs` | extend theory + add ReadyToCollect fact |
| `VGAnima/Cache/ConversionRecord.cs` | add `PendingRewards` property |
| `VGAnima/Cache/ConversionRegistry.cs` | add `FindByValue` method |
| `VGAnima.Tests/Cache/ConversionRegistryTests.cs` | add FindByValue tests |
| `VGAnima/Patches/BrokerStateDetector.cs` | add `ReadyToCollect` branch |
| `VGAnima/Patches/MissionRewardsPatches.cs` | **new** — `Mission.ClaimRewards` prefix |
| `VGAnima/Patches/SalesmanPatches.cs` | add `ClaimAndPay` + `PayPending`, wire into state switch |
| `VGAnima/Patches/BarRefreshPatches.cs` | pinning + `IsActive` helper |
| `VGAnima/Plugin.cs` | `PatchAll(MissionRewardsPatches)` |
| `README.md` | save-timing caveat |

## Acceptance

- `make build` — 0 warnings, 0 errors.
- `make test` — all existing tests + new `FindByValue` + new pitch tests pass.
- Manual E2E cases 1–5 (above) succeed in-game.
- VGTTS voices the new `ReadyToCollect` lines without live-synth lag (pre-warmed at injection by the existing Phase A warming loop, which iterates every state).
