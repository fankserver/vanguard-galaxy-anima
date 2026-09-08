using System.Collections.Generic;

namespace VGAnima.MissionJournal;

/// <summary>Consumer-owned immutable projection; safe to load without the journal assembly.</summary>
internal sealed record JournalRecord(
    string StoryId, string MissionInstanceId, string? MissionName, string MissionSubclass,
    string? SourceStationId, string? SourceStationName, string? SourceSystemId,
    string? SourceSystemName, string? SourceFaction, bool IsActive,
    double? TerminalAtGameSeconds, string? Outcome, IReadOnlyList<string> ObjectiveTags, int Magnitude);
