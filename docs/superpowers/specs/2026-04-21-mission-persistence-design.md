# Mission Persistence — Design Spec

**Status:** Design approved, pending implementation plan
**Author:** fank + Claude
**Date:** 2026-04-21
**Depends on:** `2026-04-21-llm-mission-gen-v2-mission.md` (v2-mission epic, shipped)

---

## 1. Goal

Make LLM-authored missions and their brokers survive across game sessions. Today, closing and reopening the game loses all v2-mission state — brokers revert to regular patrons, accepted missions crash vanilla's deserializer (`KeyNotFoundException` from `Mission.FromJson`), and the LLM inference cost of every broker is thrown away.

After this change:
- Accepted missions survive restart, load cleanly, and stay in vanilla's mission list with their original storyId.
- Brokers tied to accepted missions stay pinned to their bar — across daily rotation and restart — until the mission resolves.
- Inference results for unaccepted brokers are cached on disk so revisits don't re-invoke the LLM.
- Multiple save slots each carry their own story state in parallel (no cross-contamination).
- The game never crashes because our sidecar is missing or corrupt.

## 2. Architecture

Each vanilla save file gets a pair-named sidecar: `<vanilla-save-path>.vganima.json`. The sidecar holds the data vanilla's own save format can't express — LLM-authored mission blocks, broker dialogue trees, and broker → station bindings.

Lifecycle is driven by Harmony hooks on vanilla's own save-write and save-load methods. An in-memory registry is the source of truth during a session; writes happen when vanilla saves, reads happen when vanilla loads. Save-slot isolation falls out automatically — our hooks key off whatever save path vanilla is currently operating on.

## 3. Scope

**In scope (persisted):**
- Accepted missions — `storyId`, full `LlmMissionBlock`, and the broker's `ConversionRecord`.
- Offered missions — same shape; caches the LLM inference so revisits don't re-call the LLM.
- Accepted-broker bar pinning — broker stays in the same bar slot through rotations and restarts until the mission resolves.

**Out of scope for v1:**
- TTL / LRU / wall-clock expiry policies for unaccepted brokers. No auto-pruning beyond "mission resolved" and "dead save file". Timestamps are recorded on disk from day one so a policy can be added later without a migration.
- Cross-plugin compatibility (other BepInEx plugins sharing this storage).
- Running a VGAnima save without VGAnima loaded (player's problem — documented in README).

## 4. Storage layout

**Sidecar path:** `<vanilla-save-path>.vganima.json`, next to the vanilla save file on disk. Pair-naming means a copy/move/delete of the vanilla save pairs cleanly with its sidecar when the user takes either action manually.

**Schema (version 1):**

```json
{
  "version": 1,
  "entries": [
    {
      "storyId": "vganima_llm_<station>_<broker>_<nonce>",
      "state": "offered",
      "missionBlock": { /* validated LlmMissionBlock */ },
      "broker": {
        "seed": "vganima-broker-abc-0",
        "stationId": "<station-guid>",
        "displayName": "Shawn Jenkins",
        "isMale": true,
        "story": { /* LlmStory: hook, pitch, rejection, reward, character */ }
      },
      "timestamps": {
        "createdGameSeconds": 18420.5,
        "createdRealUtc": "2026-04-21T10:15:30Z",
        "lastSeenGameSeconds": 19800.0,
        "lastSeenRealUtc": "2026-04-21T10:38:00Z"
      }
    }
  ]
}
```

**Field notes:**
- `storyId` — vanilla's registry key. Stable; never regenerated after the broker is first inferred. This is what `Mission.FromJson` will look up on load.
- `state` — `"offered"` or `"accepted"`. Resolved entries are purged (no terminal state on disk).
- `missionBlock` — the same validated `LlmMissionBlock` we already assemble today in `MissionFactoryFromJson`.
- `broker.seed` — matches vanilla's `BarPatron.seed`. Vanilla persists this field; we use it to re-bind on load.
- `broker.stationId` — stable station reference so pinning targets the right bar.
- `broker.story` — full `LlmStory` so the broker's dialogue doesn't trigger a second LLM call on restore.
- `timestamps` — recorded in both in-game seconds and real UTC. Not acted on in v1; present for future pruning policies.

**Atomic writes:** every write goes to `<sidecar>.tmp` first and is then renamed. A crash mid-write leaves the previous sidecar intact.

**Version field:** top-level `version` is bumped on any breaking schema change. Unknown versions are quarantined (see §7) rather than interpreted optimistically.

## 5. Write lifecycle

In-memory registry is the source of truth during a session. Writes to disk are driven by Harmony hooks on vanilla's save-write method.

**Hook:** **postfix** on vanilla save-write.
- Postfix (not prefix) so our sidecar only commits after vanilla's save succeeds. A failed vanilla save means we don't leave a sidecar referencing a save that never got written.
- Extract the target save path from vanilla's method arguments; derive `<save>.vganima.json`.
- Serialize the in-memory registry to JSON, atomic-write to the sidecar.

**In-memory-only mutations (no flush):**
- Broker inferred → add entry with `state=offered`.
- Mission accepted → mutate entry to `state=accepted`.
- Mission resolved (completed / failed / archived) → remove entry.
- Bar refresh touches a persisted broker → bump `lastSeen` timestamps.

**Safety net:** `OnDestroy` / `ApplicationQuit` hook flushes to the most-recently-active save path *if and only if* the session has a known active slot (i.e. the player saved or loaded at least once during the session). Matches vanilla's "quit without save = lose changes" semantics.

**Crash semantics:** identical to vanilla. A crash mid-session loses everything since the last save, for both our sidecar and vanilla's save. No special recovery, no partial state to reconcile.

## 6. Read lifecycle (rehydration)

**Hook:** **prefix** on vanilla save-load — must run strictly before vanilla processes any persisted mission or broker data.

Steps, in order:

1. **Extract source save path** from vanilla's method arguments. Derive sidecar path.
2. **Clear current in-memory state** — drop any entries left over from a previous session's save, unregister any previously-registered VGAnima factories from `StoryMission.allMissions`. Prevents cross-slot leakage when the player loads a different save mid-session.
3. **Read and parse sidecar.** On missing / corrupt / unsupported-version: see §7. Otherwise populate in-memory registry.
4. **Register mission factories.** For each entry, register a factory under the stored `storyId` in `StoryMission.allMissions`. The factory body is today's `MissionFactoryFromJson.Build` invoked with the persisted `missionBlock`.
5. **Return to vanilla** — vanilla's own load logic proceeds. It deserializes the mission list, calls `Mission.FromJson` per storyId, our factories fire, rehydration completes.

**Missing-entry safety net (separate Harmony patch, installed once in `Plugin.Awake`):** vanilla's mission lookup path — `StoryMission.Get(player, storyId)` or equivalent, to be confirmed during scout — is patched so a lookup miss against a `vganima_llm_*` storyId returns a placeholder Mission rather than throwing `KeyNotFoundException`. This catches the case where vanilla's save references an authored storyId our sidecar doesn't have (sidecar missing, corrupted, quarantined, or the specific entry was dropped by orphan purge on a previous load). See §7 for the placeholder Mission contract.

**Broker rehydration** happens later, during the first `BarRefreshPatches` cycle after load. Existing logic already detects saved brokers via the `vganima-broker-` seed prefix and currently re-invokes the LLM with a legacy fallback. Rewrite: on seed match, look up the entry in the in-memory registry, restore the `ConversionRecord` directly from `broker.story` / `broker.displayName` / etc. No LLM call. On lookup miss (seed matches prefix but no entry): fall back to regular patron; no mission attached.

**Bar pinning** extends current `BarRefreshPatches` logic. Today, pinning keys off "storyId is active in player mission list" — which only covers accepted missions. Extend to "entry exists in in-memory registry" — covers both offered and accepted. This is what gives unaccepted brokers their across-rotation / across-session persistence.

## 7. Failure modes

Every failure logs a warning and keeps the game playable. Never crash vanilla because our sidecar is unhappy.

| Failure | Trigger | Behavior |
|---|---|---|
| Sidecar missing | Vanilla save exists but no paired sidecar (deleted externally, copied without sidecar, first load after installing plugin on existing save) | Load hook completes with empty registry. Placeholder factory catches any `vganima_llm_*` storyId vanilla tries to resolve; affected missions archive cleanly. |
| Sidecar corrupted | JSON parse error, truncated write, mangled encoding | Rename to `<save>.vganima.corrupt.<timestamp>.json` (quarantine). Continue with empty registry. Placeholder-factory path same as missing. |
| Version too new | Sidecar `version` > what this plugin build supports | Quarantine same as corruption. Log directing user to check for a plugin update. |
| Entry missing required fields | Partial write, manual edit gone wrong | Skip that entry, load the rest. Log which entry and why. |
| Duplicate `storyId` in entries | Defensive against bugs | Last-wins, warn. |
| Broker seed matches no patron after rehydration | Bar rotation dropped the patron in vanilla's save, or vanilla didn't persist it | If `state=accepted`: mission still works (it's in the player's list), just no visible broker. Log and proceed. If `state=offered`: purge on next save. |
| Plugin uninstalled, vanilla save still references VGAnima storyIds | Player ran with plugin, saved, then loaded without plugin | Out of scope — we can't fix this from inside the plugin. Document in README. |

**Placeholder Mission** is the load-bearing safety net. It is returned by a Harmony patch on vanilla's mission-lookup path (see §6, "Missing-entry safety net") whenever a `vganima_llm_*` storyId is requested but absent from our in-memory registry. Contract:
- Must be a valid `Mission` instance that vanilla can serialize/deserialize without crashing.
- Should be marked archivable (or auto-archived on first tick) so it doesn't pollute the player's active list indefinitely.
- Logs a one-line warning on construction identifying the missing storyId.

Exact archive mechanism settled during implementation; goal is "game doesn't crash, player sees a cleanup message in the log".

## 8. Cleanup rules

**Active (runtime):**
- Mission resolves → entry removed in-memory → flushed to sidecar on next vanilla save.
- Offered broker's bar rotates naturally and no re-spawn happens → entry lingers in-memory until load-time orphan check catches it (we don't actively prune offered entries whose patrons vanish mid-session).

**Load-time orphan purge (runs inside the load hook, after registry is populated):**
- Entries with `state=accepted` whose `storyId` isn't in vanilla's active or archived mission lists → drop. The mission is gone from the player's timeline; the sidecar entry is stale.
- Entries with `state=offered` whose `broker.seed` doesn't appear in any bar after patron rehydration → drop. The broker is gone; the cached inference has nowhere to live.

**Startup sweep (runs in `Plugin.Awake`):**
- Scan the save directory for `*.vganima.json` files whose base save file no longer exists → delete. Prevents accumulation when users delete saves outside the game.

**Not in v1 (intentional):**
- No TTL on unaccepted brokers.
- No per-station LRU cap.
- No sidecar size limit.
- No wall-clock age policy.

Timestamps are written from day one, so adding any policy above later is a config switch, not a migration.

## 9. Prerequisites (implementation plan will tackle these first)

1. **Locate vanilla save-write method.** Scout must find the top-level method vanilla calls to write a save, its signature, how the target path is exposed, and whether it can be called reentrantly. Without this, we can't place the save postfix.
2. **Locate vanilla save-load method.** Same exercise for the read side. Critically, find a hook point that runs strictly *before* vanilla deserializes the mission list or calls `Mission.FromJson`, so our factory registration is complete when vanilla needs it.
3. **Determine vanilla's `Mission` deserialization semantics.** Two scenarios have different implications for the factory body:
   - **Factory re-called on load:** our factory must be idempotent and avoid respawning POIs (`station.system.AddCombat`) that are already in vanilla's system state. Factory becomes load-aware.
   - **Mission restored from its own serialized fields, factory not re-called:** trivial; registration is insurance only. No factory changes needed.
4. **Verify atomicity.** Confirm that the save-write method isn't called from multiple threads and that a postfix can safely write a sibling file during or immediately after vanilla's own disk write.

## 10. Testing approach (for the implementation plan)

Unit-testable components:
- Sidecar reader/writer: JSON roundtrip on `LlmMissionBlock` + `LlmStory` + timestamps. Schema version handling (current / too-new / missing). Atomic write pattern (verify `.tmp` cleanup).
- Orphan detector: given a sidecar and a set of vanilla mission lists + patron seeds, produce the expected purge set.
- Placeholder factory: produces a Mission that vanilla can archive (at minimum: doesn't throw on construction).

Integration-testable components:
- Save → load cycle in a harness that mocks vanilla's save path.
- Load ordering: assert factory registration completes before any `Mission.FromJson`-like call fires.

Manual E2E (live game, same pattern as v2-mission §14):
- Accept mission → save → quit → launch → load → mission present in list, broker pinned at bar, reward dialog works.
- Offer mission → close game without accepting → relaunch → load → broker still pinned with same pitch.
- Multiple save slots → different brokers in each → switch between them without cross-contamination.
- Delete sidecar externally → load → game doesn't crash, missions archive with a warning.
- Corrupt sidecar (manual edit) → load → file quarantined, game doesn't crash.

## 11. Non-goals / future work

- **Expiry policies** (TTL, LRU, station-capped) — deliberately deferred. Timestamps are on disk for when we want them.
- **Broker activity tracking** (refinery use, workshop use, trade history) — separate feature, different data model, belongs with the "activity history" roadmap item.
- **Cross-save broker sharing** — each save is isolated.
- **Export / share brokers between players** — out of scope.
- **Prompt caching** — orthogonal; tracked as its own roadmap item.
