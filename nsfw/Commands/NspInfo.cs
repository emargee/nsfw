using System.Globalization;
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
    public static readonly string Unknown = "UNKNOWN";
    public static readonly string Empty = "EMPTY";
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
    public bool HasLanguages => Titles.Count > 0;
    public IEnumerable<string> LanguagesFull => Titles.Keys.Select(x => x switch
    {
        NacpLanguage.AmericanEnglish => "English (America)",
        NacpLanguage.BritishEnglish => "English (Great Britain)",
        NacpLanguage.Japanese => "Japanese",
        NacpLanguage.French => "French (France)",
        NacpLanguage.CanadianFrench => "French (Canada)",
        NacpLanguage.German => "German",
        NacpLanguage.Italian => "Italian",
        NacpLanguage.Spanish => "Spanish (Spain)",
        NacpLanguage.LatinAmericanSpanish => "Spanish (Latin America)",
        NacpLanguage.SimplifiedChinese => "Chinese (Simplified)",
        NacpLanguage.TraditionalChinese => "Chinese (Traditional)",
        NacpLanguage.Korean => "Korean",
        NacpLanguage.Dutch => "Dutch",
        NacpLanguage.Portuguese => "Portuguese (Portugal)",
        NacpLanguage.BrazilianPortuguese => "Portuguese (Brazil)",
        NacpLanguage.Russian => "Russian",
        _ => "Unknown"
    });
    public IEnumerable<string> LanguagesFullShort => Titles.Keys.Select(x => x switch
    {

            NacpLanguage.AmericanEnglish => "En-US",
            NacpLanguage.BritishEnglish => "En-GB",
            NacpLanguage.Japanese => "Ja",
            NacpLanguage.French => "Fr-FR",
            NacpLanguage.CanadianFrench => "Fr-CA",
            NacpLanguage.German => "De",
            NacpLanguage.Italian => "It",
            NacpLanguage.Spanish => "Es-ES",
            NacpLanguage.LatinAmericanSpanish => "Es-XL",
            NacpLanguage.SimplifiedChinese => "Zh-Hans",
            NacpLanguage.TraditionalChinese => "Zh-Hant",
            NacpLanguage.Korean => "Ko",
            NacpLanguage.Dutch => "Nl",
            NacpLanguage.Portuguese => "Pt-pt",
            NacpLanguage.BrazilianPortuguese => "Pt-BR",
            NacpLanguage.Russian => "Ru",
            _ => "Unknown"
    });
    public IEnumerable<string> LanguagesShort => Titles.Keys.Select(x => x switch
    {
        NacpLanguage.AmericanEnglish => "En",
        NacpLanguage.BritishEnglish => "En",
        NacpLanguage.Japanese => "Ja",
        NacpLanguage.French => "Fr",
        NacpLanguage.CanadianFrench => "Fr",
        NacpLanguage.German => "De",
        NacpLanguage.Italian => "It",
        NacpLanguage.Spanish => "Es",
        NacpLanguage.LatinAmericanSpanish => "Es",
        NacpLanguage.SimplifiedChinese => "Zh-Hans",
        NacpLanguage.TraditionalChinese => "Zh-Hant",
        NacpLanguage.Korean => "Ko",
        NacpLanguage.Dutch => "Nl",
        NacpLanguage.Portuguese => "Pt",
        NacpLanguage.BrazilianPortuguese => "Pt",
        NacpLanguage.Russian => "Ru",
        _ => "Unknown"
    }).Distinct();
    public HashSet<string> Warnings { get; set; } = [];
    public HashSet<string> Errors { get; set; } = [];
    public bool HasWarnings => Warnings.Count > 0;
    public bool HasErrors => Errors.Count > 0;
    public bool CanProceed { get; set; } = true;
    public bool PossibleDlcUnlocker { get; set; }
    public bool GenerateNewTicket { get; set; }
    public Dictionary<string, RawContentFile> RawFileEntries { get; set; } = [];
    public bool HasTicket => Ticket != null;
    public string HeaderMagic { get; } = NspHeaderMagic;
    public string BaseTitleId { get; set; } = Unknown;
    public string TitleId { get; set; } = Unknown;
    public bool UseBaseTitleId => BaseTitleId != TitleId;
    public bool HasTitleKeyCrypto { get; set; }
    public Validity HeaderSignatureValidity => NcaFiles.Any(x => !x.Value.IsHeaderValid) ? Validity.Invalid : Validity.Valid;
    public Validity NcaValidity => NcaFiles.Any(x => x.Value.IsErrored) ? Validity.Invalid : Validity.Valid;
    public Validity ContentValidity => ContentFiles.Any(x => x.Value.SizeMismatch) ? Validity.Invalid : Validity.Valid;
    public byte[] TitleKeyEncrypted { get; set; } = Array.Empty<byte>();
    public byte[] TitleKeyDecrypted { get; set; } = Array.Empty<byte>();
    public bool IsTicketSignatureValid { get; set; }
    public bool IsNormalisedSignature { get; set; }
    public int MasterKeyRevision { get; set; }
    public string TitleVersion { get; set; } = Unknown;
    public ContentMetaType TitleType { get; set; }
    public string DisplayType => TitleType switch
    {
        ContentMetaType.SystemProgram => "System Program",
        ContentMetaType.SystemData => "System Data",
        ContentMetaType.SystemUpdate => "System Update",
        ContentMetaType.BootImagePackage => "Boot Image Package",
        ContentMetaType.Application => "Game",
        ContentMetaType.Patch => "Update",
        ContentMetaType.AddOnContent => "DLC",
        ContentMetaType.Delta => "DLC Update",
        _ => Unknown
    };
    public string DisplayTypeShort => TitleType switch
    {
        ContentMetaType.Application => "BASE",
        ContentMetaType.Patch => "UPD",
        ContentMetaType.AddOnContent => "DLC",
        ContentMetaType.Delta => "DLCUPD",
        _ => Unknown
    };
    
    public Dictionary<string, ContentFile> ContentFiles { get; set; } = [];
    public Dictionary<string, NcaInfo> NcaFiles { get; set; } = [];
    public string MinimumApplicationVersion { get; set; } = string.Empty;
    public string MinimumSystemVersion { get; set; } = string.Empty;
    public Dictionary<NacpLanguage, TitleInfo> Titles { get; set; } = [];
    public string ControlTitle => Titles.Count > 0 ? Titles[0].Title : Unknown;
    public string DisplayTitle { get; set; } = Unknown;
    public Source DisplayTitleSource { get; set; } = Source.Unknown;
    public string DisplayVersion { get; set; } = Unknown;
    public string? DisplayParentTitle { get; set; }
    public LogLevel LogLevel { get; set; }
    public bool IsLogFull => LogLevel == LogLevel.Full;
    public bool IsLogCompact => LogLevel == LogLevel.Compact;
    public bool IsLogQuiet => LogLevel == LogLevel.Quiet;
    public string RightsId { get; set; } = Empty;
    public string OutputName => BuildOutputName();
    public string DisplayParentLanguages { get; set; } = Unknown;
    public IEnumerable<NacpLanguage> ParentLanguages { get; set; } = [];

    public NspInfo(string filePath)
    {
        if (!File.Exists(filePath))
        {
            throw new FileNotFoundException("File not found", filePath);
        }
        FilePath = filePath;
        FileSystemType = FileSystemType.Unknown;
    }

    private Region GetRegion(NacpLanguage[] titles, ref string languageList)
    {
        var region = Region.Unknown;

        if (titles.Length == 0)
        {
            return region;
        }
        
        if(titles is [NacpLanguage.AmericanEnglish])
        {
            region = Region.USA;
            languageList = string.Empty;
        }
        
        if(titles is [NacpLanguage.Japanese])
        {
            region = Region.Japan;
            languageList = string.Empty;
        }
        
        if(titles is [NacpLanguage.Korean])
        {
            region = Region.Korea;
            languageList = string.Empty;
        }
        
        if(titles is [NacpLanguage.Russian])
        {
            region = Region.Russia;
            languageList = string.Empty;
        }
        
        if(titles is [NacpLanguage.TraditionalChinese])
        {
            region = Region.China;
            languageList = string.Empty;
        }
        
        if(titles is [NacpLanguage.SimplifiedChinese])
        {
            region = Region.China;
            languageList = string.Empty;
        }
        
        if (titles.Any(x => x is NacpLanguage.TraditionalChinese or NacpLanguage.SimplifiedChinese))
        {
            region = Region.China;
            
            if(titles.Any(x => x is NacpLanguage.Japanese or NacpLanguage.Korean))
            {
                region = Region.Asia;
                
                if (titles.Contains(NacpLanguage.AmericanEnglish) || Titles.ContainsKey(NacpLanguage.BritishEnglish))
                {
                    region = Region.World;
                }
            }
        }
        
        if (titles.Any(x => x is NacpLanguage.BritishEnglish or NacpLanguage.French or NacpLanguage.German or NacpLanguage.Italian or NacpLanguage.Spanish or NacpLanguage.Dutch or NacpLanguage.Portuguese))
        {
            region = Region.Europe;
            
            if(titles.Any(x => x is NacpLanguage.AmericanEnglish or NacpLanguage.Japanese or NacpLanguage.Korean or NacpLanguage.Russian or NacpLanguage.TraditionalChinese or NacpLanguage.SimplifiedChinese or NacpLanguage.LatinAmericanSpanish or NacpLanguage.BrazilianPortuguese or NacpLanguage.CanadianFrench))
            {
                region = Region.World;
            }
        }
        
        if(titles.Any(x => x is NacpLanguage.AmericanEnglish))
        {
            region = Region.USA;
            
            if(titles.Any(x => x is NacpLanguage.Japanese or NacpLanguage.Korean or NacpLanguage.Russian or NacpLanguage.TraditionalChinese or NacpLanguage.SimplifiedChinese or NacpLanguage.LatinAmericanSpanish or NacpLanguage.BrazilianPortuguese or NacpLanguage.CanadianFrench))
            {
                region = Region.World;
            }
        }

        return region;
    }

    public string BuildOutputName()
    {
        var languageList = string.Empty;

        if (OutputOptions.LanguageMode == LanguageMode.Full)
        {
            languageList = string.Join(",", LanguagesFullShort);
        }

        if (OutputOptions.LanguageMode == LanguageMode.Short)
        {
            languageList = string.Join(",", LanguagesShort);
        }
        
        if(OutputOptions.LanguageMode == LanguageMode.None)
        {
            languageList = string.Empty;
        }
        
        if (!string.IsNullOrEmpty(languageList))
        {
            languageList = $"({languageList})";
        }
        
        var region = GetRegion(Titles.Keys.ToArray(), ref languageList);
        var displayRegion = OutputOptions.LanguageMode != LanguageMode.None ? $"({region})" : string.Empty;
        
        var cleanTitle = DisplayTitle.CleanTitle();
        var cleanParentTitle = DisplayParentTitle?.CleanTitle();
        
        var textInfo = new CultureInfo("en-US", false).TextInfo;
        cleanTitle = textInfo.ToTitleCase(cleanTitle);
        cleanParentTitle = textInfo.ToTitleCase(cleanParentTitle ?? string.Empty);
        
        if (TitleType is ContentMetaType.Patch or ContentMetaType.Delta)
        {
            return $"{cleanTitle} {displayRegion}{languageList}[{DisplayVersion}][{TitleId}][{TitleVersion}][{DisplayTypeShort}]".CleanTitle();
        }
        
        if (TitleType is ContentMetaType.AddOnContent  && !string.IsNullOrEmpty(DisplayParentTitle))
        {
            if (ParentLanguages.Any())
            {
                languageList = DisplayParentLanguages;
                region = GetRegion(ParentLanguages.ToArray(), ref languageList);
                displayRegion = OutputOptions.LanguageMode != LanguageMode.None ? $"({region})" : string.Empty;
                
                if (!string.IsNullOrEmpty(languageList))
                {
                    languageList = $"({languageList})";
                }
            }
            
            if(cleanTitle.Contains(cleanParentTitle, StringComparison.InvariantCultureIgnoreCase))
            {
                return $"{cleanTitle} {displayRegion}{languageList}[{TitleId}][{TitleVersion}][{DisplayTypeShort}]".CleanTitle();
            }
            
            var parentParts = cleanParentTitle.Split(" - ", StringSplitOptions.TrimEntries);
            cleanTitle = parentParts.Aggregate(cleanTitle, (current, part) => current.Replace(part, string.Empty, StringComparison.InvariantCultureIgnoreCase));
        
            var formattedTitle = $"{cleanParentTitle} - {cleanTitle} {displayRegion}{languageList}[{TitleId}][{TitleVersion}][{DisplayTypeShort}]";
               
            return formattedTitle.CleanTitle();
        }
        
        return $"{cleanTitle} {displayRegion}{languageList}[{TitleId}][{TitleVersion}][{DisplayTypeShort}]".CleanTitle();
    }
}

public enum Region
{
    World,
    Europe,
    Asia,
    Japan,
    Korea,
    China,
    Russia,
    // ReSharper disable once InconsistentNaming
    USA,
    Unknown
}

public class OutputOptions
{
    public LanguageMode LanguageMode { get; set; } = LanguageMode.Full;
    public bool IsTitleDbAvailable { get; set; }
    public string TitleDbPath { get; set; } = string.Empty;
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
    public string FullPath { get; set; } = string.Empty;
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
    public bool SizeMismatch { get; set; }
}

public class NcaInfo(string ncaFilename)
{
    public string FileName { get; init; } = ncaFilename;
    public Dictionary<int, NcaSectionInfo> Sections { get; set; } = [];
    public bool IsHeaderValid { get; set; }
    public bool IsErrored => Sections.Any(x => x.Value.IsErrored) || !IsHeaderValid;
    public NcaContentType Type { get; set; }
    public HashMatchType HashMatch { get; set; } = HashMatchType.Missing;
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

public enum HashMatchType
{
    Missing,
    Match,
    Mismatch
}

public enum Source
{
    Unknown,
    Control,
    FileName,
    TitleDb    
}

[Flags]
public enum NacpLanguage : uint
{
    AmericanEnglish = 0,
    BritishEnglish = 1,
    Japanese = 2,
    French = 3,
    German = 4,
    LatinAmericanSpanish = 5,
    Spanish = 6,
    Italian = 7,
    Dutch = 8,
    CanadianFrench = 9,
    Portuguese = 10,
    Russian = 11,
    Korean = 12,
    TraditionalChinese = 13,
    SimplifiedChinese = 14,
    BrazilianPortuguese = 15,
}

public record TitleInfo
{
    public string Title { get; init; } = string.Empty;
    public string Publisher { get; init; } = string.Empty;
    public NacpLanguage RegionLanguage { get; init; }
}
