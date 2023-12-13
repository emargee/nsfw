using LibHac.Fs;

namespace Nsfw.Commands;

public class RawContentFileInfo
{
    public string Name { get; set; } = string.Empty;
    public string FullPath { get; set; } = string.Empty;
    public long Size { get; set; }
    public DirectoryEntryType Type { get; set; }
    public string DisplaySize => Size.BytesToHumanReadable();
    public int BlockCount { get; set; }
}