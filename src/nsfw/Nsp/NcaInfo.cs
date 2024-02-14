using LibHac.Tools.Fs;
using LibHac.Tools.FsSystem.NcaUtils;

namespace Nsfw.Nsp;

public class NcaInfo(SwitchFsNca nca)
{
    public SwitchFsNca FsNca { get; init; } = nca;
    public string FileName { get; init; } = nca.Filename;
    public Dictionary<int, NcaSectionInfo> Sections { get; set; } = [];
    public bool IsHeaderValid { get; set; }
    public bool IsNpdmValid { get; set; }
    public bool IsErrored => Sections.Any(x => x.Value.IsErrored) || !IsHeaderValid;
    public NcaContentType Type { get; set; }
    public HashMatchType HashMatch { get; set; } = HashMatchType.Missing;
    public string[] EncryptedKeys { get; set; } = [];
    public byte[] RawHeader { get; set; } = [];
    public int EncryptionKeyIndex { get; set; }
    public string[] DecryptedKeys { get; set; } = [];
}