# System-visit recording from travel events (0.4.0)

Requires VGModAPI 0.1.9–0.1.x. The API's travel group is **experimental and opt-in**: system visits are recorded only while `[Travel] Enabled = true` in `BepInEx/config/vgmodapi.cfg` and the `native-travel` capability reports available. Anima consumes `ModApi.Travel` public contracts only (`ITravelEvents`, `TravelTransition`, `TravelLocation`, `TravelTransitionKind`, `TravelMode`). The former `TravelManager.JumpToSystem` Harmony prefix is removed; there is no travel-hook fallback and no silent re-installation on fault.

## What counts as a visit

`SystemVisitObserver` increments a system's tally only from a witnessed `Arrived` fact whose mode is `JumpGate` or `Wormhole`, and always at `ActualLocation` — never `RequestedDestination`. A tutorial-exit rewrite is therefore recorded where the ship actually ended up, not where the gate nominally pointed.

- `InitialPlacement` and `RecoveredPlacement` seed the current-system identity and count nothing. Placement in the system you are already latched to changes no tally.
- `Requested`, `Departed`, `Cancelled` and `RouteCompleted` are never arrivals. A cancelled leg records nothing, before or after departure.
- In-system POI arrivals (`TravelMode.InSystem`) confirm where the ship is and never count a cross-system visit.
- A visit is counted when the arrival's system differs from the latched current system. On the route A → B → A that means two visits to A only when the first A was itself a witnessed arrival; when A was merely the placement system at load, the same route counts one visit to A (the return leg) and one to B.
- `TravelTransition.GameSeconds` is the authoritative visit time. No player or global clock is consulted.
- System labels come from the public `TravelLocation.SystemName` snapshot. When it is null (unavailable) the previously stored label is preserved — first sightings store an empty label rather than forcing a lazy vanilla name lookup.

## Ownership and ordering

Facts whose session is not the provider's current travel session cannot mutate the registry, including evidence delivered during dispatch of another fact. A replaced session resets the latch, the leg index and the sequence watermark; a replayed or out-of-order sequence is dropped, and one witnessed leg (operation id) can count at most one visit. Repeated delivery of the same arrival therefore counts once.

Slot load and the API's own placement event are not ordered against each other. The load prefix takes ownership of the new slot in one synchronous main-thread block: it clears the registry, resets visit tracking, and only then reads the sidecar. The reset therefore precedes any IO that can fail, so a missing, corrupt or unsupported sidecar cannot leave the previous slot's latch in place over an empty visit map. This does not rely on the API also minting a new session per load. Placement events carry no tally change, so an early placement cannot write into the outgoing slot's history, and a sidecar failure is still reported and still yields an empty history rather than a silently published one.

## Failure policy

The optional travel service is never assumed available.

- Unavailable at startup (opt-out, disabled group, or failed binding): nothing is recorded, a warning names the config switch, and `regionally_known` is omitted from prompts. Recorded history stays in the registry and continues to round-trip through the sidecar.
- A refused subscription (disposed hub, rejected owner) is reported and swallowed at the binding call. It degrades exactly like an unavailable service and never propagates into plugin startup, so it cannot stop the mission provider.
- Capability lost later, or the observer throws: recording stops until restart, again with history preserved and `regionally_known` omitted. Stale counts are never pitched as current recognition. "Until restart" here scopes to visit recording only — mission authoring, publication and save writes keep running; the wider provider restart is a separate mission-side policy.
- Visit history is not a completeness precondition for authoring. Mission authoring, publication, save/load and the independent `vganima.load-safety` hooks are unaffected by a travel fault; only the recognition feature degrades.
- The prompt flag is sampled once, at context-gather time. A stop that happens while an LLM call is already in flight does not retract the `regionally_known` snapshot that was already gathered, and it cancels nothing else: the next gather simply omits the section. This is deliberate snapshot semantics, not a broader cancellation scope.

Mission-provider failure still stops authoring, observation and save writes as before, and disposes the visit observer with it.

## Save data remains legacy

The v4 sidecar shape is unchanged: `visited_systems` still stores identifier, label, count and first/last game-seconds, and still upgrades from v2/v3 the same way. No API-managed save data, schema change or persistent travel-event history is introduced. Anima owns no travel journal; it owns per-system tallies.

Host tests cover placement seeding, gate and wormhole arrivals, redirected arrivals, duplicate/replayed evidence, cancellations, in-system arrivals, both A → B → A shapes (arrival-seeded and placement-seeded), refused subscriptions, unavailable labels, foreign and stale sessions, reentrant delivery, session replacement, slot-load reset, disposal and latched failure, plus Cecil metadata proving no native travel hook remains and that the observer references only the public API assembly. None of this is native runtime qualification of the API's travel hooks; that remains the API's own merge gate.
