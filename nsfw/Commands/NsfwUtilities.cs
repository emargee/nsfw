using System.Numerics;
using System.Security.Cryptography;
using LibHac.Tools.Es;

namespace Nsfw.Commands;

public static class NsfwUtilities
{
    public static byte[] FixedSignature { get; }= Enumerable.Repeat((byte)0xFF, 0x100).ToArray();
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

    public static string BuildName(string title, string version, string titleId, string titleVersion, string titleType)
    {
        titleType = titleType switch
        {
            "PATCH" => "UPD",
            "APPLICATION" => "BASE",
            "ADDONCONTENT" => "DLC",
            "DELTA" => "DLCUPD",
            _ => "UNKNOWN"
        };

        if (titleType is "UPD" or "DLCUPD")
        {
            return $"{title} [{version}][{titleId}][{titleVersion}][{titleType}]";   
        }

        return $"{title} [{titleId}][{titleVersion}][{titleType}]";
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
}