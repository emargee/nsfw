using System.Globalization;
using System.Numerics;
using System.Security.Cryptography;
using System.Text.RegularExpressions;
using LibHac.Common;
using LibHac.Tools.Es;
using LibHac.Tools.Fs;
using LibHac.Tools.FsSystem;
using LibHac.Tools.FsSystem.NcaUtils;
using LibHac.Util;
using Spectre.Console;
using SQLite;
using HierarchicalIntegrityVerificationStorage = LibHac.Tools.FsSystem.HierarchicalIntegrityVerificationStorage;

namespace Nsfw.Commands;

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

    public static string BuildName(string title, string version, string titleId, string titleVersion, string titleType, string parentTitle, IEnumerable<TitleInfo> titles, LanguageMode languageMode = LanguageMode.Full)
    {
        titleType = titleType switch
        {
            "PATCH" => "UPD",
            "APPLICATION" => "BASE",
            "ADDONCONTENT" => "DLC",
            "DELTA" => "DLCUPD",
            _ => "UNKNOWN"
        };
        
        var titleArray = titles.ToArray();

        var languageList = string.Empty;

        if (languageMode == LanguageMode.Full)
        {
            languageList = string.Join(",", titleArray.Select(titleInfo => titleInfo.RegionLanguage switch
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
                }).Distinct()
                .ToList());
        }

        if (languageMode == LanguageMode.Short)
        {
            languageList = string.Join(",", titleArray.Select(titleInfo => titleInfo.RegionLanguage switch
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
                    NacpLanguage.SimplifiedChinese => "Zh",
                    NacpLanguage.TraditionalChinese => "Zh",
                    NacpLanguage.Korean => "Ko",
                    NacpLanguage.Dutch => "Nl",
                    NacpLanguage.Portuguese => "Pt",
                    NacpLanguage.BrazilianPortuguese => "Pt",
                    NacpLanguage.Russian => "Ru",
                    _ => "Unknown"
                }).Distinct()
                .ToList());
        }
        
        if (!string.IsNullOrEmpty(languageList))
        {
            languageList = $"({languageList})";
        }

        var region = string.Empty;

        if (titleArray is [{ RegionLanguage: NacpLanguage.AmericanEnglish }])
        {
            region = "(USA)";
            languageList = string.Empty;
        }
        
        if (titleArray is [{ RegionLanguage: NacpLanguage.Japanese }])
        {
            region = "(Japan)";
            languageList = string.Empty;
        }
        
        if (titleArray is [{ RegionLanguage: NacpLanguage.Korean }])
        {
            region = "(Korea)";
            languageList = string.Empty;
        }
        
        if (titleArray is [{ RegionLanguage: NacpLanguage.Russian }])
        {
            region = "(Russia)";
            languageList = string.Empty;
        }
        
        if (titleArray.Any(x => x.RegionLanguage is NacpLanguage.TraditionalChinese or NacpLanguage.SimplifiedChinese))
        {
            region = "(China)";
            
            if(titleArray.Any(x => x.RegionLanguage is NacpLanguage.Japanese or NacpLanguage.Korean))
            {
                region = "(Asia)";
                if (titleArray.Any(x => x.RegionLanguage == NacpLanguage.AmericanEnglish))
                {
                    region = "(World)";
                }
            }
        }
        
        if (titleArray.Any(x => x.RegionLanguage is NacpLanguage.BritishEnglish or NacpLanguage.French or NacpLanguage.German or NacpLanguage.Italian or NacpLanguage.Spanish or NacpLanguage.Dutch or NacpLanguage.Portuguese))
        {
            region = "(Europe)";
            
            if(titleArray.Any(x => x.RegionLanguage is NacpLanguage.AmericanEnglish or NacpLanguage.Japanese or NacpLanguage.Korean or NacpLanguage.Russian or NacpLanguage.TraditionalChinese or NacpLanguage.SimplifiedChinese or NacpLanguage.LatinAmericanSpanish or NacpLanguage.BrazilianPortuguese or NacpLanguage.CanadianFrench))
            {
                region = "(World)";
            }
        }

        title = title.CleanTitle();
        parentTitle = parentTitle.CleanTitle();
        
        var textInfo = new CultureInfo("en-US", false).TextInfo;
        title = textInfo.ToTitleCase(title);
        parentTitle = textInfo.ToTitleCase(parentTitle);

        if (languageMode == LanguageMode.None)
        {
            region = string.Empty;
        }
        
        if (titleType is "UPD" or "DLCUPD")
        {
            return $"{title} {region}{languageList}[{version}][{titleId}][{titleVersion}][{titleType}]".CleanTitle();
        }

        if (titleType is "DLC" && !string.IsNullOrEmpty(parentTitle))
        {
            var parentParts = parentTitle.Split(" - ", StringSplitOptions.TrimEntries);

            title = parentParts.Aggregate(title, (current, part) => current.Replace(part, string.Empty, StringComparison.InvariantCultureIgnoreCase));

            var formattedTitle = $"{parentTitle} - {title} {region}{languageList}[{titleId}][{titleVersion}][{titleType}]";
               
            return formattedTitle.CleanTitle();
        }
        
        return $"{title} {region}{languageList}[{titleId}][{titleVersion}][{titleType}]".CleanTitle();
        
    }

    private static string CleanTitle(this string title)
    {
        return title
            .ReplaceLineEndings("")
            .Replace("“", "'")
            .Replace("*", "")
            .Replace("”", "'")
            .Replace('/', '-')
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
            .Replace("Digital Edition", "(Digital Edition)");
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
            (< OneKb) => $"{bytes}B",
            (>= OneKb) and (< OneMb) => $"{bytes / OneKb:N0}KB",
            (>= OneMb) and (< OneGb) => $"{bytes / OneMb:N0}MB",
            (>= OneGb) and (< OneTb) => $"{bytes / OneMb:N0}GB",
            (>= OneTb) => $"{bytes / OneTb}"
        };
    }

    public static async Task<GameInfo[]> GetTitleDbInfo(string titledbPath, string titleId, string? region = null)
    {
        var languageOrder = new List<string>()
        {
            "US", "GB", "JP", "DE", "FR", "ES", "IT", "NL", "PT", "KR", "TW", "CN", "RU"
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
        
        var result = await query.ToArrayAsync();
        return result.OrderBy(x => languageOrder.IndexOf(x.RegionLanguage)).ToArray();
    }

    public static void LookUpTitle(string titledbPath, string titleId, out string titleDbTitle, out bool fromTitleDb)
    {
        var titleNames = GetTitleDbInfo(titledbPath, titleId).Result;
        
        if(titleNames.Length != 0)
        {
            titleDbTitle = titleNames.First().Name ?? "UNKNOWN";
            fromTitleDb = true;
            return;
        }

        titleDbTitle = string.Empty;
        fromTitleDb = false;
    }

    public static string? LookUpTitle(string titleDbPath, string titleId)
    {
        return GetTitleDbInfo(titleDbPath, titleId).Result.FirstOrDefault()?.Name;
    }
    
    public static async Task<string[]> LookUpRelatedTitles(string titleDbPath, string titleId)
    {
        var db = new SQLiteAsyncConnection(titleDbPath);
        var trimmedTitleId = titleId[..^3];
        var query = db.Table<GameInfo>().Where(x => x.Id.StartsWith(trimmedTitleId));
        
        var result = await query.ToArrayAsync();
        
        return result.Select(x => x.Name ?? "UNKNOWN").ToArray();
    }
    
    public static string LookupLanguages(string titleDbPath, string titleId)
    {
        var db = new SQLiteAsyncConnection(titleDbPath);
        var result = db.Table<GameInfo>().FirstOrDefaultAsync(x => x.Id == titleId).Result;
        
        return result.Languages ?? string.Empty;
    }

    public static async Task<TitleVersions[]> LookUpUpdates(string titleDbPath, string titleId)
    {
        var db = new SQLiteAsyncConnection(titleDbPath);
        return await db.Table<TitleVersions>().Where(x => x.TitleId == titleId.ToLower()).ToArrayAsync();
    }

    public static string TrimTitle(string title)
    {
        if (!title.Contains('「') || !title.Contains('」'))
        {
            return title;
        }

        var titleParts = new List<string>();

        const string pattern = @"(?<=「).*?(?=」)";
        var regex = JapaneseBracketRegex();

        var matches = regex.Matches(title);

        foreach (Match match in matches)
        {
            titleParts.Add(match.Value.Trim());
        }
        
        return string.Join(" & ", titleParts);
    }

    [GeneratedRegex("(?<=「).*?(?=」)")]
    private static partial Regex JapaneseBracketRegex();
    
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

    public static void FormatTicket(Table table, Ticket ticket)
    {
        table.AddRow("Issuer", ticket.Issuer);
        table.AddRow("Format Version", "0x" + ticket.FormatVersion.ToString("X"));
        table.AddRow("TitleKey Type", ticket.TitleKeyType.ToString());
        table.AddRow("Ticket Id", "0x" +ticket.TicketId.ToString("X"));
        table.AddRow("Ticket Version", "0x" +ticket.TicketVersion.ToString("X"));
        table.AddRow("License Type", ticket.LicenseType.ToString());
        table.AddRow("Crypto Type", "0x" +ticket.CryptoType.ToString("X"));
        table.AddRow("Device Id", "0x" +ticket.DeviceId.ToString("X"));
        table.AddRow("Account Id", "0x" +ticket.AccountId.ToString("X"));
        table.AddRow("Rights Id", ticket.RightsId.ToHexString());
        table.AddRow("Signature Type", ticket.SignatureType.ToString());
        
        var propertyTable = new Table{
            ShowHeaders = false
        };
        propertyTable.AddColumn("Property");
        propertyTable.AddColumn("Value");
        
        var myTikFlags = (FixedPropertyFlags)ticket.PropertyMask;
        propertyTable.AddRow("Pre-Install ?", myTikFlags.HasFlag(FixedPropertyFlags.PreInstall) ? "[red]True[/]" : "[green]False[/]");
        propertyTable.AddRow("Allow All Content ?", myTikFlags.HasFlag(FixedPropertyFlags.AllowAllContent) ? "[red]True[/]" : "[green]False[/]");
        propertyTable.AddRow("Shared Title ?", myTikFlags.HasFlag(FixedPropertyFlags.SharedTitle) ? "[red]True[/]" : "[green]False[/]");
        propertyTable.AddRow("DeviceLink Independent ?", myTikFlags.HasFlag(FixedPropertyFlags.DeviceLinkIndependent) ? "[red]True[/]" : "[green]False[/]");
        propertyTable.AddRow("Volatile ?", myTikFlags.HasFlag(FixedPropertyFlags.Volatile) ? "[red]True[/]" : "[green]False[/]");
        propertyTable.AddRow("E-License Required ?", myTikFlags.HasFlag(FixedPropertyFlags.ELicenseRequired) ? "[red]True[/]" : "[green]False[/]");

        table.AddRow(new Text("Properties"), propertyTable);
    }
}