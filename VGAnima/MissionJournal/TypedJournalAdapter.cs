using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using VGMissionJournal.Api;
using VGMissionJournal.Logging;

namespace VGAnima.MissionJournal;

/// <summary>Typed producer references are resolved only after the presence guard.
/// Keep the factory signatures producer-free and prevent inlining across that boundary.</summary>
internal static class TypedJournalAdapter
{
    [MethodImpl(MethodImplOptions.NoInlining)]
    internal static IJournalQueries? Open() => MissionJournalApi.Current is { } query ? new Queries(query) : null;

    [MethodImpl(MethodImplOptions.NoInlining)]
    internal static IJournalQueries Wrap(object query) => new Queries((IMissionJournalQuery)query);

    private sealed class Queries : IJournalQueries
    {
        private readonly IMissionJournalQuery _query;
        internal Queries(IMissionJournalQuery query) => _query = query;
        public IReadOnlyList<JournalRecord> Active() => Project(_query.GetActiveMissions());
        public IReadOnlyList<JournalRecord> InSystem(string id, double since) => Project(_query.GetMissionsInSystem(id, since));
        public IReadOnlyList<JournalRecord> ByFaction(string id, double since) => Project(_query.GetMissionsByFaction(id, since));
        public IReadOnlyList<JournalRecord> WithinJumps(string id, int maximum, Func<string, string, int> distance, double since) =>
            Project(_query.GetMissionsWithinJumps(id, maximum, distance, since));
        private static IReadOnlyList<JournalRecord> Project(IReadOnlyList<MissionRecord> records) => records.Select(record =>
            new JournalRecord(record.StoryId, record.MissionInstanceId, record.MissionName, record.MissionSubclass,
                record.SourceStationId, record.SourceStationName, record.SourceSystemId, record.SourceSystemName,
                record.SourceFaction, record.IsActive, record.TerminalAtGameSeconds,
                record.Outcome.HasValue ? MissionRecordArchetype.OutcomeString(record.Outcome) : null,
                MissionRecordArchetype.ObjectiveTags(record), MagnitudeDerivation.Derive(record))).ToArray();
    }
}
