# Managed bar rosters (VGModAPI `Bars`)

Anima presents LLM brokers as API-owned bar contacts through the VGModAPI 0.2.8
`ModApi.Services.Bars` service instead of mutating the vanilla bar roster.
This is the default; the native roster path remains as a **fallback** selected
once in `Awake` from `Bars.Availability` (`ServiceUnavailableReason` is logged;
no polling afterwards). `[Bars] Enabled=false` forces native-fallback mode —
useful for A/B comparison and for users who prefer the vanilla seating model.

## Contract invariants (0.2.8)

- **Provider**: `AcquireProvider(thisPlugin, saveData: null)` from `Start`,
  never `Awake`; the v5 sidecar (not the API) is the durable presence store.
  A failed acquisition logs and falls back to native mode for the process.
- **Lifetime**: one owned provider and one `RosterFinalized` subscription for
  the plugin lifetime (restart re-attaches); `Dispose` unsubscribes the exact
  handler and disposes the provider once.
- **Declarations**: `BarPatronRetention.Transient` with consumer-minted
  `LocalId` (SHA-256 of the broker seed — never a storyId). The API mints the
  opaque `DefinitionId`; Anima never parses it. Handles are tracked until the
  API confirms absence.
- **Station presentation**: `ConfigureStation(stationId, Additive)` once per
  station per provider, always outside roster callbacks.
- **`RosterFinalized` is observation-only**: the callback stores the latest
  membership snapshot and queues deferred work; all registration, seat/sprite
  patching, and eviction run from `DrainPending` on the Unity Update pump.
  Stale members Anima does not track are logged once and otherwise owned by
  the API.
- **Retirement proves absence**: withdrawal calls
  `game.Bars.Get(definition).Remove()` on the live patron and only then
  disposes the declaration handle. `BrokerReservationRecovery.Retry` therefore
  runs the removal proof *before* the declaration revoke; a refusal keeps the
  contact tracked so the next reconciliation retries.
- **Dialogue**: authored dialogue starts from the `Register(definition,
  interact)` callback using the consumer-owned `Salesman` actor keyed by
  `LocalId`. The API suppresses `Salesman.InteractWithPatron` for owned
  contacts; `SalesmanPatches` additionally swallows native clicks on tracked
  managed seeds as a local guard against a suppression regression. Anima never
  seeks native `Salesman` identity for API-placed actors.

## Fallback mode

When Bars are unavailable (or opted out), the retained `CheckUpdatePatrons`
Harmony pair runs the legacy path: roster mutation, seat/sprite assignment,
per-frame `ReconcileTrackedContacts` eviction, and the same `SalesmanPatches`
presentation hooks. Purchase, presentation, and idle-animation hooks stay
patched in both modes.

## Session replacement

On a witnessed lifecycle session replacement the rosters reset: tracked
handles are withdrawn without waiting for placements, the visit observer
rebinds, and `PlayerReady` re-declares every persisted offered/accepted entry
(so the API can re-place brokers even if the player never enters a bar) —
including entries whose in-bar placement never committed. Warm TTS caches are
*not* dropped on session churn (re-declared brokers reuse them); they are
dropped when a broker retires permanently with its sidecar entry.
