using LibHac.Tools.FsSystem.NcaUtils;

namespace Nsfw.Commands;

public class NcaSectionInfo(int sectionId)
{
    public int SectionId { get; set; } = sectionId;
    public bool IsPatchSection { get; set; }
    public bool IsErrored { get; set; }
    public string ErrorMessage { get; set; } = string.Empty;
    public NcaEncryptionType EncryptionType { get; set; }
    public NcaFormatType FormatType { get; set; }
}