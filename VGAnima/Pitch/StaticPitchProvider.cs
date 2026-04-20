namespace VGAnima.Pitch;

/// <summary>v0.1 pitch provider — produces templated pitch lines without
/// touching the network. Mission-aware lines (destination, reward) would
/// require reading <see cref="PatronContext.Mission"/>, but for the walking
/// skeleton we keep the pitch intentionally generic so it works even if the
/// mission object is malformed.</summary>
internal sealed class StaticPitchProvider : IPitchProvider
{
    public PitchResult Pitch(PatronContext ctx)
    {
        return new PitchResult(new[]
        {
            "Captain — I've got a run that needs a steady hand.",
            "Nothing fancy. Cargo haul to a neighbour system, decent pay, fair turnaround.",
            "I've posted the request on the station board. Grab it if you're in.",
        });
    }
}
