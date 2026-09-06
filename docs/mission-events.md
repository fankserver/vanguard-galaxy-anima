# Mission provider events (0.3.0)

Requires VGModAPI 0.1.8–0.1.x and `[Missions] Enabled = true`. API mission events are experimental; source/host checks do not establish native or owner acceptance. No direct-hook fallback remains. Anima does not require identity continuity: its provider-definition registry is not a historical journal.

## Ownership and ordering

Only existing registry entries with factory-generated `vganima_llm_` IDs participate. Factories generate unique definition IDs; these are distinct from API occurrence GUIDs. The observer keeps a set of current occurrence GUIDs per definition and clears that index when an event carries a different session ID.

- Accepted marks an owned definition accepted; duplicate delivery is idempotent.
- Restored associates a current occurrence with an existing owned definition, without fabricating acceptance or timestamp. This is definition ownership, not matching an old journal record by name or ordinal.
- Completed, Failed, Abandoned and Removed retire the matching tracked occurrence. A definition is removed only after its last tracked live occurrence ends. A following nested archive/removal cannot delete a newly accepted occurrence.
- Archive-only, unknown, foreign and untracked terminal events do not remove definitions.

No callback reads, retains or mutates native missions. Anima's authoring factories still deliberately create vanilla missions outside the observer; their economics, objective tags and prompt schema are unchanged.

## Failure and asynchronous work

Missing dependencies prevent BepInEx startup. Disabled/unsupported mission capability prevents Anima hook installation. Authoring-hook initialization failure rolls back authoring installation. Observer failure stops the provider; capability loss is polled at most once per second and checked directly at dispatch, publication and save/quit writes. Stop unsubscribes events, removes authoring hooks, disables save publication and disposes the LLM client. The separate `vganima.load-safety` Harmony owner retains the load prefix and missing-definition lookup safeguard: loading another slot still unregisters previously restored factories, reloads its sidecar and reconstructs factories (or substitutes placeholders). These are reconstruction safeguards, not resumed authoring or observation. They are removed only on plugin destruction. Restart is required; there is no automatic fallback or catch-up. Removing the plugin/API entirely is not a safe-uninstall procedure for saves containing Anima content.

LLM dispatch captures the API session on the main thread; queued publication checks the same usable session again. The assigner also checks availability before factory/world construction. PlayerReady/GameplayInitialized are session validity gates only: existing bar callbacks retain their own UI readiness responsibilities.

## Save data remains legacy

The v4 sidecar and native save/load/lookup hooks remain. No persistent occurrence field, schema change or implicit conversion to API save data is introduced. Sidecars have their existing best-effort per-file and quit-time behavior, not exact-snapshot identity, cross-file atomicity or failed-save rollback guarantees. Keep paired save/sidecar backups. API-managed storage migration would be a separate change.

Host tests cover occurrence membership, duplicate events, missing/foreign histories, restoration, session reset, disposal, pre-factory refusal, metadata dependency/version parity and remaining Harmony target/parameter bindings. Binding inspection uses Cecil metadata only (no Unity UI runtime execution). Controlled native traces must be recorded separately; they do not qualify a real LLM backend or every authoring intent.
