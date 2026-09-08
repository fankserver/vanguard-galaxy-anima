using System;
using System.Collections.Generic;
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
            _contacts.Clear(); _session = session;
        }
        return true;
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
        return _provider.Place(session, local).Succeeded;
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
