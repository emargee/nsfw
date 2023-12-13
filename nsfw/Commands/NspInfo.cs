using System.Globalization;
using LibHac.Common;
using LibHac.Ncm;
using LibHac.Tools.Es;
using Microsoft.VisualBasic;
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
    public Dictionary<string, RawContentFileInfo> RawFileEntries { get; set; } = [];
    public bool HasTicket => Ticket != null;
    public string HeaderMagic { get; } = NspHeaderMagic;
    public string BaseTitleId { get; set; } = Unknown;
    public string TitleId { get; set; } = Unknown;
    public bool UseBaseTitleId => BaseTitleId != TitleId;
    public bool HasTitleKeyCrypto { get; set; }

    public Validity HeaderSignatureValidity =>
        NcaFiles.Any(x => !x.Value.IsHeaderValid) ? Validity.Invalid : Validity.Valid;

    public Validity NcaValidity => NcaFiles.Any(x => x.Value.IsErrored) ? Validity.Invalid : Validity.Valid;
    public Validity ContentValidity => ContentFiles.Any(x => x.Value.SizeMismatch) ? Validity.Invalid : Validity.Valid;
    public byte[] TitleKeyEncrypted { get; set; } = Array.Empty<byte>();
    public byte[] TitleKeyDecrypted { get; set; } = Array.Empty<byte>();
    public bool IsTicketSignatureValid { get; set; }
    public bool IsNormalisedSignature { get; set; }
    public int MasterKeyRevision { get; set; }
    public string TitleVersion { get; set; } = Unknown;
    public FixedContentMetaType TitleType { get; set; }
    // ReSharper disable once InconsistentNaming
    public bool IsDLC => TitleType is FixedContentMetaType.AddOnContent or FixedContentMetaType.DataPatch;

    public string DisplayType => TitleType switch
    {
        FixedContentMetaType.SystemProgram => "System Program",
        FixedContentMetaType.SystemData => "System Data",
        FixedContentMetaType.SystemUpdate => "System Update",
        FixedContentMetaType.BootImagePackage => "Boot Image Package",
        FixedContentMetaType.Application => "Game",
        FixedContentMetaType.Patch => "Update",
        FixedContentMetaType.AddOnContent => "DLC",
        FixedContentMetaType.Delta => "Delta",
        FixedContentMetaType.DataPatch => "DLC Update",
        _ => Unknown
    };

    public string DisplayTypeShort => TitleType switch
    {
        FixedContentMetaType.Application => "BASE",
        FixedContentMetaType.Patch => "UPD",
        FixedContentMetaType.AddOnContent => "DLC",
        FixedContentMetaType.DataPatch => "DLCUPD",
        _ => Unknown
    };

    public string DisplayKeyGeneration => KeyGeneration switch
    {
        KeyGeneration.U10 => "1.0.0 <-> 2.3.0",
        KeyGeneration.U30 => "3.0.0",
        KeyGeneration.U301 => "3.0.1 <-> 3.0.2",
        KeyGeneration.U40 => "4.0.0 <-> 4.1.0",
        KeyGeneration.U50 => "5.0.0 <- 5.1.0",
        KeyGeneration.U60 => "6.0.0 <-> 6.1.0",
        KeyGeneration.U62 => "6.2.0",
        KeyGeneration.U70 => "7.0.0 <-> 8.0.1",
        KeyGeneration.U81 => "8.1.0 <-> 8.1.1",
        KeyGeneration.U90 => "9.0.0 <-> 9.0.1",
        KeyGeneration.U91 => "9.1.0 <-> 12.0.3",
        KeyGeneration.U121 => "12.1.0",
        KeyGeneration.U130 => "13.0.0 <-> 13.2.1",
        KeyGeneration.U140 => "14.0.0 <-> 14.1.2",
        KeyGeneration.U150 => "15.0.0 <-> 15.0.1",
        KeyGeneration.U160 => "16.0.0 <-> 16.1.0",
        KeyGeneration.U170 => "17.0.0+",
        _ => Unknown
    };

    public Dictionary<string, ContentFileInfo> ContentFiles { get; set; } = [];
    public Dictionary<string, NcaInfo> NcaFiles { get; set; } = [];
    public string MinimumApplicationVersion { get; set; } = string.Empty;
    public string MinimumSystemVersion { get; set; } = string.Empty;
    public Dictionary<NacpLanguage, TitleInfo> Titles { get; set; } = [];
    public string ControlTitle => Titles.Count > 0 ? Titles.Values.First().Title : Unknown;
    public string DisplayTitle { get; set; } = Unknown;
    public LookupSource DisplayTitleLookupSource { get; set; } = LookupSource.Unknown;
    public string DisplayVersion { get; set; } = Unknown;
    public string? DisplayParentTitle { get; set; }
    public string RightsId { get; set; } = Empty;
    public string OutputName => BuildOutputName();
    public string DisplayParentLanguages { get; set; } = Unknown;
    public IEnumerable<NacpLanguage> ParentLanguages { get; set; } = [];
    public int DeltaCount { get; set; }
    public KeyGeneration KeyGeneration { get; set; }

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

        if (titles is [NacpLanguage.AmericanEnglish])
        {
            region = Region.USA;
            languageList = string.Empty;
        }

        if (titles is [NacpLanguage.Japanese])
        {
            region = Region.Japan;
            languageList = string.Empty;
        }

        if (titles is [NacpLanguage.Korean])
        {
            region = Region.Korea;
            languageList = string.Empty;
        }

        if (titles is [NacpLanguage.Russian])
        {
            region = Region.Russia;
            languageList = string.Empty;
        }

        if (titles is [NacpLanguage.TraditionalChinese])
        {
            region = Region.China;
            languageList = string.Empty;
        }

        if (titles is [NacpLanguage.SimplifiedChinese])
        {
            region = Region.China;
            languageList = string.Empty;
        }

        if (titles.Any(x => x is NacpLanguage.TraditionalChinese or NacpLanguage.SimplifiedChinese))
        {
            region = Region.China;

            if (titles.Any(x => x is NacpLanguage.Japanese or NacpLanguage.Korean))
            {
                region = Region.Asia;

                if (titles.Contains(NacpLanguage.AmericanEnglish) || Titles.ContainsKey(NacpLanguage.BritishEnglish))
                {
                    region = Region.World;
                }
            }
        }

        if (titles.Any(x => x is NacpLanguage.BritishEnglish or NacpLanguage.French or NacpLanguage.German
                or NacpLanguage.Italian or NacpLanguage.Spanish or NacpLanguage.Dutch or NacpLanguage.Portuguese))
        {
            region = Region.Europe;

            if (titles.Any(x => x is NacpLanguage.AmericanEnglish or NacpLanguage.Japanese or NacpLanguage.Korean
                    or NacpLanguage.Russian or NacpLanguage.TraditionalChinese or NacpLanguage.SimplifiedChinese
                    or NacpLanguage.LatinAmericanSpanish or NacpLanguage.BrazilianPortuguese
                    or NacpLanguage.CanadianFrench))
            {
                region = Region.World;
            }
        }

        if (titles.Any(x => x is NacpLanguage.AmericanEnglish))
        {
            region = Region.USA;

            if (titles.Any(x => x is NacpLanguage.Japanese or NacpLanguage.Korean or NacpLanguage.Russian
                    or NacpLanguage.TraditionalChinese or NacpLanguage.SimplifiedChinese
                    or NacpLanguage.LatinAmericanSpanish or NacpLanguage.BrazilianPortuguese
                    or NacpLanguage.CanadianFrench))
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

        if (OutputOptions.LanguageMode == LanguageMode.None)
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

        if (string.IsNullOrWhiteSpace(cleanParentTitle))
        {
            cleanParentTitle = string.Empty;
        }

        if (string.IsNullOrWhiteSpace(cleanTitle))
        {
            cleanTitle = string.Empty;
        }

        cleanTitle = cleanTitle.Replace(TitleId, String.Empty);
        if (cleanTitle.EndsWith("v0"))
        {
            cleanTitle = cleanTitle[..^2];
        }

        // cleanTitle = string.Join(' ', cleanTitle.Split(' ', StringSplitOptions.RemoveEmptyEntries)
        //     .Select(x => $"{char.ToUpper(x[0])}{x[1..]}"));
        //
        // cleanParentTitle = string.Join(' ', cleanParentTitle.Split(' ', StringSplitOptions.RemoveEmptyEntries)
        //     .Select(x => $"{char.ToUpper(x[0])}{x[1..]}"));
        //
        //var textInfo = new CultureInfo("en-US", false).TextInfo;
        //cleanTitle = textInfo.ToTitleCase(cleanTitle);
        //cleanParentTitle = textInfo.ToTitleCase(cleanParentTitle ?? string.Empty);

        if (TitleType is FixedContentMetaType.Patch)
        {
            return
                $"{cleanTitle} {displayRegion}{languageList}[{DisplayVersion}][{TitleId}][{TitleVersion}][{DisplayTypeShort}]"
                    .CleanTitle();
        }

        if (IsDLC && !string.IsNullOrEmpty(DisplayParentTitle))
        {
            if (ParentLanguages.Any())
            {
                languageList = DisplayParentLanguages;
                region = GetRegion(ParentLanguages.ToArray(), ref languageList);
                displayRegion = OutputOptions.LanguageMode != LanguageMode.None ? $"({region})" : string.Empty;

                if (OutputOptions.LanguageMode == LanguageMode.None)
                {
                    languageList = string.Empty;
                }

                if (!string.IsNullOrEmpty(languageList))
                {
                    languageList = languageList.Replace("Zh,Zh", "Zh-Hans,Zh-Hant");
                    languageList = $"({languageList})";
                }
            }

            var parentParts = cleanParentTitle.Split(" - ",
                StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            cleanTitle = parentParts.Aggregate(cleanTitle,
                    (current, part) => current.Replace(part, string.Empty, StringComparison.InvariantCultureIgnoreCase))
                .CleanTitle();

            var titleParts = cleanTitle.Split(" - ",
                StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

            foreach (var titlePart in titleParts)
            {
                if (cleanParentTitle.Contains(titlePart, StringComparison.InvariantCultureIgnoreCase))
                {
                    cleanTitle = cleanTitle
                        ?.Replace(titlePart, string.Empty, StringComparison.InvariantCultureIgnoreCase).CleanTitle();
                }
            }

            var finalTitle = $"{cleanParentTitle} - {cleanTitle} {displayRegion}{languageList}".CleanTitle();

            if (PossibleDlcUnlocker)
            {
                finalTitle = $"{cleanParentTitle} - DLC Unlocker (Homebrew){displayRegion}{languageList}".CleanTitle();
            }

            var formattedTitle = $"{finalTitle} [{TitleId}][{TitleVersion}][{DisplayTypeShort}]";

            return formattedTitle.CleanTitle();
        }

        return $"{cleanTitle} {displayRegion}{languageList}[{TitleId}][{TitleVersion}][{DisplayTypeShort}]"
            .CleanTitle();
    }
}