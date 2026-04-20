namespace VGAnima.Pitch;

/// <summary>v0.1 pitch provider — produces templated pitch lines without
/// touching the network. Text is intentionally ASCII-only: the game's pixel16
/// font drops em-dashes and smart quotes to spaces.</summary>
internal sealed class StaticPitchProvider : IPitchProvider
{
    public PitchResult Pitch(PatronContext ctx) => PitchForState(ctx, BrokerState.Initial);

    public PitchResult PitchForState(PatronContext ctx, BrokerState state) => state switch
    {
        BrokerState.Initial => new PitchResult(new[]
        {
            "Captain, I've got a run that needs a steady hand.",
            "Nothing fancy. Cargo haul to a neighbour system, decent pay, fair turnaround.",
            "I've posted the request on the station board. Grab it if you're in.",
        }),
        BrokerState.Waiting => new PitchResult(new[]
        {
            "It's still up on the board, Captain.",
            "Take a look when you get a moment.",
        }),
        BrokerState.InProgress => new PitchResult(new[]
        {
            "Still working on that run?",
            "Come back when it's done and we'll settle up.",
        }),
        BrokerState.ReadyToClaim => new PitchResult(new[]
        {
            "Ready to report in, Captain?",
            "Head over to the mission board and wrap it up.",
        }),
        BrokerState.Done => new PitchResult(new[]
        {
            "Thanks for the work, Captain.",
            "Safe travels out there.",
        }),
        _ => new PitchResult(new[] { "Captain." }),
    };
}
