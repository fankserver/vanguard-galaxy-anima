using System;
using System.Collections.Generic;
using BepInEx.Bootstrap;

namespace VGAnima.MissionJournal;

/// <summary>Optional journal access through a consumer-owned type boundary.
/// No field or method signature here requires the producer assembly.</summary>
internal sealed class VgMissionJournalBridge
{
    private readonly IJournalQueries? _query;
    public bool IsAvailable => _query != null;

    public VgMissionJournalBridge()
    {
        try
        {
            if (Chainloader.PluginInfos != null && Chainloader.PluginInfos.ContainsKey("vgmissionjournal"))
                _query = TypedJournalAdapter.Open();
        }
        catch { _query = null; }
    }

    // Tests can inject the real typed query interface without leaking it into this type's metadata.
    internal VgMissionJournalBridge(object? query) => _query = query == null ? null : TypedJournalAdapter.Wrap(query);

    public IReadOnlyList<JournalRecord> GetActiveMissions() => _query?.Active() ?? Array.Empty<JournalRecord>();
    public IReadOnlyList<JournalRecord> GetMissionsInSystem(string systemId, double sinceGameSeconds = 0) =>
        string.IsNullOrEmpty(systemId) ? Array.Empty<JournalRecord>() : _query?.InSystem(systemId, sinceGameSeconds) ?? Array.Empty<JournalRecord>();
    public IReadOnlyList<JournalRecord> GetMissionsByFaction(string factionId, double sinceGameSeconds = 0) =>
        string.IsNullOrEmpty(factionId) ? Array.Empty<JournalRecord>() : _query?.ByFaction(factionId, sinceGameSeconds) ?? Array.Empty<JournalRecord>();
    public IReadOnlyList<JournalRecord> GetMissionsWithinJumps(string pivotSystemId, int maxJumps,
        Func<string, string, int> jumpDistance, double sinceGameSeconds = 0) =>
        string.IsNullOrEmpty(pivotSystemId) ? Array.Empty<JournalRecord>() :
            _query?.WithinJumps(pivotSystemId, maxJumps, jumpDistance, sinceGameSeconds) ?? Array.Empty<JournalRecord>();
}

internal interface IJournalQueries
{
    IReadOnlyList<JournalRecord> Active();
    IReadOnlyList<JournalRecord> InSystem(string id, double since);
    IReadOnlyList<JournalRecord> ByFaction(string id, double since);
    IReadOnlyList<JournalRecord> WithinJumps(string id, int maximum, Func<string, string, int> distance, double since);
}
