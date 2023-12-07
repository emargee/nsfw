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

    public static string CleanTitle(this string title)
    {
        var cleanTitle = title
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

        if (cleanTitle.EndsWith(" - "))
        {
            cleanTitle = cleanTitle.TrimEnd(" - ".ToCharArray());
        }
        
        return cleanTitle;
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

    public static string? LookUpTitle(string titledbPath, string titleId)
    {
        var titleNames = GetTitleDbInfo(titledbPath, titleId).Result;
        
        if(titleNames.Length != 0)
        {
            return titleNames.First().Name?.ReplaceLineEndings(string.Empty) ?? "UNKNOWN";
        }

        return null;
    }
    
    public static async Task<string[]> LookUpRelatedTitles(string titleDbPath, string titleId)
    {
        var db = new SQLiteAsyncConnection(titleDbPath);
        var trimmedTitleId = titleId[..^3];
        var query = db.Table<GameInfo>().Where(x => x.Id!.StartsWith(trimmedTitleId));
        
        var result = await query.ToArrayAsync();
        
        return result.Select(x => x.Name ?? "UNKNOWN").ToArray();
    }
    
    public static string[] LookupLanguages(string titleDbPath, string titleId)
    {
        var db = new SQLiteAsyncConnection(titleDbPath);
        var result = db.Table<GameInfo>().FirstOrDefaultAsync(x => x.Id == titleId).Result;
        
        var languageOrder = new List<string>()
        {
            "en", "ja", "de", "fr", "es", "it", "nl", "pt", "kr", "zh", "ru"
        };
        
        if (result?.Languages == null)
        {
            return Array.Empty<string>();
        }
        
        return result.Languages.Split(",").OrderBy(x => languageOrder.IndexOf(x)).ToArray();
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

    public static void RenderTicket(Table table, Ticket ticket)
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