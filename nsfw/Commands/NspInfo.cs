using LibHac.Common;
using LibHac.Fs;
using LibHac.Ncm;
using LibHac.Tools.Es;
using LibHac.Tools.FsSystem.NcaUtils;
using ContentType = LibHac.Ncm.ContentType;
using Path = System.IO.Path;

namespace Nsfw.Commands;

public class NspInfo
{
    private const string NspHeaderMagic = "50465330";
    private const string Unknown = "UNKNOWN";
    public readonly byte[] NormalisedSignature = Enumerable.Repeat((byte)0xFF, 0x100).ToArray();
    public readonly int DefaultBlockSize = 0x4000;
    public Ticket? Ticket { get; set; }
    public OutputOptions OutputOptions { get; set; } = new();
    public FileSystemType FileSystemType { get; set; } = FileSystemType.Unknown;
    public string FilePath { get; set; }
    public string FileName => Path.GetFileName(FilePath);
    public string FileNameWithoutExtension => Path.GetFileNameWithoutExtension(FilePath);
    public string FileExtension => Path.GetExtension(FilePath);
    public string DirectoryName => Path.GetDirectoryName(FilePath)!;
    
    public List<string> Languages { get; set; } = [];
    public HashSet<string> Warnings { get; set; } = [];
    public HashSet<string> Notices { get; set; } = [];
    public HashSet<string> Errors { get; set; } = [];
    public bool CanExtract { get; set; } = true;
    public bool CanConvert { get; set; } = true;
    public bool PossibleDlcUnlocker { get; set; }
    public bool GenerateNewTicket { get; set; }
    public Dictionary<string, RawContentFile> RawFileEntries { get; set; } = [];
    
    public bool HasTicket => Ticket != null;
    public string HeaderMagic { get; } = NspHeaderMagic;
    public string BaseTitleId { get; set; } = Unknown;
    public string TitleId { get; set; } = Unknown;
    public bool HasTitleKeyCrypto { get; set; }
    public Validity HeaderSignatureValidity => NcaFiles.Any(x => x.Value.IsErrored) ? Validity.Invalid : Validity.Valid;
    public byte[] TitleKeyEncrypted { get; set; } = Array.Empty<byte>();
    public byte[] TitleKeyDecrypted { get; set; } = Array.Empty<byte>();
    public bool IsTicketSignatureValid { get; set; }
    public bool IsNormalisedSignature { get; set; }
    public int MasterKeyRevision { get; set; }
    public string TitleVersion { get; set; } = Unknown;
    public ContentMetaType TitleType { get; set; }
    public Dictionary<string, ContentFile> ContentFiles { get; set; } = [];
    public Dictionary<string, NcaInfo> NcaFiles { get; set; } = [];

    public NspInfo(string filePath)
    {
        if (!File.Exists(filePath))
        {
            throw new FileNotFoundException("File not found", filePath);
        }
        FilePath = filePath;
        FileSystemType = FileSystemType.Unknown;
    }
}

public class OutputOptions
{
    public LanguageMode LanguageMode { get; set; } = LanguageMode.Full;
}

public enum FileSystemType
{
    Sha256Partition,
    Partition,
    Unknown
}

public class RawContentFile
{
    public string Name { get; set; } = string.Empty;
    public long Size { get; set; }
    public DirectoryEntryType Type { get; set; }
    public string DisplaySize => Size.BytesToHumanReadable();
    public int BlockCount { get; set; }
}

public class ContentFile
{
    public string FileName { get; set; } = string.Empty;
    public byte[] Hash { get; set; } = Array.Empty<byte>();
    public string NcaId { get; set; } = string.Empty;
    public bool IsMissing { get; set; }
    public ContentType Type { get; set; }
}

public class NcaInfo(string ncaFilename)
{
    public string FileName { get; init; } = ncaFilename;
    public Dictionary<int, NcaSectionInfo> Sections { get; set; } = [];
    public bool IsHeaderValid { get; set; }
    public bool IsErrored => Sections.Any(x => x.Value.IsErrored) || !IsHeaderValid;
    public NcaContentType Type { get; set; }
    public bool HashMatch { get; set; }
}

public class NcaSectionInfo(int sectionId)
{
    public int SectionId { get; set; } = sectionId;
    public bool IsPatchSection { get; set; }
    public bool IsErrored { get; set; }
    public string ErrorMessage { get; set; } = string.Empty;
    public NcaEncryptionType EncryptionType { get; set; }
    public NcaFormatType FormatType { get; set; }
}
