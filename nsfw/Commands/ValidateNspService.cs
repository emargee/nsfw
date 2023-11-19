﻿using System.Runtime.InteropServices.ComTypes;
using System.Text;
using LibHac.Common;
using LibHac.Common.Keys;
using LibHac.Crypto;
using LibHac.Fs;
using LibHac.Fs.Fsa;
using LibHac.FsSystem;
using LibHac.Ncm;
using LibHac.Spl;
using LibHac.Tools.Es;
using LibHac.Tools.Fs;
using LibHac.Tools.FsSystem;
using LibHac.Tools.FsSystem.NcaUtils;
using LibHac.Util;
using Spectre.Console;
using ContentType = LibHac.Ncm.ContentType;
using Path = LibHac.Fs.Path;

namespace Nsfw.Commands;

public class ValidateNspService
{
    private readonly ValidateNspSettings _settings;
    private readonly KeySet _keySet;
    private string _titleId;
    private string _titleVersion;
    private string _titleType;
    private bool _hasTitleKeyCrypto;
    private Ticket _ticket;
    private byte[] _titleKeyEnc;
    private byte[] _titleKeyDec;
    private readonly byte[] _fixedSignature = Enumerable.Repeat((byte)0xFF, 0x100).ToArray();
    private bool _isTicketSignatureValid;
    private bool _isFixedSignature;
    private int _masterKeyRevision;
    private string _title = "UNKNOWN";
    private string _version = "UNKNOWN";

    public ValidateNspService(ValidateNspSettings settings)
    {
        _settings = settings;
        _keySet = ExternalKeyReader.ReadKeyFile(settings.KeysFile);
    }

    public int Process(string nspFullPath)
    {
        AnsiConsole.MarkupLine($"Processing NSP  : [olive]{new DirectoryInfo(nspFullPath).Name.EscapeMarkup()}[/]");
        AnsiConsole.WriteLine("----------------------------------------");

        var phase = "[olive]Open RAW NSP file-system[/]";
        
        using var file = new LocalStorage(nspFullPath, FileAccess.Read);

        IFileSystem fs = null;
        using var pfs = new UniqueRef<PartitionFileSystem>();
        
        pfs.Reset(new PartitionFileSystem());
        var res = pfs.Get.Initialize(file);
        if (res.IsSuccess())
        {
            fs = pfs.Get;
        }
        else if (!ResultFs.PartitionSignatureVerificationFailed.Includes(res))
        {
            res.ThrowIfFailure();
        }

        if (fs == null)
        {
            AnsiConsole.MarkupLine($"[[[red]ERROR[/]]] -> {phase} - Failed to open NSP as PFS0.");
            return 1;
        }
        
        using (var rootDir = new UniqueRef<IDirectory>())
        {
            using var rootPath = new Path();
            PathFunctions.SetUpFixedPath(ref rootPath.Ref(), "/"u8).ThrowIfFailure();
            pfs.Get.OpenDirectory(ref rootDir.Ref, in rootPath, OpenDirectoryMode.All).ThrowIfFailure();
            rootDir.Get.GetEntryCount(out long entryCount).ThrowIfFailure();
            
            var foundTree = new Tree("PFS0:");
        
            var dirEntry = new DirectoryEntry();
        
            while (true)
            {
                rootDir.Get.Read(out long entriesRead, new Span<DirectoryEntry>(ref dirEntry)).ThrowIfFailure();
                if (entriesRead == 0)
                {
                    break;
                }
                
                foundTree.AddNode(StringUtils.Utf8ZToString(dirEntry.Name) + " -> (" + dirEntry.Size + " bytes)");
            }
            AnsiConsole.MarkupLine($"[[[green]DONE[/]]] -> {phase}");
            AnsiConsole.Write(new Padder(foundTree).PadRight(1));
        }
        
        if (fs.EnumerateEntries("*.nca", SearchOptions.Default).Any())
        {
            phase = "[olive]Import tickets[/]";
            
            var tickets = ImportTickets(_keySet, fs).ToArray();
            
            if(tickets.Length == 0)
            {
                AnsiConsole.MarkupLine($"[[[green]DONE[/]]] -> {phase} - No valid tickets found.");
            }
            else
            {
                if(tickets.Length > 1)
                {
                    AnsiConsole.MarkupLine($"[[[red]WARN[/]]] -> {phase} - Multiple tickets found. Using first.");
                }
                else
                {
                    AnsiConsole.MarkupLine($"[[[green]DONE[/]]] -> {phase} (Ticket imported)");
                }
                
                _ticket = tickets[0];
            }
            
            phase = "[olive]Open NCA directory[/]";
        
            var switchFs = SwitchFs.OpenNcaDirectory(_keySet, fs);
        
            if (switchFs == null)
            {
                AnsiConsole.MarkupLine($"[[[red]ERROR[/]]] -> {phase} - Failed to open NSP as SwitchFS.");
                return 1;
            }
            
            AnsiConsole.MarkupLine($"[[[green]DONE[/]]] -> {phase}");

            phase = "[olive]NSP structure validation[/]";

            if (switchFs.Applications.Count != 1)
            {
                AnsiConsole.MarkupLine($"[[[red]ERROR[/]]] -> {phase} - Expected 1 Application, found {switchFs.Applications.Count}");
                return 1;
            }

            if (switchFs.Applications.Count != switchFs.Titles.Count)
            {
                AnsiConsole.MarkupLine($"[[[red]WARN[/]]] -> {phase} - Title count ({switchFs.Titles.Count}) does not match Application count ({switchFs.Applications.Count})");
            }

            var title = switchFs.Titles.First();
            
            phase = $"[olive]Open Main[/]";

            var mainNca = title.Value.MainNca;

            if (mainNca == null)
            {
                AnsiConsole.MarkupLine($"[[[red]ERROR[/]]] -> {phase}");
                return 1;
            }
            AnsiConsole.MarkupLine($"[[[green]DONE[/]]] -> {phase} ({mainNca.NcaId})");
            
            // Get info from MainNca
            
            _titleId = mainNca.Nca.Header.TitleId.ToString("X16");
            _hasTitleKeyCrypto = mainNca.Nca.Header.HasRightsId;

            if (mainNca.Nca.Header.DistributionType != DistributionType.Download)
            {
                AnsiConsole.MarkupLine($"[[[red]ERROR[/]]] -> {phase} - Wrong distribution type {mainNca.Nca.Header.DistributionType}");
                return -1;
            }

            if (tickets.Length > 0 && !_hasTitleKeyCrypto)
            {
                AnsiConsole.MarkupLine($"[[[red]WARN[/]]] -> {phase} - Has a ticket but no title key crypto.");
            }
            
            if (tickets.Length == 0 && _hasTitleKeyCrypto)
            {
                AnsiConsole.MarkupLine($"[[[red]WARN[/]]] -> {phase} - TitleKey encrypted but no valid ticket found.");
            }

            if (_hasTitleKeyCrypto)
            {
                if (mainNca.Nca.Header.RightsId.IsZeros())
                {
                    AnsiConsole.MarkupLine($"[[[red]ERROR[/]]] -> {phase} - NCA is encrypted but has empty rights ID.");
                    return 1; 
                }
                
                phase = $"[olive]Validate TitleKey Crypto[/]";
                
                if (_ticket.SignatureType != TicketSigType.Rsa2048Sha256)
                {
                    AnsiConsole.MarkupLine($"[[[red]ERROR[/]]] -> {phase} - Unsupported ticket signature type {_ticket.SignatureType}");
                    return 1;
                }

                if (_ticket.TitleKeyType != TitleKeyType.Common)
                {
                    AnsiConsole.MarkupLine($"[[[red]ERROR[/]]] -> {phase} - Unsupported ticket titleKey type {_ticket.TitleKeyType}");
                    return 1;
                }
                
                _titleKeyEnc = _ticket.GetTitleKey(_keySet);
                _titleKeyDec = mainNca.Nca.GetDecryptedTitleKey();
                
                if (_fixedSignature.ToHexString() == _ticket.Signature.ToHexString())
                {
                    _isTicketSignatureValid = true;
                    _isFixedSignature = true;
                }
                else
                {
                    _isTicketSignatureValid = NsfwUtilities.ValidateTicket(_ticket, _settings.CertFile);
                }
                
                _masterKeyRevision = Utilities.GetMasterKeyRevision(mainNca.Nca.Header.KeyGeneration);

                var ticketMasterKey = Utilities.GetMasterKeyRevision(_ticket.RightsId.Last());

                if (_masterKeyRevision != ticketMasterKey)
                {
                    AnsiConsole.MarkupLine($"[[[red]ERROR[/]]] -> {phase} - Invalid rights ID key generation! Got {ticketMasterKey}, expected {_masterKeyRevision}.");
                    return 1;
                }
                
                if (!_isTicketSignatureValid)
                {
                    AnsiConsole.MarkupLine($"[[[red]WARN[/]]] -> {phase} - Invalid ticket signature");
                }
                
                AnsiConsole.MarkupLine($"[[[green]DONE[/]]] -> {phase}");
            }
            
            phase = $"[olive]Open Metadata (CNMT)[/]";

            var cnmt = title.Value.Metadata;
            
            if (cnmt == null)
            {
                AnsiConsole.MarkupLine($"[[[red]ERROR[/]]] -> {phase}");
                return 1;
            }
            AnsiConsole.MarkupLine($"[[[green]DONE[/]]] -> {phase}");
            
            phase = $"[olive]Validate Metadata[/]";

            if (cnmt.TitleId.ToString("X16") != _titleId && title.Value.Metadata.Type == ContentMetaType.Application)
            {
                AnsiConsole.MarkupLine($"[[[red]WARN[/]]] -> {phase} - TitleID Mis-match. Expected {_titleId}, found {cnmt.TitleId.ToString("X16")}");
            }
            
            _titleVersion = $"v{cnmt.TitleVersion.Version}";
            _titleType = cnmt.Type.ToString().ToUpperInvariant();
            
            if (cnmt.Type != ContentMetaType.Patch && cnmt.Type != ContentMetaType.Application && cnmt.Type != ContentMetaType.Delta && cnmt.Type != ContentMetaType.AddOnContent)
            {
                AnsiConsole.MarkupLine($"[[[red]ERROR[/]]] -> Unsupported type {cnmt.Type}");
                return 1;
            }
            
            var foundContentTree = new Tree("Metadata Content:");
            var deltaCount = 0;
            
            foreach (var contentEntry in cnmt.ContentEntries)
            {
                var filename = $"{contentEntry.NcaId.ToHexString().ToLower()}.nca";
                
                if(contentEntry.NcaId.ToHexString() != contentEntry.Hash.Take(16).ToArray().ToHexString())
                {
                    AnsiConsole.MarkupLine($"[[[red]ERROR[/]]] -> {phase} - Hash Mis-match {filename}.");
                    return 1;
                }
                
                if(contentEntry.Type == ContentType.DeltaFragment)
                {
                    deltaCount++;
                }
                
                foundContentTree.AddNode(filename + "-> " + contentEntry.Type + " -> " + contentEntry.Size + " bytes");
            }
            
            if(cnmt.ContentEntryCount + 1 != title.Value.Ncas.Count)
            {
                if (cnmt.ContentEntryCount + 1 - deltaCount != title.Value.Ncas.Count())
                {
                    AnsiConsole.MarkupLine($"[[[red]WARN[/]]] -> {phase} - ContentEntryCount ({cnmt.ContentEntryCount + 1}) does not match NCA count ({title.Value.Ncas.Count})");
                }
            }
            
            AnsiConsole.Write(new Padder(foundContentTree).PadRight(1));
            
            phase = $"[olive]Validate NCAs[/]";

            if (title.Value.Ncas.Count == 0)
            {
                AnsiConsole.MarkupLine($"[[[red]ERROR[/]]] -> {phase}");
                return 1;
            }
            var canExtract = true;
            var foundNcaTree = new Tree("NCAs:");
            foreach (var fsNca in title.Value.Ncas)
            {
                var validity = Validity.Unchecked;
                
                try
                {
                    validity = fsNca.VerifyNca();
                }
                catch (Exception e)
                {
                    AnsiConsole.MarkupLine($"[[[red]ERROR[/]]] -> {phase} - {e.Message}");
                    return 1;
                }

                if (validity != Validity.Valid)
                {
                    canExtract = false;
                    foundNcaTree.AddNode("[[[red]X[/]]] " + fsNca.NcaId + " -> " + fsNca.Nca.Header.ContentType + $" -> {validity.ToString().ToUpperInvariant()}");
                }
                else
                {
                    foundNcaTree.AddNode("[[[green]V[/]]] " + fsNca.NcaId + " -> " + fsNca.Nca.Header.ContentType);
                }
            }
            
            AnsiConsole.MarkupLine($"[[[green]DONE[/]]] -> {phase}");
            AnsiConsole.Write(new Padder(foundNcaTree).PadRight(1));

            if (title.Value.Control.Value.Title.Items != null)
            {
                foreach (var titleItem in title.Value.Control.Value.Title.Items)
                {
                    if (!string.IsNullOrEmpty(titleItem.NameString.ToString()))
                    {
                        _title = titleItem.NameString.ToString() ?? "UNKNOWN";
                        break;
                    }
                }
            }

            if (!string.IsNullOrEmpty(title.Value.Control.Value.DisplayVersionString.ToString()))
            {
                _version = title.Value.Control.Value.DisplayVersionString.ToString()!.Trim();
            }
            
            var table = new Table();
            table.AddColumn("Property");
            table.AddColumn("Value");

            table.AddRow("Display Title", _title);
            table.AddRow("Display Version", _version);
            table.AddRow("Title ID", _titleId);
            table.AddRow("Title Type", _titleType);
            table.AddRow("Title Version", _titleVersion);

            if (_titleKeyDec != null)
            {
                table.AddRow("TitleKey (Enc)", _titleKeyEnc.ToHexString());
                table.AddRow("TitleKey (Dec)", _titleKeyDec.ToHexString());
                if (_isFixedSignature)
                {
                    table.AddRow("Valid Signature?", "FIXED");
                }
                else
                {
                    table.AddRow("Valid Signature?", _isTicketSignatureValid.ToString());
                }
                table.AddRow("MasterKey Revision", _masterKeyRevision.ToString());
            }
            
            AnsiConsole.Write(new Padder(table).PadRight(1));

            if (!_settings.Extract)
            {
                return 0;
            }
            
            phase = $"[olive]Extract NCAs[/]";
            
            if(_settings.Extract && canExtract)
            {
                var outDir = System.IO.Path.Combine(_settings.OutDirectory, NsfwUtilities.BuildName(_title, _version, _titleId, _titleVersion, _titleType));
                
                if(_settings.DryRun)
                {
                    AnsiConsole.MarkupLine($"[[[green]DRYRUN[/]]] -> Would create: [olive]{outDir.EscapeMarkup()}[/]");
                }
                else
                {
                    Directory.CreateDirectory(outDir);
                }
                
                foreach (var nca in title.Value.Ncas.Where(nca => nca.Nca.Header.ContentType != NcaContentType.Data))
                {
                    if(_settings.DryRun)
                    {
                        AnsiConsole.MarkupLine($"[[[green]DRYRUN[/]]] -> Would export: [olive]{nca.Filename}[/]");
                        continue;
                    }
                    
                    var stream = nca.Nca.BaseStorage.AsStream();
                    var outFile = System.IO.Path.Combine(outDir, nca.Filename);

                    using var outStream = new FileStream(outFile, FileMode.Create, FileAccess.ReadWrite);
                    stream.CopyStream(outStream, stream.Length);
                }

                if (_hasTitleKeyCrypto)
                {
                     var decFile = $"{_ticket.RightsId.ToHexString().ToLower()}.dectitlekey.tik";
                     var encFile = $"{_ticket.RightsId.ToHexString().ToLower()}.enctitlekey.tik";
                     
                    if(_settings.DryRun)
                    {
                        AnsiConsole.MarkupLine($"[[[green]DRYRUN[/]]] -> Would export: [olive]{decFile.EscapeMarkup()}[/]");
                        AnsiConsole.MarkupLine($"[[[green]DRYRUN[/]]] -> Would export: [olive]{encFile.EscapeMarkup()}[/]");
                        AnsiConsole.MarkupLine($"[[[green]DRYRUN[/]]] -> Would export: [olive]{_ticket.RightsId.ToHexString().ToLower()}.tik[/]");
                    }
                    else
                    {
                        File.WriteAllBytes(System.IO.Path.Combine(outDir, decFile), _titleKeyDec);
                        File.WriteAllBytes(System.IO.Path.Combine(outDir, encFile), _titleKeyEnc);
                        File.WriteAllBytes(System.IO.Path.Combine(outDir, $"{_ticket.RightsId.ToHexString().ToLower()}.tik"), _ticket.File);
                    }
                }
                
                if(!_settings.DryRun)
                {
                    AnsiConsole.MarkupLine($"[[[green]DONE[/]]] -> Exported: [olive]{outDir.EscapeMarkup()}[/]");
                }
            }
            else
            {
                AnsiConsole.MarkupLine($"[[[red]WARN[/]]] -> {phase} - Skipping extraction - Validation failed.");
            }
        }
        else
        {
            AnsiConsole.MarkupLine($"[[[red]ERROR[/]]] -> No NCA files found.");
            return 1;
        }
        
        return 0;
    }

    private static IFileSystem? OpenFileSystem(LocalStorage file)
    {
        IFileSystem? fs = null;
        using var pfs = new UniqueRef<PartitionFileSystem>();
        using var hfs = new UniqueRef<Sha256PartitionFileSystem>();

        pfs.Reset(new PartitionFileSystem());
        var res = pfs.Get.Initialize(file);
        if (res.IsSuccess())
        {
            fs = pfs.Get;

        }
        else if (!ResultFs.PartitionSignatureVerificationFailed.Includes(res))
        {
            res.ThrowIfFailure();
        }
        else
        {
            // Reading the input as a PartitionFileSystem didn't work. Try reading it as an Sha256PartitionFileSystem
            hfs.Reset(new Sha256PartitionFileSystem());
            res = hfs.Get.Initialize(file);
            if (res.IsFailure())
            {
                if (ResultFs.Sha256PartitionSignatureVerificationFailed.Includes(res))
                {
                    ResultFs.PartitionSignatureVerificationFailed.Value.ThrowIfFailure();
                }

                res.ThrowIfFailure();
            }

            fs = hfs.Get;
        }

        return fs;
    }
    
    private static IEnumerable<Ticket> ImportTickets(KeySet keySet, IFileSystem fileSystem)
    {
        foreach (var entry in fileSystem.EnumerateEntries("*.tik", SearchOptions.Default))
        {
            using var tikFile = new UniqueRef<IFile>();
            fileSystem.OpenFile(ref tikFile.Ref, entry.FullPath.ToU8Span(), OpenMode.Read).ThrowIfFailure();
            
            var ticket = new Ticket(tikFile.Get.AsStream());

            if (ticket.RightsId.IsZeros())
            {
                AnsiConsole.MarkupLine($"[[[red]WARN[/]]] -> [olive]Import tickets[/] - Empty Rights ID. Skipping");
                continue;
            }

            byte[] key = ticket.GetTitleKey(keySet);
            if (key is null)
            {   
                AnsiConsole.MarkupLine($"[[[red]WARN[/]]] -> [olive]Import tickets[/] - TitleKey not found. Skipping");
                continue;
            }

            var rightsId = new RightsId(ticket.RightsId);
            var accessKey = new AccessKey(key);
            
            keySet.ExternalKeySet.Add(rightsId, accessKey).ThrowIfFailure();
            yield return ticket;
        }
    }
    
}

public static class Extensions
{
}