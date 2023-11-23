using System.Numerics;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Nodes;
using LibHac.Common;
using LibHac.Tools.Es;
using LibHac.Tools.Fs;
using LibHac.Tools.FsSystem;
using LibHac.Tools.FsSystem.NcaUtils;
using HierarchicalIntegrityVerificationStorage = LibHac.Tools.FsSystem.HierarchicalIntegrityVerificationStorage;

namespace Nsfw.Commands;

public static class NsfwUtilities
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

    public static string BuildName(string title, string version, string titleId, string titleVersion, string titleType, string parentTitle)
    {
        titleType = titleType switch
        {
            "PATCH" => "UPD",
            "APPLICATION" => "BASE",
            "ADDONCONTENT" => "DLC",
            "DELTA" => "DLCUPD",
            _ => "UNKNOWN"
        };
        
        title = title.CleanTitle();
        parentTitle = parentTitle.CleanTitle();

        if (titleType is "UPD" or "DLCUPD")
        {
            return $"{title} [{version}][{titleId}][{titleVersion}][{titleType}]";
        }

        if (titleType is "DLC" && !string.IsNullOrEmpty(parentTitle))
        {
            title = title.Replace(parentTitle, string.Empty).Replace(" - ", "").Trim();
            return $"{parentTitle} - {title} [{titleId}][{titleVersion}][{titleType}]";
        }
        
        return $"{title} [{titleId}][{titleVersion}][{titleType}]";
    }

    private static string CleanTitle(this string title)
    {
        return title
            .Replace('/', '-')
            .Replace(": ", " - ")
            .Replace("'","")
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

        var validity = stream.Validate(true, logger);

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

    private static async IAsyncEnumerable<string> GetTitleDbInfo(string titledbPath, string titleId)
    {
        await using var fs = File.OpenRead(titledbPath);

        var enumerable = JsonSerializer.DeserializeAsyncEnumerable<JsonNode>(fs, JsonSerializerOptions.Default);
        
        await foreach(var node in enumerable)
        {
            if (node == null) continue;
            
            if(node["id"] is not { }) continue;
            
            if (!node["id"]!.ToString().StartsWith(titleId)) continue;

            if (node["name"] is { } nameNode)
            {
                yield return nameNode.ToString().Replace("Ôäó",""); 
            }
        }
    }

    public static void LookUpTitle(string titledbPath, string titleId, out string titleDbTitle, out bool fromTitleDb)
    {
        var titleNames = GetTitleDbInfo(titledbPath, titleId).ToBlockingEnumerable().ToArray();
        
        if(titleNames.Length != 0)
        {
            titleDbTitle = titleNames.First();
            fromTitleDb = true;
            return;
        }

        titleDbTitle = string.Empty;
        fromTitleDb = false;
    }

    public static string? LookUpTitle(string titleDbPath, string titleId)
    {
        return GetTitleDbInfo(titleDbPath, titleId).ToBlockingEnumerable().FirstOrDefault();
    }
    
    public static string[] LookUpRelatedTitles(string titleDbPath, string titleId)
    {
        return GetTitleDbInfo(titleDbPath, titleId[..^3]).ToBlockingEnumerable().ToArray();
    }
}