namespace VGAnima.Pitch;

internal interface IPitchProvider
{
    /// <summary>Initial pitch (shortcut for <c>PitchForState(ctx, Initial)</c>).
    /// Kept for the existing call site in BarRefreshPatches; new code should
    /// call <see cref="PitchForState"/> with an explicit state.</summary>
    PitchResult Pitch(PatronContext ctx);

    /// <summary>State-aware pitch — lines vary by the player's progress on
    /// the pitched mission.</summary>
    PitchResult PitchForState(PatronContext ctx, BrokerState state);
}
