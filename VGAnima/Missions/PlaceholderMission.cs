using BepInEx.Logging;
using Source.MissionSystem;

namespace VGAnima.Missions;

/// <summary>Returned by <see cref="VGAnima.Patches.MissionLookupPatch"/>
/// when vanilla tries to resolve a <c>vganima_llm_*</c> storyId that our
/// registry doesn't have — typically because the sidecar is missing,
/// corrupt, or was quarantined. Zero steps means vanilla's tick sees
/// nothing to complete; the mission archives itself on first evaluation
/// (or can be archived by the player). Logs a warning at construction
/// identifying the missing storyId.
///
/// <para>Note on the <c>Mission</c> shape (per decomp scout):
/// <c>steps</c> is an auto-property with a private setter but is
/// initialized to an empty <see cref="System.Collections.Generic.List{T}"/>
/// at field init, so <c>new Mission()</c> already yields a zero-step
/// instance. We only write the public fields here.</para></summary>
internal static class PlaceholderMission
{
    private static readonly ManualLogSource Log =
        BepInEx.Logging.Logger.CreateLogSource("VGAnima");

    public static Mission Build(string storyId)
    {
        Log.LogWarning(
            $"No sidecar entry for storyId `{storyId}` — returning placeholder " +
            $"(auto-archive)");

        return new Mission
        {
            storyId        = storyId,
            name           = "Archived VGAnima Mission",
            description    = "This LLM-authored mission's data is no longer available.",
            completionText = "Archived.",
            dynamicLevel   = false,
            trackedOnHud   = false,
            canBeIdled     = true,
        };
    }
}
