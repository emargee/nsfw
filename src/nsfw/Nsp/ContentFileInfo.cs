using LibHac.Ncm;

namespace Nsfw.Nsp;

public class ContentFileInfo
{
    public string FileName { get; set; } = string.Empty;
    public byte[] Hash { get; set; } = Array.Empty<byte>();
    public string NcaId { get; set; } = string.Empty;
    public bool IsMissing { get; set; }
    public ContentType Type { get; set; }
    public bool SizeMismatch { get; set; }
    public bool IsCompressed { get; set; }
    public string CompressedFileName { get; set; } = string.Empty;
}