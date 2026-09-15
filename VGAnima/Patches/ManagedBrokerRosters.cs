using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using VGAnima.Cache;
using VGAnima.Persistence;
using System.Security.Cryptography;
using System.Text;
using Source.Galaxy.POI;
using Source.Galaxy.POI.Station.Patrons;
using VGModAPI;

namespace VGAnima.Patches;

/// <summary>
/// API-owned presentation; generated dialogue and legacy mission authoring remain Anima data.
/// </summary>
/// <remarks>
/// <para>0.2.x contact model: Anima <em>declares</em> contacts
/// (<see cref="IBarProvider.Register(BarPatronDefinition, Action{IBarPatron}?)"/>)
/// and the API places them; there is no per-session Place/Remove. Declarations
/// are <see cref="BarPatronRetention.Transient"/> — the v5 sidecar registry is
/// the single durable presence authority, so the API never stores a competing
/// one. Definitions are declared at PlayerReady from the preloaded registry and
/// disposed when a broker is no longer declared. Retirement proves absence via
/// <see cref="IBarPatron.Remove"/> on the live patron before dropping the
/// handle.</para>
/// <para><see cref="IBarService.RosterFinalized"/> is observation-only: the
/// handler never registers or mutates. Rehydration, orphan purge and chance
/// dispatch run from <see cref="DrainPending"/> on the plugin's Update pump,
/// outside API callback dispatch.</para>
/// </remarks>
internal sealed class ManagedBrokerRosters : IDisposable
{
    private const string ProceduralMaleVoice   = "kokoro:12";
    private const string ProceduralFemaleVoice = "kokoro:9";

    /// <summary>One declared contact: the API handle plus Anima's own dialogue
    /// actor. The actor is consumer-owned bookkeeping; we never seek native
    /// Salesman identity for API-placed contacts.</summary>
    private sealed class Contact
    {
        internal IBarPatronDefinition Definition = null!;
        internal string Seed = string.Empty;
        internal string StationId = string.Empty;
        internal Salesman? Actor;
    }

    private readonly IBarService _api;
    private readonly Plugin _plugin;
    private readonly IBarProvider _provider;
    private readonly Dictionary<string, Contact> _contacts = new(StringComparer.Ordinal);
    private readonly HashSet<string> _configuredStations = new(StringComparer.Ordinal);
    private readonly HashSet<string> _legacyRosterNoticed = new(StringComparer.Ordinal);
    private readonly HashSet<string> _denialLogged = new(StringComparer.Ordinal);
    private readonly Dictionary<string, BarRosterFinalized> _latestRosters = new(StringComparer.Ordinal);
    private string? _pendingRefreshStation;
    private Guid _session;
    private bool _disposed;

    internal ManagedBrokerRosters(IBarService api, Plugin plugin)
    {
        _api = api ?? throw new ArgumentNullException(nameof(api));
        _plugin = plugin ?? throw new ArgumentNullException(nameof(plugin));
        // saveData: null — broker presence is canonically Anima's v5 sidecar,
        // not an API save registration; no save prerequisite gates placements.
        var result = api.AcquireProvider(plugin, saveData: null);
        _provider = result.Provider ?? throw new InvalidOperationException("Bar provider refused: " + result.Status + " " + result.Detail);
        try { api.RosterFinalized += OnRosterFinalized; }
        catch
        {
            // Never leak an owned provider when setup fails: the API keeps
            // acquired providers for the process and reattachment needs a
            // restart.
            try { _provider.Dispose(); } catch { }
            throw;
        }
    }

    private bool ApiAvailable
    {
        get { try { return _api.Availability.IsAvailable; } catch { return false; } }
    }

    private bool Session(out Guid session)
    {
        session = Guid.Empty;
        if (!ApiAvailable) return false;
        var current = _plugin.ObservedSession;
        if (current == null) return false;
        session = current.Id;
        if (_session != session) ResetSession(session);
        return true;
    }

    /// <summary>Transient declarations live per game session: withdraw every
    /// handle on replacement; PlayerReady re-declares from the sidecar registry.</summary>
    private void ResetSession(Guid session)
    {
        foreach (var contact in _contacts.Values) SafeDispose(contact.Definition);
        _contacts.Clear();
        _configuredStations.Clear();
        _denialLogged.Clear();
        _latestRosters.Clear();
        _pendingRefreshStation = null;
        _legacyRosterNoticed.Clear();
        _session = session;
    }

    /// <summary>Observation only — no registration or mutation happens inside
    /// API callback dispatch; work is deferred to <see cref="DrainPending"/>.</summary>
    private void OnRosterFinalized(BarRosterFinalized roster)
    {
        if (_disposed || roster == null) return;
        try
        {
            var live = _plugin.ObservedSession;
            if (live == null || roster.SessionId != live.Id) return; // stale session evidence
            _latestRosters[roster.StationId] = roster;
            if (roster.DeniedProviders.TryGetValue(_provider.ProviderId, out var reason)
                && _contacts.Values.Any(c => c.StationId == roster.StationId)
                && _denialLogged.Add(roster.StationId))
                Plugin.Log.LogWarning(
                    $"Managed broker contributions denied at station {roster.StationId} by policy: {reason}");
            if (_plugin.CanPublishFor(roster.SessionId) && SpaceStation.current?.guid == roster.StationId)
                _pendingRefreshStation = roster.StationId;
        }
        catch (Exception error)
        {
            // The API isolates subscriber failures; belt-and-braces locally.
            Plugin.Log.LogError("RosterFinalized observer failed (observation-only): " + error);
        }
    }

    /// <summary>Declare every restored broker contact before the first bar
    /// refresh of the session (PlayerReady), so station admission sees them
    /// without waiting for a second refresh.</summary>
    internal void RegisterRestoredDefinitions(Plugin plugin)
    {
        if (!Session(out var session) || !plugin.CanPublishFor(session)) return;
        foreach (var entry in plugin.PersistedRegistry.All().ToArray())
        {
            if (entry.BarRetirementPending) continue;
            if (plugin.PersistedRegistry.BarReservations.Any(r => r.Seed == entry.Broker.Seed && r.Aborted)) continue;
            var local = LocalId(entry.Broker.Seed);
            if (_contacts.ContainsKey(local)) continue;
            var identity = MaterializeActor(entry, station: null);
            if (identity == null) continue;
            Declare(identity, entry.Broker.StationId, out _);
        }
    }

    /// <summary>Runs the deferred work a finalized roster observation queued:
    /// tracked-handle reconciliation plus rehydrate/purge/chance-dispatch for
    /// the station the player is docked at. Main thread, outside callback dispatch.</summary>
    internal void DrainPending(Plugin plugin)
    {
        if (!Session(out var session) || !plugin.CanPublishFor(session))
        {
            _pendingRefreshStation = null;
            return;
        }
        ReconcileTrackedContacts(plugin);
        var stationId = _pendingRefreshStation;
        _pendingRefreshStation = null;
        var station = SpaceStation.current;
        if (stationId == null || station == null || station.guid != stationId || station.bar == null) return;
        Rehydrate(plugin, station);
        OrphanPurgeManaged(plugin, station);
        BarRefreshPatches.DispatchManagedStationRefresh(plugin, station);
    }

    /// <summary>Withdraw tracked contacts whose durable entry vanished (e.g.
    /// orphan purge): prove absence through the live patron, then dispose the
    /// declaration.</summary>
    private void ReconcileTrackedContacts(Plugin plugin)
    {
        foreach (var pair in _contacts.ToArray())
        {
            if (plugin.PersistedRegistry.FindBySeed(pair.Value.Seed) != null) continue;
            if (!Remove(pair.Value.Seed)) continue;
            DropRecord(plugin, pair.Value.Actor);
        }
    }

    internal void ReconcileRetirements(Plugin plugin)
    {
        if (!Session(out var session) || !plugin.CanPublishFor(session)) return;
        foreach (var reservation in plugin.PersistedRegistry.BarReservations.ToArray())
            BrokerReservationRecovery.Retry(plugin.PersistedRegistry, reservation, Revoke, Remove);
        foreach (var entry in plugin.PersistedRegistry.All().Where(entry => entry.BarRetirementPending).ToArray())
            if (Remove(entry.Broker.Seed)) plugin.PersistedRegistry.Remove(entry.StoryId);
    }

    internal bool HasAt(SpaceStation station) => Session(out _) &&
        (_plugin.PersistedRegistry.BarReservations.Any(reservation => reservation.StationId == station.guid)
         || _contacts.Values.Any(contact => contact.StationId == station.guid));

    internal bool Reserve(Salesman patron, SpaceStation station)
    {
        _plugin.PersistedRegistry.ReserveBar(new BrokerReservation(patron.seed, station.guid));
        if (Declare(patron, station.guid, out _)) return true;
        _plugin.PersistedRegistry.FinishBarReservation(patron.seed);
        return false;
    }
    internal void Commit(string seed) => _plugin.PersistedRegistry.FinishBarReservation(seed);
    private void Revoke(string seed)
    {
        var local = LocalId(seed);
        if (_contacts.TryGetValue(local, out var contact)) SafeDisposeAndRemove(local, contact);
    }
    internal bool Rollback(string seed, string station)
    {
        var reservation = new BrokerReservation(seed, station, Aborted: true);
        _plugin.PersistedRegistry.ReserveBar(reservation);
        return BrokerReservationRecovery.Retry(_plugin.PersistedRegistry, reservation, Revoke, Remove);
    }

    /// <summary>True while a seed is a tracked managed declaration — used by
    /// the interaction prefix as a defensive local guard against an API
    /// suppression regression (an owned contact must never open the vanilla
    /// sale UI).</summary>
    internal bool OwnsSeed(string? seed) =>
        seed != null && _contacts.Values.Any(contact => contact.Seed == seed);

    /// <summary>Retire a broker: prove absence on the live patron, then
    /// withdraw the declaration. An undeclared seed is already absent. A
    /// refused/queued removal keeps the handle and the durable identity
    /// (retirement stays pending for the next reconciliation).</summary>
    internal bool Remove(string seed)
    {
        if (seed == null) return false;
        if (!Session(out _)) return false;
        var local = LocalId(seed);
        if (!_contacts.TryGetValue(local, out var contact)) return true; // declaration withdrawn = absent
        var game = _plugin.CurrentGame;
        if (game == null) return false;
        IBarPatron? patron = null;
        try { patron = game.Bars.Get(contact.Definition); }
        catch (Exception error)
        {
            Plugin.Log.LogWarning($"Managed patron lookup failed for {contact.Seed}: {error.Message}");
            return false;
        }
        if (patron != null && patron.Status != BarPatronStatus.Removed)
        {
            var result = patron.Remove();
            if (result.Status is not (BarStatus.Succeeded or BarStatus.NotRegistered or BarStatus.GameEnded))
            {
                Plugin.Log.LogDebug(
                    $"Managed patron removal not yet proven for {contact.Seed}: {result.Status} {result.Detail}");
                return false;
            }
        }
        SafeDisposeAndRemove(local, contact);
        return true;
    }

    /// <summary>Single withdrawal funnel: drop the consumer record and warmed
    /// TTS lines, dispose the declaration handle, and untrack. Used by every
    /// path that withdraws a tracked contact.</summary>
    private void SafeDisposeAndRemove(string local, Contact contact)
    {
        DropRecord(_plugin, contact.Actor);
        SafeDispose(contact.Definition);
        _contacts.Remove(local);
    }

    /// <summary>Idempotent per-station presentation prep: consumer records,
    /// voice warm, and declarations for the station the player is docked at.
    /// Never removes anything from the API-owned roster itself.</summary>
    internal void Rehydrate(Plugin plugin, SpaceStation station)
    {
        if (station == null || !Session(out var session) || !plugin.CanPublishFor(session)) return;
        foreach (var entry in plugin.PersistedRegistry.All().Where(entry => entry.Broker.StationId == station.guid).ToArray())
        {
            if (plugin.PersistedRegistry.BarReservations.Any(reservation => reservation.Seed == entry.Broker.Seed && reservation.Aborted)) continue;
            if (entry.BarRetirementPending)
            {
                if (Remove(entry.Broker.Seed)) plugin.PersistedRegistry.Remove(entry.StoryId);
                continue;
            }
            var local = LocalId(entry.Broker.Seed);
            // A vanilla-persisted row with the same seed (native-fallback era
            // leftover) is roster-owned: present it through the retained
            // interaction prefix and keep the managed declaration withdrawn.
            // Anima never mutates the API-owned roster.
            var native = station.bar?.availablePatrons.OfType<Salesman>()
                .FirstOrDefault(s => s.seed == entry.Broker.Seed);
            if (native != null)
            {
                if (_legacyRosterNoticed.Add(station.guid))
                    Plugin.Log.LogInfo(
                        $"Legacy native-mode broker rows at station {station.guid} are left to vanilla roster rollover; " +
                        $"Anima does not mutate the API-owned roster.");
                // Withdraw any tracked managed duplicate only with proven
                // absence; until then skip the station entry (the click guard
                // keeps it inert) so no live handle is dropped unproven.
                if (_contacts.ContainsKey(local) && !Remove(entry.Broker.Seed)) continue;
                if (!plugin.Registry.TryGet(native, out _))
                    RegisterRecord(plugin, native, entry, station);
                continue;
            }
            if (!_contacts.TryGetValue(local, out var contact))
            {
                var identity = MaterializeActor(entry, station);
                if (identity == null || !Declare(identity, station.guid, out contact) || contact == null) continue;
            }
            EnsureActorAndRecord(plugin, contact, entry, station);
        }
    }

    /// <summary>Declares one contact with the API: additive station policy
    /// (idempotent, never inside roster callbacks) plus a Transient
    /// definition. Interaction dispatches to the consumer-owned actor.</summary>
    private bool Declare(Salesman identity, string stationId, out Contact? contact)
    {
        contact = null;
        if (identity == null || string.IsNullOrEmpty(stationId) || !Session(out _)) return false;
        var local = LocalId(identity.seed);
        if (_contacts.TryGetValue(local, out var existing))
            return existing.Seed == identity.seed;
        ConfigureStationOnce(stationId);
        var description = string.IsNullOrWhiteSpace(identity.description) ? "Contract broker" : identity.description;
        var definition = new BarPatronDefinition(
            local, stationId, identity.name, description,
            new BarPatronPresentation(identity.seed, portrait: null, isMale: identity.isMale),
            BarPatronRetention.Transient);
        BarRegistrationResult registered;
        try { registered = _provider.Register(definition, _ => Interact(local)); }
        catch (Exception error)
        {
            Plugin.Log.LogWarning($"Managed contact declaration threw at {stationId}: {error.Message}");
            return false;
        }
        if (!registered.Succeeded || registered.Definition == null)
        {
            Plugin.Log.LogWarning($"Managed contact declaration refused at {stationId}: {registered.Status} {registered.Detail}");
            return false;
        }
        contact = new Contact { Definition = registered.Definition, Seed = identity.seed, StationId = stationId, Actor = identity };
        _contacts[local] = contact;
        return true;
    }

    private void ConfigureStationOnce(string stationId)
    {
        if (!_configuredStations.Add(stationId)) return;
        try
        {
            var result = _provider.ConfigureStation(stationId, BarRosterOwnership.Additive);
            if (!result.Succeeded)
            {
                _configuredStations.Remove(stationId);
                Plugin.Log.LogWarning($"Additive roster policy refused at {stationId}: {result.Status} {result.Detail}");
            }
        }
        catch (Exception error)
        {
            _configuredStations.Remove(stationId);
            Plugin.Log.LogWarning($"Additive roster policy threw at {stationId}: {error.Message}");
        }
    }

    /// <summary>Owned-contact click: API-owned contacts never reach the
    /// vanilla <c>Salesman.InteractWithPatron</c> prefix, so dialogue is
    /// started from the consumer-owned actor and record — keyed by the
    /// definition's LocalId, never by native Salesman identity.</summary>
    private void Interact(string local)
    {
        if (_disposed) return;
        try
        {
            if (!_contacts.TryGetValue(local, out var contact)) return; // withdrawn declaration
            var station = SpaceStation.current;
            if (station?.bar == null) return;
            var entry = _plugin.PersistedRegistry.FindBySeed(contact.Seed);
            if (entry == null || entry.BarRetirementPending) return;   // no owned narrative to present
            if (station.guid != entry.Broker.StationId) return;        // foreign context — do not present
            EnsureActorAndRecord(_plugin, contact, entry, station);
            if (contact.Actor != null && _plugin.Registry.TryGet(contact.Actor, out _))
                SalesmanPatches.InteractOwned(contact.Actor);
        }
        catch (Exception error)
        {
            Plugin.Log.LogError("Managed broker interaction failed: " + error);
        }
    }

    private void EnsureActorAndRecord(Plugin plugin, Contact contact, PersistedEntry entry, SpaceStation station)
    {
        contact.Actor ??= MaterializeActor(entry, station);
        if (contact.Actor == null || plugin.Registry.TryGet(contact.Actor, out _)) return;
        RegisterRecord(plugin, contact.Actor, entry, station);
    }

    private static void RegisterRecord(Plugin plugin, Salesman actor, PersistedEntry entry, SpaceStation station)
    {
        var story = entry.Broker.Story;
        var lines = story.Pitch.Concat(story.CheckIn).Concat(story.Payout)
            .Where(text => !string.IsNullOrWhiteSpace(text))
            .Select(text => (Speaker: actor.name, Text: text)).ToList();
        plugin.Registry.Register(actor, new ConversionRecord(lines, station, entry.StoryId, story, stationId: station.guid));
        plugin.Vgtts.RegisterVoice(actor.name, actor.isMale ? ProceduralMaleVoice : ProceduralFemaleVoice);
        _ = Task.Run(async () =>
        {
            foreach (var line in lines)
                try { await plugin.Vgtts.WarmCacheAsync(line.Speaker, line.Text, CancellationToken.None); } catch { }
        });
    }

    private void DropRecord(Plugin plugin, Salesman? actor)
    {
        if (actor == null || !plugin.Registry.TryGet(actor, out var record)) return;
        foreach (var (speaker, text) in record.WarmedLines)
            plugin.Vgtts.DropCache(speaker, text);
        plugin.Registry.Remove(actor);
    }

    /// <summary>Seed-derived vanilla salesman used as the dialogue actor and
    /// identity source (name/isMale come from the seed via vanilla's own
    /// regeneration). Station may be null for pre-visit declarations — the
    /// seed generator does not consult it.</summary>
    private static Salesman? MaterializeActor(PersistedEntry entry, SpaceStation? station)
    {
        try
        {
            var patron = new Salesman(entry.Broker.Seed, station);
            patron.Initialize();
            if (!string.IsNullOrWhiteSpace(entry.Broker.NameSnapshot)) patron._name = entry.Broker.NameSnapshot;
            return patron;
        }
        catch (Exception error)
        {
            Plugin.Log.LogWarning($"Failed to materialize broker actor for seed {entry.Broker.Seed}: {error.Message}");
            return null;
        }
    }

    private void OrphanPurgeManaged(Plugin plugin, SpaceStation station)
    {
        var reg = plugin.PersistedRegistry;
        if (reg == null || station.bar == null) return;
        try
        {
            var activeIds = new List<string>();
            var archivedIds = new List<string>();
            foreach (var entry in reg.All())
            {
                if (plugin.PlayerView.GetActive(entry.StoryId) is not null)
                    activeIds.Add(entry.StoryId);
                else if (plugin.PlayerView.IsArchived(entry.StoryId))
                    archivedIds.Add(entry.StoryId);
            }
            // Finalized membership is the authoritative roster view; the live
            // native list is merged in for rows the refresh snapshot predates.
            var seeds = new List<string>();
            if (_latestRosters.TryGetValue(station.guid, out var roster))
                seeds.AddRange(roster.Members.Select(member => member.Seed));
            foreach (var patron in station.bar.availablePatrons)
                if (patron is Salesman salesman && !string.IsNullOrEmpty(salesman.seed))
                    seeds.Add(salesman.seed);
            var dropped = OrphanPurger.Purge(reg, activeIds, archivedIds, seeds, station.guid);
            if (dropped.Count > 0)
                Plugin.Log.LogInfo(
                    $"Managed orphan-purged {dropped.Count} stale entr{(dropped.Count == 1 ? "y" : "ies")} " +
                    $"at station {station.guid}: {string.Join(", ", dropped)}");
        }
        catch (Exception e)
        {
            Plugin.Log.LogError($"Managed orphan purge failed: {e}");
        }
    }

    private static void SafeDispose(IBarPatronDefinition definition)
    {
        try { definition.Dispose(); }
        catch (Exception error) { Plugin.Log.LogWarning($"Managed contact handle disposal failed; restart required for reattachment: {error.Message}"); }
    }

    internal static string LocalId(string seed)
    {
        using var hash = SHA256.Create();
        return "broker-" + BitConverter.ToString(hash.ComputeHash(Encoding.UTF8.GetBytes(seed))).Replace("-", "").ToLowerInvariant().Substring(0, 40);
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        try { _api.RosterFinalized -= OnRosterFinalized; }
        catch (Exception error) { Plugin.Log.LogWarning("RosterFinalized unsubscription failed: " + error.Message); }
        foreach (var contact in _contacts.Values) SafeDispose(contact.Definition);
        _contacts.Clear();
        _configuredStations.Clear();
        _denialLogged.Clear();
        _latestRosters.Clear();
        _pendingRefreshStation = null;
        _legacyRosterNoticed.Clear();
        _session = Guid.Empty;
        _provider.Dispose();
    }
}
