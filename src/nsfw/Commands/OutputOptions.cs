namespace Nsfw.Commands;

public class OutputOptions
{
    public LanguageMode LanguageMode { get; set; } = LanguageMode.Full;
    public bool IsTitleDbAvailable { get; set; }
    public bool KeepName { get; set; }
}