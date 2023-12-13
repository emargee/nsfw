namespace Nsfw.Commands;

public record TitleInfo
{
    public string Title { get; init; } = string.Empty;
    public string Publisher { get; init; } = string.Empty;
    public NacpLanguage RegionLanguage { get; init; }
}