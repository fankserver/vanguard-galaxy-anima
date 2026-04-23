namespace VGAnima.Llm;

/// <summary>Pure function describing whether a resolved mission's news
/// has reached a broker, given distance, age, faction alignment, and
/// player fame. Split from <see cref="JournalContextBuilder"/> so the
/// formula is independently testable and tuneable.
///
/// <para>Direction: the gate computes a <i>required</i> magnitude at
/// each distance band, modified by age (older = harder), faction
/// (same-faction easier), and fame (famous players push their stories
/// further). Include iff the record's magnitude meets the requirement.</para>
///
/// <para>Design principle (from product: "gossip always travels faster"):
/// the adjacent-system base is 0, not 1. Magnitude-1 abandoned gathers
/// DO reach the neighbor system; they just don't reach 8 jumps out.
/// Far systems only hear about big events, old events only reach
/// famous players, etc.</para></summary>
internal static class MagnitudeReachFormula
{
    // --- Distance bands (jumps from resolved station to current broker) ---
    //
    // Wide bands rather than per-jump thresholds — the LLM reads magnitude
    // as a coarse "importance" signal, so the reach gate shouldn't be
    // higher-resolution than that. Bands are tuned so the 80% case is
    // intuitive: neighbor gossip flows freely, 5+ jumps is frontier,
    // 11+ is the edge of the galaxy.

    /// <summary>Required magnitude at each distance band.</summary>
    public static int BaseMagnitudeForDistance(int jumpsAway)
    {
        if (jumpsAway <= 2)  return 0;   // neighbors — gossip always travels
        if (jumpsAway <= 5)  return 2;
        if (jumpsAway <= 10) return 4;
        return 6;                        // 11+ or unknown
    }

    // --- Age penalty ---
    //
    // Old events fade; a 2-month-old salvage gather is not as talkable
    // as yesterday's. Days-scaled, not seconds — avoids floating-point
    // weirdness near the band edges.

    /// <summary>Penalty added to the required magnitude based on age in
    /// game-days. Monotone non-decreasing so older events only get
    /// harder to hear, never easier.</summary>
    public static int AgePenalty(double ageDays)
    {
        if (ageDays < 1)   return 0;
        if (ageDays < 7)   return 1;
        if (ageDays < 30)  return 2;
        return 3;
    }

    // --- Faction bonus ---
    //
    // Internal faction comms beat outside-the-network chatter. Same
    // faction as the broker makes the reach check one rung easier.

    public const int SameFactionBonus = 1;

    // --- Fame bonus ---
    //
    // Per the design decision (2026-04-23 chat): prestige is
    // generalized, not specialty-coupled. A CEO who made their name in
    // combat is still a CEO — known across gather and deliver brokers
    // too. Fame = max of the three rank tracks.

    /// <summary>Bonus subtracted from the required magnitude based on
    /// the player's highest rank track. Higher fame → events involving
    /// the player propagate farther.</summary>
    public static int FameBonus(int fame)
    {
        if (fame >= 15) return 3;
        if (fame >= 10) return 2;
        if (fame >= 5)  return 1;
        return 0;
    }

    /// <summary>Net magnitude the record must meet to reach the broker
    /// — base-for-distance plus age penalty, minus same-faction and
    /// fame bonuses. Exposed separately so callers that log per-entry
    /// decisions (JournalContextBuilder's debug trace) can report the
    /// required value without re-computing it.</summary>
    public static int RequiredMagnitude(
        int jumpsAway, double ageDays, bool sameFaction, int fame) =>
        BaseMagnitudeForDistance(jumpsAway)
        + AgePenalty(ageDays)
        - (sameFaction ? SameFactionBonus : 0)
        - FameBonus(fame);

    /// <summary>Verdict on whether a mission reaches the broker's ears.
    /// True iff its magnitude meets
    /// <see cref="RequiredMagnitude"/>.</summary>
    public static bool Reaches(
        int magnitude, int jumpsAway, double ageDays,
        bool sameFaction, int fame) =>
        magnitude >= RequiredMagnitude(jumpsAway, ageDays, sameFaction, fame);
}
