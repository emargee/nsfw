using System.Diagnostics.CodeAnalysis;
using System.Numerics;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using LibHac.Common;
using LibHac.Common.Keys;
using LibHac.Fs;
using LibHac.Fs.Fsa;
using LibHac.FsSystem;
using LibHac.Ncm;
using LibHac.Spl;
using LibHac.Tools.Es;
using LibHac.Tools.Fs;
using LibHac.Tools.FsSystem;
using LibHac.Tools.FsSystem.NcaUtils;
using LibHac.Tools.Ncm;
using LibHac.Util;
using Spectre.Console;
using ContentType = LibHac.Ncm.ContentType;
using Path = System.IO.Path;

namespace Nsfw.Commands;

public class Cdn2NspService
{
    private readonly Cdn2NspSettings _settings;
    private readonly KeySet _keySet;
    private string _title;
    private string _titleId;
    private string _titleVersion;
    private string _version;
    private string _titleType;
    private bool _hasRightsId;
    private string _certFile;
    private string _ticketFile;
    private byte[] _titleKeyEnc;
    private byte[] _titleKeyDec;
    private byte[] _rightsId;
    private bool _isTicketSignatureValid;
    private int _masterKeyRevision;
    private byte[] _newTicket;
    private readonly Dictionary<string, long> _contentFiles = new();
    private readonly byte[] _fixedSignature = Enumerable.Repeat((byte)0xFF, 0x100).ToArray();
    private bool _isFixedSignature;
    
    public Cdn2NspService(Cdn2NspSettings settings)
    {
        _settings = settings;
        _keySet = ExternalKeyReader.ReadKeyFile(settings.KeysFile);
        _titleId = "UNKNOWN";
        _titleVersion = "UNKNOWN";
        _titleType = "UNKNOWN";
        _title = "UNKNOWN";
        _version = "UNKNOWN";
        _certFile = string.Empty;
        _ticketFile = string.Empty;
        _titleKeyEnc = Array.Empty<byte>();
        _titleKeyDec = Array.Empty<byte>();
        _rightsId = Array.Empty<byte>();
        _newTicket = Array.Empty<byte>();
    }
    
    public int Process(string currentDirectory, string metaNcaFileName)
    {
        var metaNcaFilePath = Path.Combine(currentDirectory, metaNcaFileName);
        
        if (_settings.Verbose)
        {
            AnsiConsole.MarkupLine($"Processing Dir  : [olive]{new DirectoryInfo(currentDirectory).Name.EscapeMarkup()}[/]");
            AnsiConsole.MarkupLine($"Processing CNMT : [olive]{metaNcaFileName}[/]");
        }
        
        var nca = new Nca(_keySet, new LocalStorage(metaNcaFilePath, FileAccess.Read));
        
        if(!nca.CanOpenSection(0))
        {
            AnsiConsole.MarkupLine("[red]Cannot open section 0[/]");
            return 1;
        }
        
        var fs = nca.OpenFileSystem(NcaSectionType.Data, IntegrityCheckLevel.ErrorOnInvalid);
        var fsCnmtPath = fs.EnumerateEntries("/", "*.cnmt").Single().FullPath;
        
        using var file = new UniqueRef<IFile>();
        fs.OpenFile(ref file.Ref, fsCnmtPath.ToU8Span(), OpenMode.Read).ThrowIfFailure();
        
        var cnmt = new Cnmt(file.Release().AsStream());
        
        _titleId = cnmt.TitleId.ToString("X16");
        _titleVersion = $"v{cnmt.TitleVersion.Version}";
        _titleType = cnmt.Type.ToString().ToUpperInvariant();
        
        if (_settings.Verbose)
        {
            AnsiConsole.MarkupLine($"Title ID        : [olive]{_titleId}[/]");
            AnsiConsole.MarkupLine($"Title Version   : [olive]{_titleVersion}[/]");
            AnsiConsole.MarkupLine($"Title Type      : [olive]{_titleType}[/]");
        }
        
        if (cnmt.Type != ContentMetaType.Patch && cnmt.Type != ContentMetaType.Application && cnmt.Type != ContentMetaType.Delta)
        {
            AnsiConsole.MarkupLine($"[red]Unsupported rebuild type {cnmt.Type}[/]");
            return 1;
        }
            
        foreach (var contentEntry in cnmt.ContentEntries)
        {
            var fileName = $"{contentEntry.NcaId.ToHexString().ToLower()}.nca";
            if(!File.Exists(Path.Combine(currentDirectory, fileName)))
            {
                var msg = $"[red]Cannot find {fileName}[/]";
                
                if (contentEntry.Type != ContentType.DeltaFragment)
                {
                    AnsiConsole.MarkupLine(msg+".Skipping delta fragment");
                    continue;
                }
                AnsiConsole.MarkupLine(msg);
                return 1;
            }
            var contentNca = new Nca(_keySet, new LocalStorage(Path.Combine(currentDirectory, fileName), FileAccess.Read));
            if (contentNca.Header.HasRightsId && _titleKeyEnc.Length == 0)
            {
                _hasRightsId = contentNca.Header.HasRightsId;
                _ticketFile = Path.Combine(currentDirectory, $"{contentNca.Header.RightsId.ToHexString()}.tik".ToLowerInvariant());
                _certFile = Path.Combine(currentDirectory, $"{contentNca.Header.RightsId.ToHexString()}.cert".ToLowerInvariant());

                if (!File.Exists(_ticketFile))
                {
                    AnsiConsole.MarkupLine($"[red]Cannot find ticket file - {_ticketFile}[/]");
                    return 1;
                }
                
                var ticket = new Ticket(new LocalFile(_ticketFile, OpenMode.Read).AsStream());
                
                if (ticket.SignatureType != TicketSigType.Rsa2048Sha256)
                {
                    AnsiConsole.MarkupLine($"[red]Unsupported ticket signature type {ticket.SignatureType}[/]");
                    return 1;
                }

                if (ticket.TitleKeyType != TitleKeyType.Common)
                {
                    AnsiConsole.MarkupLine($"[red]Unsupported ticket titleKey type {ticket.TitleKeyType}[/]");
                    return 1;
                }

                _titleKeyEnc = ticket.GetTitleKey(_keySet);
                _rightsId = ticket.RightsId;
                
                _keySet.ExternalKeySet.Add(new RightsId(_rightsId), new AccessKey(_titleKeyEnc));
                
                _titleKeyDec = contentNca.GetDecryptedTitleKey();

                if (_fixedSignature.ToHexString() == ticket.Signature.ToHexString())
                {
                    _isTicketSignatureValid = true;
                    _isFixedSignature = true;
                }
                else
                {
                    _isTicketSignatureValid = ValidateTicket(ticket, _settings.CertFile);
                }
                
                _masterKeyRevision = Utilities.GetMasterKeyRevision(contentNca.Header.KeyGeneration);

                var ticketMasterKey = Utilities.GetMasterKeyRevision(_rightsId.Last());

                if (_masterKeyRevision != ticketMasterKey)
                {
                    AnsiConsole.MarkupLine($"[red]Invalid rights ID key generation! Got {ticketMasterKey}, expected {_masterKeyRevision}.[/]");
                    return 1;
                }
            }

            if (_settings.CheckShas)
            {
                var sha256 = SHA256.HashData(File.ReadAllBytes(Path.Combine(currentDirectory, fileName)));
                if (sha256.Take(16).ToArray().ToHexString() != contentEntry.Hash.Take(16).ToArray().ToHexString())
                {
                    AnsiConsole.MarkupLine($"[red]SHA256 Hash mismatch for {fileName}[/]");
                    return 1;
                }
            }
            
            if(contentEntry.NcaId.ToHexString() != contentEntry.Hash.Take(16).ToArray().ToHexString())
            {
                AnsiConsole.MarkupLine($"[red]Hash mismatch for {fileName}[/]");
                return 1;
            }
        
            if (contentEntry.Type == ContentType.Control)
            {
                var nacpFs = contentNca.OpenFileSystem(NcaSectionType.Data, IntegrityCheckLevel.ErrorOnInvalid);
                var title = new Title();
        
                using (var control = new UniqueRef<IFile>())
                {
                    nacpFs.OpenFile(ref control.Ref, "/control.nacp"u8, OpenMode.Read).ThrowIfFailure();
                    control.Get.Read(out _, 0, title.Control.ByteSpan).ThrowIfFailure();
                }
             
                _title = title.Control.Value.Title[0].NameString.ToString() ?? "UNKNOWN";
                _version = title.Control.Value.DisplayVersionString.ToString() ?? "UNKNOWN";
            }
         
            _contentFiles.Add(Path.Combine(currentDirectory, $"{contentEntry.NcaId.ToHexString().ToLower()}.nca"), contentEntry.Size);
        }
        
        if (_settings.Verbose)
        {
            AnsiConsole.MarkupLine($"Display Title   : [olive]{_title}[/]");
            AnsiConsole.MarkupLine($"Display Version : [olive]{_version}[/]");
            AnsiConsole.MarkupLine($"Title Key (Enc) : [olive]{_titleKeyEnc.ToHexString()}[/]");
            AnsiConsole.MarkupLine($"Title Key (Dec) : [olive]{_titleKeyDec.ToHexString()}[/]");
            AnsiConsole.MarkupLine($"MasterKey Rev.  : [olive]{_masterKeyRevision}[/]");
            if (_isTicketSignatureValid)
            {
                if (_isFixedSignature)
                {
                    AnsiConsole.MarkupLine($"Signature       : [green]VALID (Normalised)[/]");
                }
                else
                {
                    AnsiConsole.MarkupLine($"Signature       : [green]VALID[/]");
                }
            }
            else
            {
                AnsiConsole.MarkupLine($"Signature       : [red]INVALID - Normalising ticket..[/]");
            }
            
        }
        
        _contentFiles.Add(Path.GetFullPath(metaNcaFilePath), new FileInfo(Path.Combine(metaNcaFilePath)).Length);
        
        if (_hasRightsId && !string.IsNullOrEmpty(_ticketFile) && File.Exists(_ticketFile))
        {
            if (!_isTicketSignatureValid)
            {
                var keyGen = 0;
                if (_masterKeyRevision > 0)
                {
                    keyGen = _masterKeyRevision += 1;
                }
                var ticket = new Ticket
                {
                    SignatureType = TicketSigType.Rsa2048Sha256,
                    Signature = _fixedSignature,
                    Issuer = "Root-CA00000003-XS00000020",
                    FormatVersion = 2,
                    RightsId = _rightsId,
                    TitleKeyBlock = _titleKeyEnc,
                    CryptoType = (byte)keyGen,
                    SectHeaderOffset = 0x2C0
                };
                _newTicket = ticket.GetBytes();
                File.WriteAllBytes(_ticketFile, _newTicket);
            }
            _contentFiles.Add(_ticketFile, new FileInfo(_ticketFile).Length);
            File.Copy(_settings.CertFile, _certFile, true);
            _contentFiles.Add(_certFile, new FileInfo(_settings.CertFile).Length);
        }
        
        var nspFilename = BuildNspName(_title, _version, _titleId, _titleVersion, _titleType);
        if (_settings.Verbose)
        {
            AnsiConsole.WriteLine("----------------------------------------");
            var root = new Tree($"[olive]{nspFilename.EscapeMarkup()}[/]");
            foreach (var contentFile in _contentFiles)
            {
                var nodeLabel = $"{Path.GetFileName(contentFile.Key)} -> {contentFile.Value:N0} bytes";
                if (_settings.CheckShas)
                {
                    nodeLabel = "[[[green]V[/]]] "+nodeLabel;
                }
                root.AddNode(nodeLabel);
            }
        
            AnsiConsole.Write(root);
            AnsiConsole.WriteLine("----------------------------------------");
        }
        
        AnsiConsole.MarkupLine($"Creating : [olive]{nspFilename.EscapeMarkup()}[/]");

        if (!_settings.Verbose)
        {
            AnsiConsole.WriteLine(" -> All checks passed.");    
        }
        
        if (_settings.DryRun)
        {
            AnsiConsole.WriteLine(" -> Dry Run. Skipping...");
            return 0;    
        }

        var builder = new PartitionFileSystemBuilder();

        foreach (var contentFile in _contentFiles)
        {
            builder.AddFile(Path.GetFileName(contentFile.Key), new LocalFile(contentFile.Key, OpenMode.Read));
        }

        using var outStream = new FileStream(Path.Combine(_settings.OutDirectory,nspFilename), FileMode.Create, FileAccess.ReadWrite);
        var builtPfs = builder.Build(PartitionFileSystemType.Standard);
        builtPfs.GetSize(out var pfsSize).ThrowIfFailure();
        builtPfs.CopyToStream(outStream, pfsSize);

        AnsiConsole.WriteLine(" -> Done!");
        
        return 0;
    }

    private string BuildNspName(string title, string version, string titleId, string titleVersion, string titleType)
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
            return $"{title} [{version}][{titleId}][{titleVersion}][{titleType}].nsp";   
        }

        return $"{title} [{titleId}][{titleVersion}][{titleType}].nsp";
    }

    private bool ValidateTicket(Ticket ticket, string certPath)
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
}