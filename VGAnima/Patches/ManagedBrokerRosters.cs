using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using VGAnima.Cache;
using System.Security.Cryptography;
using System.Text;
using Source.Galaxy.POI;
using Source.Galaxy.POI.Station.Patrons;
using VGModAPI;

namespace VGAnima.Patches;

/// <summary>API-owned presentation; generated dialogue and legacy mission authoring remain Anima data.</summary>
internal sealed class ManagedBrokerRosters : IDisposable
{
    private readonly IBarApi _api;
    private readonly IBarProvider _provider;
    private readonly Dictionary<string, Salesman> _contacts = new(StringComparer.Ordinal);
    private Guid _session;
    private readonly HashSet<string> _migratedStations = new(StringComparer.Ordinal);
    internal ManagedBrokerRosters(IBarApi api, Plugin plugin)
    {
        _api = api;
        var result = api.AcquireProvider(plugin);
        _provider = result.Provider ?? throw new InvalidOperationException("Bar provider refused: " + result.Status);
    }
    private bool Session(out Guid session)
    {
        session = ModApi.Current?.CurrentSession?.Id ?? Guid.Empty;
        if (session == Guid.Empty || !ReferenceEquals(ModApi.Bars, _api)) return false;
        if (_session != session)
        {
            foreach (var local in _contacts.Keys) _provider.Unregister(local);
            _contacts.Clear(); _migratedStations.Clear(); _session = session;
        }
        return true;
    }
    internal void ReconcileRetirements(Plugin plugin)
    {
        if (!Session(out var session) || !plugin.CanPublishFor(session)) return;
        foreach (var entry in plugin.PersistedRegistry.All().Where(entry => entry.BarRetirementPending).ToArray())
            if (Remove(entry.Broker.Seed)) plugin.PersistedRegistry.Remove(entry.StoryId);
    }

    internal bool HasAt(SpaceStation station) => Session(out _) && _contacts.Values.Any(patron =>
        Plugin.Instance.Registry.TryGet(patron, out var record) && record.StationId == station.guid);

    internal void Rehydrate(Plugin plugin, SpaceStation station)
    {
        if (!Session(out var session) || !plugin.CanPublishFor(session)) return;
        var entries = plugin.PersistedRegistry.All().Where(entry => entry.Broker.StationId == station.guid).ToArray();
        if (_migratedStations.Add(station.guid))
            station.bar.availablePatrons.RemoveAll(patron => patron is Salesman salesman
                && entries.Any(entry => entry.Broker.Seed == salesman.seed));
        foreach (var entry in entries)
        {
            if (entry.BarRetirementPending)
            {
                if (Remove(entry.Broker.Seed)) plugin.PersistedRegistry.Remove(entry.StoryId);
                continue;
            }
            var local = LocalId(entry.Broker.Seed);
            if (_contacts.ContainsKey(local)) continue;
            var patron = new Salesman(entry.Broker.Seed, station);
            patron.Initialize();
            if (!string.IsNullOrWhiteSpace(entry.Broker.NameSnapshot)) patron._name = entry.Broker.NameSnapshot;
            var story = entry.Broker.Story;
            var lines = story.Pitch.Concat(story.CheckIn).Concat(story.Payout)
                .Where(text => !string.IsNullOrWhiteSpace(text)).Select(text => (Speaker: patron.name, Text: text)).ToList();
            plugin.Registry.Register(patron, new ConversionRecord(lines, station, entry.StoryId, story, stationId: station.guid));
            if (!Place(patron, station)) { plugin.Registry.Remove(patron); continue; }
            plugin.Vgtts.RegisterVoice(patron.name, patron.isMale ? "kokoro:12" : "kokoro:9");
            _ = Task.Run(async () =>
            {
                foreach (var line in lines)
                    try { await plugin.Vgtts.WarmCacheAsync(line.Speaker, line.Text, CancellationToken.None); } catch { }
            });
        }
    }

    internal static string LocalId(string seed)
    {
        using var hash = SHA256.Create();
        return "broker-" + BitConverter.ToString(hash.ComputeHash(Encoding.UTF8.GetBytes(seed))).Replace("-", "").ToLowerInvariant().Substring(0, 40);
    }
    internal bool Place(Salesman patron, SpaceStation station)
    {
        if (!Session(out var session)) return false;
        var local = LocalId(patron.seed);
        if (_contacts.TryGetValue(local, out var existing) && existing.seed != patron.seed) return false;
        if (!_contacts.ContainsKey(local))
        {
            var definition = new BarPatronDefinition(local, station.guid, patron.name,
                string.IsNullOrWhiteSpace(patron.description) ? "Contract broker" : patron.description, patron.seed);
            var registered = _provider.Register(definition, interaction =>
            {
                if (interaction.SessionId != _session || ModApi.Current?.CurrentSession?.Id != _session) return;
                if (_contacts.TryGetValue(local, out var current)) SalesmanPatches.InteractOwned(current);
            });
            if (!registered.Succeeded) return false;
            _contacts.Add(local, patron);
        }
        if (_provider.Place(session, local).Succeeded) return true;
        _provider.Unregister(local); _contacts.Remove(local);
        return false;
    }
    internal bool Remove(string seed)
    {
        if (!Session(out var session)) return false;
        var local = LocalId(seed);
        if (!_provider.Remove(session, local).Succeeded) return false;
        _provider.Unregister(local); _contacts.Remove(local);
        return true;
    }
    public void Dispose() { _provider.Dispose(); _contacts.Clear(); _session = Guid.Empty; }
}
