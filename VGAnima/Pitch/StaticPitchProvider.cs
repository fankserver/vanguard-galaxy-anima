namespace VGAnima.Pitch;

/// <summary>Static templated pitch lines — no network, no LLM. Text is
/// intentionally ASCII-only: the game's pixel16 font drops em-dashes and
/// smart quotes to spaces. Lines are Captain-addressed to match vanilla
/// side-mission dialogue tone.
///
/// Line-count shape per state:
///   <list type="bullet">
///     <item><c>Initial</c> — 3-5 lines (full pitch)</item>
///     <item><c>InProgress</c> — 1-2 lines (check-in)</item>
///     <item><c>ReadyToClaim</c> — 3-4 lines (congrats + payout cue)</item>
///     <item><c>Done</c> — 1-2 lines (farewell)</item>
///   </list></summary>
internal sealed class StaticPitchProvider : IPitchProvider
{
    public PitchResult Pitch(PatronContext ctx) => PitchForState(ctx, BrokerState.Initial);

    public PitchResult PitchForState(PatronContext ctx, BrokerState state) => state switch
    {
        BrokerState.Initial => new PitchResult(new[]
        {
            "Captain, got a moment? I've got a job that needs a steady hand.",
            "Nothing fancy. Clean work, fair pay, you'll be back before the kettle whistles.",
            "Details are in the brief. If you're in, it's yours.",
        }),
        BrokerState.InProgress => new PitchResult(new[]
        {
            "Still working on that job, Captain?",
            "Come find me when it's wrapped and we'll settle up.",
        }),
        BrokerState.ReadyToClaim => new PitchResult(new[]
        {
            "There you are, Captain. Good work out there.",
            "Clean finish, exactly how I like it.",
            "Here's your pay. Every last credit you were owed.",
            "Safe flying, Captain.",
        }),
        BrokerState.Done => new PitchResult(new[]
        {
            "Thanks again for the work, Captain.",
            "Nothing more for you here today.",
        }),
        _ => new PitchResult(new[] { "Captain." }),
    };
}
