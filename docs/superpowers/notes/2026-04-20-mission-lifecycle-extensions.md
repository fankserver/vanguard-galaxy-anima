# Mission Lifecycle Extensions — Design Notes

Status: **exploration / not yet a spec**. Captured from the 2026-04-20 brainstorm. Revisit before starting any of this work.

## Motivation

Today's flow is transactional: the broker pitches, the player picks up the mission from the board, the game pays out automatically on completion, the broker is unaware. Turning that into a narrative arc where the broker stays relevant through the whole mission lifecycle would make the feature feel alive.

## Proposed 8-step flow (user's sketch)

1. Player talks to broker → pitch
2. Broker posts mission to the station board
3. Mission listed on board **without rewards** (rewards deferred to turn-in with broker)
4. Player talks to broker again → "still on the board if you're interested"
5. Player accepts mission from the board
6. Player talks to broker → "did you finish it?"
7. Player completes mission — **no reward yet**
8. Player talks to broker → "thanks, here's your pay (and maybe a follow-up)"

## Phasing

### Phase A — dialogue state (cheap, no persistence)

Broker's pitch lines change based on *game-observable* state — no new storage needed:

| Game state | Broker line |
|---|---|
| Mission on `station.missionBoard.availableMissions` (not yet accepted) | "it's still on the board" |
| Mission in `GamePlayer.current.missions` and `CanClaimRewards()==false` | "did you finish it?" |
| `CanClaimRewards()==true` | "ready to report in?" |
| Mission neither on board nor in player missions | "thanks captain, safe travels" |

All states derivable from game state. Nothing to save. Delivers steps 4 and 6 of the flow immediately.

**Effort:** small (afternoon). Ship this regardless of whether B/C follow.

### Phase B — deferred rewards (moderate, one new concept)

On generation, strip `mission.rewards` into our `ConversionRecord` and clear the game's list. The game then pays out nothing on mission completion. When the player talks to the broker while `CanClaimRewards()==true`, we manually pay out via `GamePlayer.AddCredits` / `AddExperience` / etc.

**Hard blocker: NPC lifecycle.** Brokers roll off on daily `Bar.CheckUpdatePatrons` refresh. If the mission takes several in-game days to complete, the quest-giver is gone and the player can't turn in — effectively stole their reward. Mitigations:

- **Pin broker while mission active.** Bypass our eviction for brokers whose associated mission is in `GamePlayer.current.missions`. Keep them in `availablePatrons` past normal rollover.
  - Pro: narratively correct, mission-specific
  - Con: bar gets one extra patron per outstanding mission (probably fine in practice)
- **Any broker at this station can accept turn-in.** Drop the NPC-identity constraint.
  - Pro: no lifecycle concern
  - Con: less immersive — "someone I've never met is paying me"
- **Station-level turn-in.** Any patron at any station at the source station accepts.
  - Pro: bulletproof
  - Con: least immersive; breaks the narrative

**Recommendation:** pin-while-active. Bar-cap concern is minor (one pinned patron per open mission).

**Effort:** medium. Requires understanding how the game's mission system triggers reward payout (whether `ClaimRewards()` runs on its own or via the mission-board UI), and our manual reward-award API.

### Phase C — follow-up mission chains (heavy, needs persistence)

Chain of missions — after reward, broker offers the next link. Needs to track chain position per save.

**Save persistence options:**

1. **`BepInEx/cache/VGAnima/<save-guid>.json`** — one file per save. Read on `GameplayManager.Start`, write on game save. Need to obtain the save's ID (`GamePlayer.saveName` or similar — requires RE).
   - Pro: simple to implement, invisible to vanilla
   - Con: not bundled into the `.sav` file — user copying their `.sav` to another machine leaves our json behind
2. **Piggy-back on `DataToJson` / `DataFromJson`** — Harmony-patch the game's save path so our state travels with the `.sav`.
   - Pro: cleaner for users
   - Con: invasive; fragile to save-format changes
3. **Store on the `Mission` itself** — abuse `Mission.storyId` (a free-form string) to encode chain id + step index.
   - Pro: zero new persistence, travels with `.sav` automatically
   - Con: abuses an unrelated field; only holds small scalar state

**Recommendation:** start with option 1 for MVP. Migrate to option 2 if save portability becomes a real user need.

## Technical challenges summary

1. **Suppressing vanilla reward payout.** Need to strip `mission.rewards` after generation and ensure `ClaimRewards()` is a no-op. May require hooking the mission-board UI's complete button if rewards fire from there.
2. **Deferred reward API.** Need to identify the exact game APIs for granting credits / XP / items / reputation from a plugin (vanilla rewards have a polymorphic `MissionReward` base class — we'd call `reward.Grant()` or similar manually).
3. **NPC lifecycle.** Bar rollover vs. outstanding missions. Pin while active is the cleanest answer.
4. **State tracking.** Beyond in-memory `ConversionRegistry`, persistent state keyed by save id.
5. **Follow-up chain design.** Which generators chain into which? Random? Author-defined? LLM-chosen (v0.2+)?

## Recommended next action

**Implement Phase A** next. It's quick, adds real immersion, validates the dialogue-state pattern, and doesn't commit us to any of the hard problems. Once A is shipped and felt out in-game, we'll know whether B's NPC-pinning trade-off feels right. C is a v0.3+ conversation.

## Out-of-scope reminders

- Don't design the LLM pitch-variation here (that's its own design doc in v0.2).
- Don't couple chain design to a specific mission generator — the infrastructure should be generator-agnostic.
- Revisit this doc before any B/C work — the technical landscape may have changed (e.g., game patches, new BepInEx APIs).
