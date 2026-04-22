# NPC Interaction Ideas

Backlog of ways to make LLM-authored bar brokers feel like inhabitants
of the galaxy instead of quest-triggering vending machines. Sister
document to `special-quest-ideas.md`: that one covers rare mission
*variants*, this one covers the *interaction layer* around any mission.

None of this is implemented. Treat it as a menu we pick from when
designing future schema versions (`vganima/mission/v2`, v3…). Captured
from a joint brainstorm between the project owner, Claude (Sonnet/Opus),
and Gemma-4-26b running locally.

---

## Hard constraints

Before any idea in this doc, these are non-negotiable:

1. **Ahead-of-time authoring.** The LLM call fires at bar refresh, before
   the player walks up. Mid-interaction LLM calls are immersion-breaking
   (~15 s of waiting is hostile to UX) and streaming does not help because
   we need the full validated JSON before spawning POIs / committing
   objectives.
2. **One LLM call per broker.** We can grow the output (more branches,
   more signals) but not issue a second call per broker interaction.
3. **Mission mechanics fixed at author-time.** Objectives, rewards, POI
   spawns are all decided at author-time — the interaction layer can
   surface or reshape them, but not invent new ones.
4. **Modal UI via `AlertPopup`.** Vanilla's `AlertPopup.ShowQuery` gives
   us Yes / No / optional-third-button popups that pause the game. For
   more than 3 branches we'd reuse vanilla's `createDialogue` 3-branch
   pattern or build a new pane.

All constraints documented in `docs/vanilla-reference.md`.

---

## The core design lever

**Don't add LLM calls — add signals to the existing one.**

The LLM already authors once per broker with ~4.5 KB of context. Every
new interaction pattern in this doc lives in one of two places:

- **Expand the output schema** — author more branches / fields in the
  same response (negotiate replies, decline responses, backstory,
  stage directions, hidden truths).
- **Expand the input context** — feed new signals so the same
  single call can produce more reactive content (station condition,
  other brokers at the same station, time-since-spawn, player
  notoriety tier, illegal cargo flags).

Most ideas below are orthogonal combinations of those two levers.

### Context signals worth adding

| Signal | Enables | Cost |
|---|---|---|
| `station_condition` (war-torn / quarantine / luxury / famine / boom) | Atmospheric prose mirroring | 1 field, 1 context read |
| `last_n_broker_pitches` at this station | Rumor triangulation, cross-broker references | Bar-refresh cache, ~1 KB added context |
| `broker_age_refreshes` | Information-decay phrasing ("I *think* it was…") | Registry timestamp we already have |
| `illegal_cargo_flags` | Blackmail / shakedown branch | Cargo scan on injection |
| `notoriety_tier` (unknown / known / notorious / feared) | Reputation-mirrored prose register | Derived from existing rep+bounty values |
| `is_lying` + `hidden_truth` | Bait-and-switch missions | Schema addition + skill-check |
| `broker_mood` (desperate / cocky / paranoid / drunk) | Tonal variants | Single enum field |
| `broker_employer_hidden` | Broker lies about who hired them | Schema addition |
| `stage_directions: true` | Physical presence in prose | Prompt rule only |
| `trust_level` with this broker (0..3) | Escalating mission scope across encounters | Persistence field per storyId |
| `prior_missions_from_broker` | Broker memory / callback lines | Registry lookup |

---

## Idea menu

Organized by how architecturally expensive each one is, cheapest first.

### Tier 1 — Prompt-only (no schema, no persistence, no UI)

All authored in the existing single call, no new fields.

| # | Name | Description | Complexity | Value |
|---|---|---|---|---|
| 1 | **Stage-direction prose** | Instruct LLM to drop bracketed directions in pitch: `*[Spits on the floor]* *[Glances at the door]* *[Wipes blood from his lip]*`. Physical presence without animation work. | tiny | high |
| 2 | **Interrupted monologue** | Prompt rule: pitch can be written as mid-sentence or cut off by a bar patron. Breaks the "broker monologues at you" rhythm. | tiny | medium |
| 3 | **Tonal variants (drunk / scared / cocky / mournful)** | Pass a `broker_mood` enum into the prompt. Same mechanics, wildly different feel. | tiny | high |
| 4 | **Atmospheric mirroring by station condition** | `station_condition` field shifts linguistic register — clipped/urgent in war zones, verbose/flowery in luxury. Applies to every broker at that station. | small | high |
| 5 | **Reputation-mirrored prose style** | Broker phrasing shifts with player notoriety. High-notoriety = subservient; low = contempt, dismissive. Different *how*, not different *what*. | small | high |
| 6 | **Rumor triangulation** | Pass last 3 broker pitches at this station into the next call. Broker can reference them: "Don't trust what Keril told you, he's a liar." Emergent same-station coherence. | small | high |
| 7 | **Gossip line (free)** | Authored one-liner of ambient lore unrelated to the job. "Heard the mining strike in Sarus is still on." No mechanics — pure world-building. | tiny | medium |
| 8 | **Broker backstory reveal** | "Who are you?" button unlocks a short LLM-written personal history. Throwaway flavor; makes the world feel lived-in. | tiny | medium |

### Tier 2 — Schema expansion (more authored branches, same single call)

Grow the response schema to support richer interaction trees. Still one
call, just bigger output.

| # | Name | Description | Complexity | Value |
|---|---|---|---|---|
| 9 | **Accept / Negotiate / Decline (3-button)** | Negotiate swaps to a pre-computed alt reward mix (e.g. +20 % credits / −40 % rep, or harder combat for double payout). LLM writes the haggle comeback per variant. | medium | high |
| 10 | **"More details" branch** | Risk disclosure: hostile faction size, distance, why it's dangerous. Authored alongside pitch. Reduces information asymmetry. | small | medium |
| 11 | **Decline-consequence tones** | LLM writes separate "polite decline" vs "rude decline" responses. Rude path costs a bit of rep with broker's faction; polite is free. | small | medium |
| 12 | **Veracity flag + bait-and-switch** | Hidden `is_lying: true` + `hidden_truth` field the player never sees at pitch. An Intel-stat / reputation check reveals the truth before accept. Mission objective stays as authored; only the *framing* flips. | medium | high |
| 13 | **"Threaten" as third button** | Reframes Negotiate as intimidation. Uses a pre-authored "fearful" dialogue branch. Broker spills extra info / bumps reward under duress. | medium | medium |
| 14 | **Cryptic hook as default** | Default broker output is an observation, not a pitch: "I saw a Marauder dropship limping toward Sarus." Clicking "Ask for details" reveals the actual mission. Makes every broker feel embedded in a world. | medium | high |
| 15 | **Broker asks YOUR motive** | Three pre-authored motive options (greed / loyalty / glory). Motive colors the reward mix and the broker's tone in subsequent lines. | small | medium |
| 16 | **Small talk / "buy me a drink"** | 200 cr tip unlocks friendlier dialogue. Humans establish rapport before business. Skipping it makes the broker terse. | small | medium |

### Tier 3 — Persistence-driven (tracks state across encounters)

Needs sidecar schema extensions. Sits on top of Tier 1 + 2.

| # | Name | Description | Complexity | Value |
|---|---|---|---|---|
| 17 | **Trust ladder per broker** | `trust_level: 0..3` field in persistence. First mission is small; completing it unlocks bigger jobs on next encounter. Prompt threads the tier. | medium | high |
| 18 | **Broker memory** | Context includes "this broker already gave you missions X, Y." LLM references past events organically ("how's the ship after that Corsair job?"). | medium | high |
| 19 | **Grudges for rudeness** | Rude-decline third-button costs rep + broker skips this player for N game-hours. Makes declining a real choice. | small | medium |
| 20 | **Retirement arcs** | After 5+ missions from one broker, their "farewell" pitch fires and they're permanently removed from the station's pool. Players feel the broker had a life. | medium | high |
| 21 | **Broker memory of failed missions** | "You let my shipment get hit last time, captain. Why should I trust you again?" Authored dialogue branch fires only when prior-failure flag is set. | small | medium |
| 22 | **Information decay** | Track `broker_age_refreshes`. If a broker has been sitting unspoken-to for many refreshes, LLM phrases info with uncertainty ("I *think* the shipment was heading North, but that was days ago…"). Missions rot. | small | medium |
| 23 | **Shared player scores** | `underworld_score` / `honorable_score` aggregate from accepted mission types. High underworld → shady brokers approach you more; high honorable → legit faction-loyalist jobs. | medium | high |
| 24 | **Public shaming** | Declining too many from one faction changes how OTHER brokers of that faction greet you. Context field: `faction_standing_recent_declines`. | medium | medium |

### Tier 4 — Systemic / emergent

Multi-broker interactions, cross-station continuity. Biggest lift,
deepest payoff.

| # | Name | Description | Complexity | Value |
|---|---|---|---|---|
| 25 | **Broker referrals** | "This job isn't for me, but my cousin at Outrider 3A could use you." Pre-authors a hook at another station. Cross-station narrative pull. | large | high |
| 26 | **Multi-broker rivalries** | Two brokers at the same station offer opposing missions — pick one, the other shuts you out. LLM authors them as a pair in one context pass. | large | high |
| 27 | **Cross-station characters** | Top-quality brokers travel. See Robert Miyama at Roost XIII this week, Spire II next week. Persistence already tracks storyId + seed; needs migration logic. | large | high |
| 28 | **Broker archetypes as prompt families** | Four or five prompt variants: Desperate (high pay, low quality), Shady (illegal / rep-hit), Established (stable, low-risk), Networker (opens referrals), Informant (sells intel instead of missions). Structural variety beats endless text. | large | high |
| 29 | **Information brokers** | Some "brokers" don't sell missions — they sell intel. 5000 cr reveals a high-yield asteroid, a pirate fleet's next move, or a faction's weakness. Spawns a POI hint. | medium | medium |
| 30 | **Fragmented clue / dead-drop chain** | Broker A's mission is incomplete — coordinates without objective, or target without location. Broker B at another station has the rest. Explicit LLM→LLM hand-off authored with shared `chain_id`. | huge | high |
| 31 | **Multi-captain competition** | "There are three other captains on this contract" — visible NPC-captain shadow timer. If you're slow, NPC completes first, partial pay. Creates real urgency. | large | medium |
| 32 | **Ambush / betrayal missions** | Rare: broker's mission is a setup (hostile ambush at the POI). Survive it → rep bonus with the betrayer's enemies + broker gone from station. | medium | high |
| 33 | **Reputation laundering** | Broker offers to "clean" a negative rep hit for credits. Covers tracks after a betrayal or shady job. | medium | medium |

### Tier 5 — Speculative / uncanny

Ideas that push LLM authorship into territory games rarely attempt.
Higher complexity, unclear value until prototyped.

| # | Name | Description | Complexity | Value |
|---|---|---|---|---|
| 34 | **Gaslight pitches** | Broker states things that contradict known game facts ("Station X is gone" when it's not). Tests player trust; or subtle manipulation toward a specific sector. | medium | speculative |
| 35 | **Blackmail / shakedown branches** | If player has contraband in cargo, broker's pitch includes a shakedown path: pay X or I'll report you. Uses existing cargo state as dialogue trigger. | medium | medium |
| 36 | **Ego check / ship-tier gating** | Broker refuses to even present high-tier missions if your ship class is too low. "Come back when you've got something with real guns." Progression feels earned. | small | medium |
| 37 | **Broker has conditions beyond the job** | "Don't tell anyone you got this from me." Breaking it (mentioned to another broker) costs you access to the network. Persistence flag. | medium | speculative |
| 38 | **Social debt economy** | Instead of credits, a mission is paid with a "favor" token usable at another broker. Non-monetary currency. | large | speculative |
| 39 | **Broker emotional arcs** | Across successive missions, broker's tone shifts from fearful → neutral → respectful. LLM picks tone from `trust_level + last_outcome`. | medium | medium |
| 40 | **Coerced informant** | Broker is visibly being forced to give you this mission (by a third party). The mission text is flat/scared. Player can dig via the "Threaten" branch. | medium | speculative |
| 41 | **Inner-voice commentary (ship AI)** | When LLM flags a broker as lying or hiding something, the player's ship AI whispers a warning: "He's not telling us everything, cap'n." Second layer of agency that doesn't require player deduction. Draws from Disco Elysium / Cyberpunk. | medium | high |
| 42 | **Broker calls you back** | Decline a mission → 30 game-minutes later the broker contacts you via popup: "Captain, reconsidered? I can bump the payout." Time-gated second chance. | medium | medium |
| 43 | **Broker asks to join your crew** | Mid-trust ladder, broker pitches themselves: "I'm done with this dump. Got a seat?" Converts NPC to crew via vanilla's hire path. | large | speculative |

---

## Inspirations

- **Disco Elysium** — skill-gated dialogue options as voices in the player's head. Inner-voice commentary (idea 41) is the direct steal.
- **Witcher 3** — Axii (persuasion) branches. Pre-scripted persuasion options gated by stats / titles. Maps to Tier 2 accept-branch variants.
- **Mass Effect / KOTOR** — paragon/renegade dialogue wheel. Tonal choices (idea 15: player motive) steal from this.
- **EVE Online** — agent loyalty + quality system. Maps to Tier 3 trust ladders (idea 17).
- **Mount & Blade** — tavern scenes with persistent personas. Matches retirement arcs (20) + cross-station characters (27).
- **Cyberpunk 2077** — fixers as recurring voice characters with context-aware job pitches. Maps to archetypes (28) + broker memory (18).
- **Red Dead 2** — NPCs remember small interactions; reputation is local. Public shaming (24) + honorable/underworld score (23) trace back here.
- **Planescape: Torment** — stat checks in dialogue. Maps to veracity-reveal (12) + ship-tier gating (36).
- **Deus Ex: HR** — CASIE "social battle" augment reveals NPC emotional state. Inner-voice commentary (41) is the analog.
- **Baldur's Gate 3** — every NPC deeply branching; failed rolls have real consequences. Matches rude-decline / grudge systems (11 + 19).

---

## Pitfalls and tradeoffs

1. **Token inflation.** Every new authored branch grows LLM response. 2× tokens ≈ 2× latency on most backends. Test before committing to schema growth. Current broker pitch runs ~13 s at 1865 chars output — doubling that pushes past the 30 s config default.
2. **Decision paralysis.** 3 buttons is the natural cap on `AlertPopup`. Anything richer needs vanilla's 3-branch `createDialogue` path or a custom pane. Tier 2+ ideas with many branches need UI work.
3. **Narrative consistency.** Authoring 6 branches in one call risks tone mismatch — warm pitch with cold grudge line, etc. Prompt needs explicit coherence rules across branches, same way the current pitch/payout coherence rule works.
4. **Negotiate economy can be gamed.** If haggling always succeeds, players always haggle. Needs stochastic failure or a rep/time cost. Counter-offer must have a real downside.
5. **Persistence bloat.** Trust ladders + broker memory + cross-station travelers grow the sidecar linearly with player-hours. Manageable, but needs orphan-purge extensions to handle the new shapes.
6. **Feature creep.** Every idea here is weeks of work. Plugin's core is tight and shipped; adding five interaction layers turns it into a different project. Pick one line and commit.
7. **Uncanny-valley LLM consistency.** The LLM will occasionally author contradictions the player notices ("broker is desperate but offers tiny reward"). Rare, but memorable when it happens. Validator should catch gross mismatches.
8. **Prompt complexity explosion.** Every new context signal and output field grows the system prompt. Already ~7.7 KB; doubling it eats the context budget and risks the LLM ignoring later instructions. Prune ruthlessly.

---

## Recommended first steps

If we pick a next direction from this menu, the three cheapest-with-highest-payoff picks are Tier 1:

1. **Stage-direction prose (idea 1)** — one prompt rule, massive atmosphere gain.
2. **Atmospheric mirroring by station condition (idea 4)** — one context field, applies everywhere.
3. **Rumor triangulation (idea 6)** — feed last-N-pitches into context, get emergent same-station coherence.

None of these require new UI, new persistence schema, or new LLM calls.
They're pure prompt / schema work on the existing dispatch path.

Natural *second* step, if those land well, is **Tier 2 / idea 9 —
Accept / Negotiate / Decline (3-button)**. That's where the `AlertPopup`
research finally gets used, and where broker interactions start to feel
like negotiations instead of vending-machine transactions.

Tier 4 (broker archetypes, cross-station characters, dead-drop chains)
is where the project stops being "bar brokers with LLM missions" and
becomes "a galaxy of persistent characters." That's the ambitious end
state — load-bearing only if we decide this mod is worth years of
iteration.
