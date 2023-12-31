﻿using System.Numerics;
using System.Security.Cryptography;
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
            .Replace(" <", " - ")
            .Replace(">", string.Empty)
            .Replace("–","-")
            .Replace("“", "'")
            .Replace("*", "")
            .Replace("”", "'")
            .Replace('/', '-')
            .Replace('︰',':')
            .Replace("\uff5e", "-")
            .Replace(":||","-")
            .Replace(": ", " - ")
            .Replace(":", "-")
            .Replace(" ~", " - ")
            .Replace("~ ", " - ")
            .Replace(" - - ", " - ")
            .Replace(" -  - ", " - ")
            .Replace("  ", " ")
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
            .Replace(" Of ", " of ")
            .Replace("((((", "(")
            .Replace("))))", ")")
            .Replace("(((", "(")
            .Replace(")))",")")
            .Replace("((","(")
            .Replace("))",")")
            .TrimEnd();
        
        if(cleanTitle.EndsWith(" -"))
        {
            cleanTitle = cleanTitle[..^2];
        }

        if (cleanTitle.StartsWith('-'))
        {
            cleanTitle = cleanTitle[1..];
        }
        
        return cleanTitle;
    }

    public static string RemoveBrackets(this string input)
    {
        var result = input.Replace(" (", " - ")
                                .Replace(")", " ");

        return result.Trim();
    }

    public static Ticket CreateTicket(int masterKeyRevision, byte[] rightsId, byte[] titleKeyEnc)
    {
        var keyGen = 0;
        if (masterKeyRevision > 0)
        {
            keyGen = masterKeyRevision += 1;
        }

        var ticket = new Ticket
        {
            SignatureType = TicketSigType.Rsa2048Sha256,
            Signature = FixedSignature,
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

    public static async Task<GameInfo[]> GetTitleDbInfo(string titledbPath, string titleId, string? region = null)
    {
        var languageOrder = new List<string>()
        {
            "en", "ja", "de", "fr", "es", "it", "nl", "pt", "ko", "tw", "cn", "zh", "ru"
        };
        var db = new SQLiteAsyncConnection(titledbPath);
        AsyncTableQuery<GameInfo> query;

        if (region != null)
        {
            query = db.Table<GameInfo>().Where(x => x.Id == titleId && x.RegionLanguage == region);
        }
        else
        {
            query = db.Table<GameInfo>().Where(x => x.Id == titleId);
        }
        
        if(await query.CountAsync() == 0)
        {
            query = db.Table<GameInfo>().Where(x => x.Ids!.Contains(titleId));
        }
        
        var result = await query.ToArrayAsync();
        return result.OrderBy(x => languageOrder.IndexOf(x.RegionLanguage)).ToArray();
    }

    public static string? LookUpTitle(string titledbPath, string titleId)
    {
        var titleNames = GetTitleDbInfo(titledbPath, titleId).Result;
        
        if(titleNames.Length != 0)
        {
            return titleNames.First().Name?.ReplaceLineEndings(string.Empty).RemoveBrackets().CleanTitle() ?? "UNKNOWN";
        }

        return null;
    }
    
    public static DateTime? LookUpReleaseDate(string titledbPath, string titleId)
    {
        var titleNames = GetTitleDbInfo(titledbPath, titleId).Result;
        
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
    
    public static async Task<string[]> LookUpRelatedTitles(string titleDbPath, string titleId)
    {
        var db = new SQLiteAsyncConnection(titleDbPath);
        var trimmedTitleId = titleId[..^3];
        var query = db.Table<GameInfo>().Where(x => x.Id!.StartsWith(trimmedTitleId));
        
        var result = await query.ToArrayAsync();
        
        return result.Select(x => (x.Name ?? "UNKNOWN").RemoveBrackets().CleanTitle()).ToArray();
    }
    
    public static string[] LookupLanguages(string titleDbPath, string titleId)
    {
        var db = new SQLiteAsyncConnection(titleDbPath);
        var result = db.Table<GameInfo>().FirstOrDefaultAsync(x => x.Id == titleId).Result;
        
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
    
    public static async Task<string[]> LookUpRegions(string titleDbPath, long nsuId)
    {
        var db = new SQLiteAsyncConnection(titleDbPath);
        var result = await db.Table<TitleRegion>().Where(x => x.NsuId == nsuId).ToArrayAsync();
        
        return result.Select(x => x.Region).ToArray();
    }

    public static async Task<TitleVersions[]> LookUpUpdates(string titleDbPath, string titleId)
    {
        var db = new SQLiteAsyncConnection(titleDbPath);
        return await db.Table<TitleVersions>().Where(x => x.TitleId == titleId.ToLower()).ToArrayAsync();
    }
    
    public static bool ValidateCommonCert(string certPath)
    {
        var commonCertSize = 0x700;
        var commonCertSha256 = "3c4f20dca231655e90c75b3e9689e4dd38135401029ab1f2ea32d1c2573f1dfe";

        var fileBytes = File.ReadAllBytes(certPath);
        
        if(fileBytes.Length != commonCertSize)
        {
            AnsiConsole.WriteLine("Common cert is invalid size");
            return false;
        }
        
        var certSha256 = SHA256.HashData(fileBytes).ToHexString();

        return certSha256 == commonCertSha256.ToUpperInvariant();
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

    public static CnmtInfo[] GetCnmtInfo(string titleDbPath, string titleId, string version)
    {
        var db = new SQLiteAsyncConnection(titleDbPath);
        return db.Table<CnmtInfo>().Where(x => x.TitleId.ToLower() == titleId.ToLower() && x.Version == version).ToArrayAsync().Result;
    }
    
    private static Region GetRegion(NacpLanguage[] titles, ref string languageList)
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
                region = Region.Taiwan;
                languageList = string.Empty;
            }

            if (titles is [NacpLanguage.SimplifiedChinese])
            {
                region = Region.China;
                languageList = string.Empty;
            }

            if (titles is [NacpLanguage.BritishEnglish])
            {
                region = Region.UnitedKingdom;
                languageList = string.Empty;
            }
            
            if(titles is [NacpLanguage.LatinAmericanSpanish])
            {
                region = Region.LatinAmerica;
                languageList = string.Empty;
            }
            
            if(titles is [NacpLanguage.BrazilianPortuguese])
            {
                region = Region.Brazil;
                languageList = string.Empty;
            }
            
            if(titles is [NacpLanguage.Dutch])
            {
                region = Region.Netherlands;
                languageList = string.Empty;
            }
            
            if(titles is [NacpLanguage.Portuguese])
            {
                region = Region.Portugal;
                languageList = string.Empty;
            }
            
            if(titles is [NacpLanguage.French])
            {
                region = Region.France;
                languageList = string.Empty;
            }
            
            if(titles is [NacpLanguage.German])
            {
                region = Region.Germany;
                languageList = string.Empty;
            }
            
            if(titles is [NacpLanguage.Italian])
            {
                region = Region.Italy;
                languageList = string.Empty;
            }
            
            if(titles is [NacpLanguage.Spanish])
            {
                region = Region.Spain;
                languageList = string.Empty;
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
        IEnumerable<string> languagesFullShort,
        IEnumerable<string> languagesShort,
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
        bool isDemo)
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

        if (languageMode == LanguageMode.None)
        {
            languageList = string.Empty;
        }

        if (!string.IsNullOrEmpty(languageList))
        {
            languageList = $"({languageList})";
        }

        var region = GetRegion(titles, ref languageList);
        
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

        if (languageMode == LanguageMode.None)
        {
            displayRegion = string.Empty;
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
        
        if (isDemo)
        {
            demo = "(Demo)";
            if (cleanTitle.ToUpperInvariant().EndsWith(" DEMO"))
            {
                cleanTitle = cleanTitle[..^5];
            }
        }

        if (titleType is FixedContentMetaType.Patch)
        {
            return $"{cleanTitle} {displayRegion}{languageList}{demo}[{displayVersion}][{titleId}][{titleVersion}][{displayTypeShort}]".CleanTitle();
        }

        if (isDlc && !string.IsNullOrEmpty(displayParentTitle))
        {
            if (parentLanguages.Any())
            {
                languageList = displayParentLanguages;
                region = GetRegion(parentLanguages.ToArray(), ref languageList);
                displayRegion = languageMode != LanguageMode.None ? $"({region})" : string.Empty;

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

            var finalTitle = $"{cleanParentTitle} - {cleanTitle} {displayRegion}{languageList}".CleanTitle();

            if (possibleDlcUnlocker)
            {
                finalTitle = $"{cleanParentTitle} - DLC Unlocker (Homebrew){displayRegion}{languageList}".CleanTitle();
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

        return $"{cleanTitle} {displayRegion}{languageList}{demo}{displayVersion}[{titleId}][{titleVersion}][{displayTypeShort}]".CleanTitle();
    }
    
    public static Validity VerifyNpdm(Nca nca)
    {
        if (nca.Header.ContentType != NcaContentType.Program) return Validity.Unchecked;

        var pfs = nca.OpenFileSystem(NcaSectionType.Code, IntegrityCheckLevel.ErrorOnInvalid);
        
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
}
