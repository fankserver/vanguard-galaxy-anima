namespace VGAnima.Pitch;

internal interface IPitchProvider
{
    PitchResult Pitch(PatronContext ctx);
}
