using System.Numerics;
using System.Security.Cryptography;
using System.Text.RegularExpressions;
using Diacritics.Extensions;
using LibHac.Common;
using LibHac.Fs;
using LibHac.Fs.Fsa;
using LibHac.Tools.Es;
using LibHac.Tools.Fs;
using LibHac.Tools.FsSystem;
using LibHac.Tools.FsSystem.NcaUtils;
using LibHac.Util;
using Nsfw.Commands;
using Spectre.Console;
using SQLite;
using HierarchicalIntegrityVerificationStorage = LibHac.Tools.FsSystem.HierarchicalIntegrityVerificationStorage;

namespace Nsfw.Nsp;

public static partial class NsfwUtilities
{
    public static byte[] FixedSignature { get; } = Enumerable.Repeat((byte)0xFF, 0x100).ToArray();
    public const int CommonCertSize = 0x700;
    public const string CommonCertSha256 = "3c4f20dca231655e90c75b3e9689e4dd38135401029ab1f2ea32d1c2573f1dfe";

    public static bool ValidateTicket(Ticket ticket, string certPath)
    {
        using var fileStream = new FileStream(certPath, FileMode.Open);
        fileStream.Seek(1480, SeekOrigin.Begin);

        var modulusBytes = new byte[256];
        var pubExpBytes = new byte[4];
        fileStream.Read(modulusBytes, 0, modulusBytes.Length);
        fileStream.Read(pubExpBytes, 0, pubExpBytes.Length);

        var modulus = new BigInteger(modulusBytes, true, true);
        var pubExp = new BigInteger(pubExpBytes, true, true);

        using var pubKey = RSA.Create();
        pubKey.ImportParameters(new RSAParameters
        {
            Modulus = modulus.ToByteArray(true, true),
            Exponent = pubExp.ToByteArray(true, true)
        });

        var message = ticket.File.Skip(0x140).ToArray();

        try
        {
            // Verify ticket signature.
            return pubKey.VerifyData(message, ticket.Signature, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        }
        catch (CryptographicException)
        {
            // Invalid signature.
            return false;
        }
    }

    public static string CleanTitle(this string title)
    {
        var cleanTitle = title
            .ReplaceLineEndings("")
            .Replace("  ", " ")
            .Replace("\u3000", " ")
            .Replace(" <", " - ")
            .Replace(">", string.Empty)
            .Replace("–","-")
            .Replace("—","-")
            .Replace("“", "'")
            .Replace("*", "")
            .Replace("”", "'")
            .Replace('/', '-')
            .Replace("①","1")
            .Replace("②","2")
            .Replace("③","3")
            .Replace("④","4")
            .Replace("⑤","5")
            .Replace("⑥","6")
            .Replace("⑦","7")
            .Replace("⑧","8")
            .Replace("⑨","9")
            .Replace("⑩","10")
            .Replace("⑪","11")
            .Replace("⑫","12")
            .Replace("⑬","13")
            .Replace("⑭","14")
            .Replace("⑮","15")
            .Replace("⑯","16")
            .Replace("⑰","17")
            .Replace("⑱","18")
            .Replace("⑲","19")
            .Replace("⑳","20")
            .Replace("１","1")
            .Replace("２","2")
            .Replace("３","3")
            .Replace("４","4")
            .Replace("５","5")
            .Replace("６","6")
            .Replace("７","7")
            .Replace("８","8")
            .Replace("９","9")
            .Replace("０","0")
            .Replace("Ａ","A")
            .Replace("Ｂ","B")
            .Replace("Ｃ","C")
            .Replace("Ｄ","D")
            .Replace("Ｅ","E")
            .Replace("Ｆ","F")
            .Replace("Ｇ","G")
            .Replace("Ｈ","H")
            .Replace("Ｉ","I")
            .Replace("Ｊ","J")
            .Replace("Ｋ","K")
            .Replace("Ｌ","L")
            .Replace("Ｍ","M")
            .Replace("Ｎ","N")
            .Replace("Ｏ","O")
            .Replace("Ｐ","P")
            .Replace("Ｑ","Q")
            .Replace("Ｒ","R")
            .Replace("Ｓ","S")
            .Replace("Ｔ","T")
            .Replace("Ｕ","U")
            .Replace("Ｖ","V")
            .Replace("Ｗ","W")
            .Replace("Ｘ","X")
            .Replace("Ｙ","Y")
            .Replace("Ｚ","Z")
            .Replace("ａ","a")
            .Replace("ｂ","b")
            .Replace("ｃ","c")
            .Replace("ｄ","d")
            .Replace("ｅ","e")
            .Replace("ｆ","f")
            .Replace("ｇ","g")
            .Replace("ｈ","h")
            .Replace("ｉ","i")
            .Replace("ｊ","j")
            .Replace("ｋ","k")
            .Replace("ｌ","l")
            .Replace("ｍ","m")
            .Replace("ｎ","n")
            .Replace("ｏ","o")
            .Replace("ｐ","p")
            .Replace("ｑ","q")
            .Replace("ｒ","r")
            .Replace("ｓ","s")
            .Replace("ｔ","t")
            .Replace("ｕ","u")
            .Replace("ｖ","v")
            .Replace("ｗ","w")
            .Replace("ｘ","x")
            .Replace("ｙ","y")
            .Replace("ｚ","z")
            .Replace("×"," x ")
            .Replace("™","")
            .Replace("®","")
            .Replace("©","")
            .Replace("！", "!")
            .Replace("？", "?")
            .Replace("：", ":")
            .Replace("；", ";")
            .Replace("，", ",")
            .Replace("、", ",")
            .Replace("。", ".")
            .Replace("「", "'")
            .Replace("」", "'")
            .Replace('︰',':')
            .Replace("\uff5e", "-")
            .Replace(":||","-")
            .Replace(": ", " - ")
            .Replace(":", "-")
            .Replace(" ~", " - ")
            .Replace("~ ", " - ")
            .Replace(" - - ", " - ")
            .Replace("- - ", " - ")
            .Replace(" -  - ", " - ")
            .Replace(" - ：", " - ")
            .Replace("|", string.Empty)
            .Replace("\"", string.Empty)
            .Replace("\u2122", string.Empty)
            .Replace("\u00ae", string.Empty)
            .Replace("\u00a9", string.Empty)
            .Replace("!?", string.Empty)
            .Replace("?", string.Empty)
            .Replace(" - - ", " - ")
            .Replace("（", " (")
            .Replace("）", ") ")
            .Replace("） (", ") (")
            .Replace(" dlc", " DLC")
            .Replace("((((", "(")
            .Replace("))))", ")")
            .Replace("(((", "(")
            .Replace(")))",")")
            .Replace("((","(")
            .Replace("))",")")
            .Replace("  ", " ")
            .TrimEnd();
        
        if(cleanTitle.EndsWith(" -"))
        {
            cleanTitle = cleanTitle[..^2];
        }

        if (cleanTitle.StartsWith('-'))
        {
            cleanTitle = cleanTitle[1..];
        }

        if (cleanTitle.HasDiacritics())
        {
            cleanTitle = cleanTitle.RemoveDiacritics();
        }

        return cleanTitle;
    }

    public static string RemoveBrackets(this string input)
    {
        var result = input.Replace(" (", " - ")
            .Replace("( ", " - ")
            .Replace("(", " - ")
            .Replace(")", " ")
            .Replace("[", " ")
            .Replace("]", " ")
            .Replace("}", " ")
            .Replace("{", " ")
            .Replace("<", " ")
            .Replace(">", " ");

        return result.Trim();
    }

    public static Ticket CreateTicket(int masterKeyRevision, byte[] rightsId, byte[] titleKeyEnc, byte[] signature)
    {
        var keyGen = 0;
        if (masterKeyRevision > 0)
        {
            keyGen = masterKeyRevision += 1;
        }

        var ticket = new Ticket
        {
            SignatureType = TicketSigType.Rsa2048Sha256,
            Signature = signature,
            Issuer = "Root-CA00000003-XS00000020",
            FormatVersion = 2,
            RightsId = rightsId,
            TitleKeyBlock = titleKeyEnc,
            CryptoType = (byte)keyGen,
            SectHeaderOffset = 0x2C0
        };

        return ticket;
    }

    public static Validity VerifyNca(SwitchFsNca fsNca, NsfwProgressLogger logger)
    {
        var nca = fsNca.Nca;

        for (var i = 0; i < 3; i++)
        {
            if (nca.CanOpenSection(i))
            {
                logger.AddSection(i);
                var sectionValidity = nca.VerifySection(i, logger);

                if (sectionValidity == Validity.Invalid) return Validity.Invalid;
            }
        }

        return Validity.Valid;
    }

    public static Validity VerifySection(this Nca nca, int index, NsfwProgressLogger logger)
    {
        Validity validity;

        try
        {
            var sect = nca.GetFsHeader(index);
            var hashType = sect.HashType;
            if (hashType != NcaHashType.Sha256 && hashType != NcaHashType.Ivfc)
            {
                logger.CloseSection(index, Validity.Unchecked, hashType);
                return Validity.Unchecked;
            }

            if (nca.OpenStorage(index, IntegrityCheckLevel.IgnoreOnInvalid, true) is not HierarchicalIntegrityVerificationStorage stream)
            {
                logger.CloseSection(index, Validity.Unchecked);
                return Validity.Unchecked;
            }
            
            validity = stream.Validate(true, logger);
        }
        catch (Exception exception)
        {
            validity = Validity.Invalid;
            logger.CloseSection(index, exception.Message);
            return validity;
        }

        logger.CloseSection(index, validity);
        return validity;
    }

    public const long OneKb = 1024;

    public const long OneMb = OneKb * OneKb;

    public const long OneGb = OneMb * OneKb;

    public const long OneTb = OneGb * OneKb;

    public static string BytesToHumanReadable(this long bytes)
    {
        return bytes switch
        {
            (< OneKb) => $"{bytes} B",
            (>= OneKb) and (< OneMb) => $"{bytes / OneKb:N0} KB",
            (>= OneMb) and (< OneGb) => $"{bytes / OneMb:N0} MB",
            (>= OneGb) and (< OneTb) => $"{bytes / OneMb:N0} GB",
            (>= OneTb) => $"{bytes / OneTb}"
        };
    }

    public static async Task<GameInfo[]> GetTitleDbInfo(SQLiteAsyncConnection dbConnection, string titleId, string? region = null)
    {
        var languageOrder = new List<string>()
        {
            "en", "ja", "de", "fr", "es", "it", "nl", "pt", "ko", "tw", "cn", "zh", "ru"
        };

        AsyncTableQuery<GameInfo> query;

        if (region != null)
        {
            query = dbConnection.Table<GameInfo>().Where(x => x.Id == titleId && x.RegionLanguage == region);
        }
        else
        {
            query = dbConnection.Table<GameInfo>().Where(x => x.Id == titleId);
        }
        
        if(await query.CountAsync() == 0)
        {
            query = dbConnection.Table<GameInfo>().Where(x => x.Ids!.Contains(titleId));
        }
        
        var result = await query.ToArrayAsync();
        return result.OrderBy(x => languageOrder.IndexOf(x.RegionLanguage)).ToArray();
    }

    public static string? LookUpTitle(SQLiteAsyncConnection dbConnection, string titleId)
    {
        var titleNames = GetTitleDbInfo(dbConnection, titleId).Result;
        
        if(titleNames.Length != 0)
        {
            return titleNames.First().Name?.ReplaceLineEndings(string.Empty).RemoveBrackets().CleanTitle() ?? "UNKNOWN";
        }

        return null;
    }
    
    public static DateTime? LookUpReleaseDate(SQLiteAsyncConnection dbConnection, string titleId)
    {
        var titleNames = GetTitleDbInfo(dbConnection, titleId).Result;
        
        if(titleNames.Length != 0)
        {
            var releaseDate = titleNames.First().ReleaseDate;
            
            if (releaseDate != 0 && releaseDate?.ToString().Length == 8)
            {
                var d = releaseDate.Value % 100;
                var m = (releaseDate.Value / 100) % 100;
                var y = releaseDate.Value / 10000;

                return new DateTime(y, m, d);
            }
        }

        return null;
    }
    
    public static async Task<string[]> LookUpRelatedTitles(SQLiteAsyncConnection dbConnection, string titleId)
    {
        var trimmedTitleId = titleId[..^3];
        var query = dbConnection.Table<GameInfo>().Where(x => x.Id!.StartsWith(trimmedTitleId));
        
        var result = await query.ToArrayAsync();
        
        return result.Select(x => ($"[{x.Id}] {x.Name?.RemoveBrackets().CleanTitle()}" ?? "UNKNOWN")).ToArray();
    }
    
    public static string[] LookupLanguages(SQLiteAsyncConnection dbConnection, string titleId)
    {
        var result = dbConnection.Table<GameInfo>().FirstOrDefaultAsync(x => x.Id == titleId).Result;
        
        var languageOrder = new List<string>()
        {
            "en", "ja", "de", "fr", "es", "it", "nl", "pt", "ko", "zh", "ru"
        };
        
        if (result?.Languages == null)
        {
            return Array.Empty<string>();
        }
        
        return result.Languages.Split(",",StringSplitOptions.TrimEntries|StringSplitOptions.RemoveEmptyEntries).OrderBy(x => languageOrder.IndexOf(x)).ToArray();
    }

    public static async Task<(string, string)> LookUpRegions(SQLiteAsyncConnection dbConnection, string titleId)
    {
        var gameInfos = await dbConnection.Table<GameInfo>().Where(x => x.Id == titleId).ToArrayAsync();

        var regionCollection = new List<string>();
        
        foreach (var nsuId in gameInfos.DistinctBy(x => x.NsuId).Select(x => x.NsuId))
        {
            var regions = await dbConnection.Table<TitleRegion>().Where(x => x.NsuId == nsuId).ToArrayAsync();
            var regionArray = regions.Select(x => x.Region.ToUpperInvariant()).ToArray();
            
            regionCollection.AddRange(regionArray);
        }
        
        var regionList = string.Join(",",regionCollection.Distinct().ToArray());
        var region = "UNKNOWN";
        
        //US.EN,CA.EN,CO.EN,PE.EN,BR.PT,MX.ES,AR.EN,AR.ES,CL.ES,MX.EN,US.ES,CA.FR,BR.EN,CO.ES
        
        string[] americas = ["US.", "CA.", "MX."];
        string[] latinAmerica = ["BR.", "AR.", "CL.", "CO.", "CR.", "EC.", "GT.", "PE."];
        string[] europe = ["GB.","DE.","FR.","ES.", "IT.", "PT.", "CH.", "HU.", "LT.", "BE.", "BG.", "EE.", "LU.", "CH.", "HR.", "SI.", "AT.", "GR.", "LU.", "NO.", "DK.", "CZ.", "RO.", "ZA.", "NZ.", "BE.", "CH.", "LV.", "SK.", "SE.", "FI.", "IE.", "AU.", "MT.", "CY."];
        string[] asia = ["HK.","KR.","JP"];

        var regionMatch = 0;
        
        if(latinAmerica.Any(regionList.Contains))
        {
            region = "Latin America";
            regionMatch++;
        }
            
        if (americas.Any(regionList.Contains))
        {
            region = "North America";
            regionMatch++;
        }
            
        if (europe.Any(regionList.Contains))
        {
            region = "Europe";
            regionMatch++;
        }
            
        if(asia.Any(regionList.Contains))
        {
            region = "Asia";
            regionMatch++;
        }

        region = regionList switch
        {
            "KR.KO" => "Korea",
            "JP.JA" => "Japan",
            "HK.ZH" => "China",
            "US.EN" => "USA",
            "DE.DE" => "Germany",
            _ => region
        };

        if(regionMatch > 1)
        {
            region = "World";
        }

        return (region, regionList);
    }
    
    public static async Task<string[]> LookUpRegions(SQLiteAsyncConnection dbConnection, long nsuId)
    {
        var result = await dbConnection.Table<TitleRegion>().Where(x => x.NsuId == nsuId).ToArrayAsync();
        
        return result.Select(x => x.Region).ToArray();
    }

    public static async Task<TitleVersions[]> LookUpUpdates(SQLiteAsyncConnection dbConnection, string titleId)
    {
        return await dbConnection.Table<TitleVersions>().Where(x => x.TitleId == titleId.ToLower()).ToArrayAsync();
    }
    
    public static bool ValidateCommonCert(string certPath)
    {
        var fileBytes = File.ReadAllBytes(certPath);
        
        if(fileBytes.Length != CommonCertSize)
        {
            AnsiConsole.WriteLine("Common cert is invalid size");
            return false;
        }
        
        var certSha256 = SHA256.HashData(fileBytes).ToHexString();

        return certSha256.Equals(CommonCertSha256, StringComparison.InvariantCultureIgnoreCase);
    }
    
    public static bool ValidateCommonCert(Stream certificate)
    {
        var certSha256 = SHA256.HashData(certificate).ToHexString();
        return certSha256.Equals(CommonCertSha256, StringComparison.InvariantCultureIgnoreCase);
    }

    public static int? AssignPriority(string fileName)
    {
        if(fileName.EndsWith(".cnmt.nca"))
        {
            return 1;
        }

        if (fileName.EndsWith(".nca"))
        {
            return 0;
        }

        if (fileName.EndsWith(".tik"))
        {
            return 2;
        }

        if (fileName.EndsWith(".cert"))
        {
            return 3;
        }

        return null;
    }

    public static bool IsOrderCorrect(RawContentFileInfo[] values)
    {
        var rawList = values.Aggregate(string.Empty, (current, rawFile) => current + rawFile.Priority);
        var sortedList = values.OrderBy(x => x.Priority).Aggregate(string.Empty, (current, rawFile) => current + rawFile.Priority);
        return rawList.Equals(sortedList, StringComparison.InvariantCulture);
    }

    public static CnmtInfo[] GetCnmtInfo(SQLiteAsyncConnection dbConnection, string titleId, string version)
    {
        return dbConnection.Table<CnmtInfo>().Where(x => x.TitleId.ToLower() == titleId.ToLower() && x.Version == version).ToArrayAsync().Result;
    }
    
    private static Region GetRegion(NacpLanguage[] titles)
    {
        var region = Region.Unknown;

        if (titles.Length == 0)
        {
            return region;
        }

        if (titles.Length == 1)
        {
            if (titles is [NacpLanguage.AmericanEnglish])
            {
                region = Region.USA;
            }

            if (titles is [NacpLanguage.Japanese])
            {
                region = Region.Japan;
            }

            if (titles is [NacpLanguage.Korean])
            {
                region = Region.Korea;
            }

            if (titles is [NacpLanguage.Russian])
            {
                region = Region.Russia;
            }

            if (titles is [NacpLanguage.TraditionalChinese])
            {
                region = Region.Taiwan;
            }

            if (titles is [NacpLanguage.SimplifiedChinese])
            {
                region = Region.China;
            }

            if (titles is [NacpLanguage.BritishEnglish])
            {
                region = Region.UnitedKingdom;
            }
            
            if(titles is [NacpLanguage.LatinAmericanSpanish])
            {
                region = Region.LatinAmerica;
            }
            
            if(titles is [NacpLanguage.BrazilianPortuguese])
            {
                region = Region.Brazil;
            }
            
            if(titles is [NacpLanguage.Dutch])
            {
                region = Region.Netherlands;
            }
            
            if(titles is [NacpLanguage.Portuguese])
            {
                region = Region.Portugal;
            }
            
            if(titles is [NacpLanguage.French])
            {
                region = Region.France;
            }
            
            if(titles is [NacpLanguage.German])
            {
                region = Region.Germany;
            }
            
            if(titles is [NacpLanguage.Italian])
            {
                region = Region.Italy;
            }
            
            if(titles is [NacpLanguage.Spanish])
            {
                region = Region.Spain;
            }
            
            if(region != Region.Unknown)
            {
                return region;
            }
        }

        if (titles.Any(x => x is NacpLanguage.TraditionalChinese or NacpLanguage.SimplifiedChinese))
        {
            region = Region.China;

            if (titles.Any(x => x is NacpLanguage.Japanese or NacpLanguage.Korean))
            {
                region = Region.Asia;
            }
            
            if (titles.Any(x => x is NacpLanguage.AmericanEnglish or NacpLanguage.BritishEnglish or NacpLanguage.French
                    or NacpLanguage.German or NacpLanguage.Italian or NacpLanguage.Spanish or NacpLanguage.Dutch
                    or NacpLanguage.Portuguese))
            {
                region = Region.World;
            }
        }

        if (titles.Any(x => x is NacpLanguage.BritishEnglish or NacpLanguage.French or NacpLanguage.German
                or NacpLanguage.Italian or NacpLanguage.Spanish or NacpLanguage.Dutch or NacpLanguage.Portuguese) && region == Region.Unknown)
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

        if (titles.Any(x => x is NacpLanguage.AmericanEnglish) && region == Region.Unknown)
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
    
    public static string BuildOutputName(LanguageMode languageMode,
        string[] languagesFullShort,
        string[] languagesShort,
        NacpLanguage[] titles,
        string displayTitle,
        string? displayParentTitle,
        string displayVersion,
        FixedContentMetaType titleType,
        string titleVersion,
        string displayTypeShort,
        string titleId,
        NacpLanguage[] parentLanguages,
        string displayParentLanguages,
        bool isDlc,
        bool possibleDlcUnlocker,
        bool isDemo,
        string distributionRegion,
        bool keepName)
    {
        var languageList = string.Empty;

        if (languageMode == LanguageMode.Full)
        {
            languageList = string.Join(",", languagesFullShort);
        }

        if (languageMode == LanguageMode.Short)
        {
            languageList = string.Join(",", languagesShort);
        }
        
        if (string.IsNullOrWhiteSpace(languageList))
        {
            languageList = "(Unknown)";
        }
        
        if (languageMode == LanguageMode.None)
        {
            languageList = string.Empty;
        }

        if (!string.IsNullOrEmpty(languageList))
        {
            languageList = $"({languageList})";
        }

        var region = GetRegion(titles);
        
        var displayRegion = region switch {
            Region.USA => "(USA)",
            Region.Europe => "(Europe)",
            Region.Asia => "(Asia)",
            Region.Japan => "(Japan)",
            Region.Korea => "(Korea)",
            Region.China => "(China)",
            Region.Russia => "(Russia)",
            Region.UnitedKingdom => "(United Kingdom)",
            Region.World => "(World)",
            Region.LatinAmerica => "(Latin America)",
            Region.Brazil => "(Brazil)",
            Region.Netherlands => "(Netherlands)",
            Region.Portugal => "(Portugal)",
            Region.France => "(France)",
            Region.Germany => "(Germany)",
            Region.Italy => "(Italy)",
            Region.Spain => "(Spain)",
            Region.Taiwan => "(Taiwan)",
            _ => string.Empty
        };
        
        if (!string.IsNullOrWhiteSpace(distributionRegion))
        {
            displayRegion = $"({distributionRegion})";
        }

        if (languageMode == LanguageMode.None)
        {
            displayRegion = string.Empty;
            languageList = string.Empty;
        }

        var cleanTitle = displayTitle.CleanTitle();
        var cleanParentTitle = (displayParentTitle ?? string.Empty).CleanTitle();

        if (string.IsNullOrWhiteSpace(cleanParentTitle))
        {
            cleanParentTitle = string.Empty;
        }

        if (string.IsNullOrWhiteSpace(cleanTitle))
        {
            cleanTitle = string.Empty;
        }

        cleanTitle = cleanTitle.Replace(titleId, String.Empty);
        if (cleanTitle.EndsWith("v0"))
        {
            cleanTitle = cleanTitle[..^2];
        }
        
        var demo = string.Empty;

        if (cleanTitle.EndsWith("体験版"))
        {
            cleanTitle = cleanTitle[..^3];
            isDemo = true;
        }
        
        if (cleanTitle.StartsWith("【体験版】"))
        {
            cleanTitle = cleanTitle[5..];
            isDemo = true;
        }
        
        if (cleanTitle.EndsWith("【体験版】"))
        {
            cleanTitle = cleanTitle[..^5];
            isDemo = true;
        }
        
        if (cleanTitle.ToUpperInvariant().EndsWith(" DEMO"))
        {
            cleanTitle = cleanTitle[..^5];
            isDemo = true;
        }

        if (cleanTitle.ToUpperInvariant().EndsWith(" [DEMO VERSION]"))
        {
            cleanTitle = cleanTitle[..^15];
            isDemo = true;
        }
            
        if (cleanTitle.ToUpperInvariant().EndsWith(" (DEMO VERSION)"))
        {
            cleanTitle = cleanTitle[..^15];
            isDemo = true;
        }
            
        if (cleanTitle.ToUpperInvariant().EndsWith(" DEMO VERSION"))
        {
            cleanTitle = cleanTitle[..^13];
            isDemo = true;
        }
            
        if (cleanTitle.ToUpperInvariant().EndsWith(" DEMO VER."))
        {
            cleanTitle = cleanTitle[..^10];
            isDemo = true;
        }
        
        if (isDemo)
        {
            if (cleanTitle.EndsWith(" -"))
            {
                cleanTitle = cleanTitle[..^2];
            }
            
            demo = "(Demo)";
        }
        
        if(cleanTitle.StartsWith("The ", StringComparison.InvariantCultureIgnoreCase))
        {
            cleanTitle = cleanTitle[4..];
            var firstPart = cleanTitle.Split(" - ", StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).First();
            cleanTitle = cleanTitle.Replace(firstPart, $"{firstPart}, The ", StringComparison.InvariantCultureIgnoreCase);
        }
        
        if(cleanTitle.StartsWith("A ", StringComparison.InvariantCultureIgnoreCase))
        {
            cleanTitle = cleanTitle[2..];
            var firstPart = cleanTitle.Split(" - ", StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).First();
            cleanTitle = cleanTitle.Replace(firstPart, $"{firstPart}, A ", StringComparison.InvariantCultureIgnoreCase);
        }
        
        var regEx = CloseHyphenWrapRegex();
        var match = regEx.Match(cleanTitle);

        if (match.Success)
        {
            var replacement = match.Value[1..^1];
            cleanTitle = cleanTitle.Replace(match.Value, $" - {replacement}", StringComparison.InvariantCultureIgnoreCase);
        }
        
        if (cleanTitle.EndsWith('-'))
        {
            cleanTitle = cleanTitle[..^1];
        }
        
        if (titleType is FixedContentMetaType.Patch)
        {
            return $"{cleanTitle.RemoveBrackets()} {displayRegion}{languageList}{demo}[{displayVersion}][{titleId}][{titleVersion}][{displayTypeShort}]".CleanTitle();
        }
        
        if (isDlc && !string.IsNullOrEmpty(displayParentTitle))
        {
            if (parentLanguages.Any())
            {
                languageList = displayParentLanguages;
                region = GetRegion(parentLanguages.ToArray());
                displayRegion = languageMode != LanguageMode.None ? $"({region})" : string.Empty;
                
                if (!string.IsNullOrWhiteSpace(displayRegion) && !string.IsNullOrWhiteSpace(distributionRegion))
                {
                    displayRegion = $"({distributionRegion})";
                }

                if (languageMode == LanguageMode.None)
                {
                    languageList = string.Empty;
                }

                if (!string.IsNullOrEmpty(languageList))
                {
                    languageList = languageList.Replace("Zh,Zh", "Zh-Hans,Zh-Hant");
                    languageList = $"({languageList})";
                }
            }

            var parentParts = cleanParentTitle.Split(" - ", StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            cleanTitle = parentParts.Aggregate(cleanTitle, (current, part) => current.Replace(part, string.Empty, StringComparison.InvariantCultureIgnoreCase)).CleanTitle();

            var titleParts = cleanTitle.Split(" - ", StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

            foreach (var titlePart in titleParts)
            {
                if (cleanParentTitle.Contains(titlePart, StringComparison.InvariantCultureIgnoreCase))
                {
                    cleanTitle = cleanTitle?.Replace(titlePart, string.Empty, StringComparison.InvariantCultureIgnoreCase).CleanTitle();
                }
            }
            
            var finalTitle = $"{cleanParentTitle.RemoveBrackets()} - {cleanTitle?.RemoveBrackets()} {displayRegion}{languageList}".CleanTitle();
            
            if (keepName)
            {
                finalTitle = $"{displayTitle.RemoveBrackets()} {displayRegion}{languageList}".CleanTitle();
            }

            var formattedTitle = $"{finalTitle}[{titleId}][{titleVersion}][{displayTypeShort}]";

            return formattedTitle.CleanTitle();
        }

        if (displayVersion == "UNKNOWN")
        {
            displayVersion = string.Empty;
        }
        else
        {
            displayVersion = $"[{displayVersion}]";
        }

        return $"{cleanTitle.RemoveBrackets()} {displayRegion}{languageList}{demo}{displayVersion}[{titleId}][{titleVersion}][{displayTypeShort}]".CleanTitle();
    }
    
    public static Validity VerifyNpdm(Nca nca)
    {
        if (nca.Header.ContentType != NcaContentType.Program) return Validity.Unchecked;

        IFileSystem pfs;
        
        try
        {
            pfs = nca.OpenFileSystem(NcaSectionType.Code, IntegrityCheckLevel.ErrorOnInvalid);
        }
        catch
        {
            return Validity.Unchecked;
        }
        
        if (!pfs.FileExists("/main.npdm")) return Validity.Unchecked;
        
        using var npdmFile = new UniqueRef<IFile>();
        pfs.OpenFile(ref npdmFile.Ref, "/main.npdm"u8, OpenMode.Read).ThrowIfFailure();
        
        var validityResult = Validity.Invalid;

        try
        {
            var npdm = new NpdmBinaryFixed(npdmFile.Release().AsStream());
            validityResult = nca.Header.VerifySignature2(npdm.AciD.Rsa2048Modulus);
        }
        catch(Exception exception)
        {
            Serilog.Log.Error(exception.Message);
        }

        return validityResult;
    }

    [GeneratedRegex(@"-\S.*?\S-")]
    private static partial Regex CloseHyphenWrapRegex();
}
