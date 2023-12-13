using LibHac.Tools.FsSystem.NcaUtils;

namespace Nsfw.Commands;

public class NcaInfo(string ncaFilename)
{
    public string FileName { get; init; } = ncaFilename;
    public Dictionary<int, NcaSectionInfo> Sections { get; set; } = [];
    public bool IsHeaderValid { get; set; }
    public bool IsErrored => Sections.Any(x => x.Value.IsErrored) || !IsHeaderValid;
    public NcaContentType Type { get; set; }
    public HashMatchType HashMatch { get; set; } = HashMatchType.Missing;
}