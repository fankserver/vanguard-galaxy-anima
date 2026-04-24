namespace VGAnima.Persistence;

/// <summary>Canonical outcome labels used in LLM context JSON.
/// Previously lived alongside the (now-deleted) completed-mission log
/// record; still needed because the journal's active/resolved windows
/// surface these strings to the model regardless of source.</summary>
internal static class CompletedMissionOutcomes
{
    public const string Completed  = "completed";
    public const string Failed     = "failed";
    public const string Abandoned  = "abandoned";
    /// <summary>Used only by the journal's active window — marks
    /// in-flight entries so the LLM can distinguish them from resolved
    /// history.</summary>
    public const string InProgress = "in_progress";
}

/// <summary>Canonical archetype labels. Emitted by archetype inferrers
/// (one for <c>LlmMissionBlock</c>, one for VGMissionJournal's
/// <c>MissionRecord</c>) so the LLM sees the same vocabulary regardless
/// of which mission source produced the journal entry.</summary>
internal static class MissionArchetypes
{
    public const string Combat           = "combat";
    public const string Gather           = "gather";
    public const string Salvage          = "salvage";
    public const string Deliver          = "deliver";
    public const string Escort           = "escort";
    public const string DefendedCollect  = "defended-collect";
    public const string Other            = "other";
}
