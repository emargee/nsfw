﻿using System.IO.Compression;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices.ComTypes;
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
    private bool _possibleUnlocker;
    private bool _fromTitleDb;
    private string _baseTitleId;
    private string _titleDbTitle;
    private Dictionary<string, long> _rawContents = new();
    private bool _metaMissing;
    private bool _metaMissingNonDelta;
    private string _parentTitle;
    private bool _metaSizeMismatch;

    public ValidateNspService(ValidateNspSettings settings)
    {
        _settings = settings;
        _keySet = ExternalKeyReader.ReadKeyFile(settings.KeysFile);
        _parentTitle = string.Empty;
    }

    public int Process(string nspFullPath)
    {
        var inputFilename = new DirectoryInfo(nspFullPath).Name;
        
        AnsiConsole.MarkupLine($"Processing NSP  : [olive]{inputFilename.EscapeMarkup()}[/]");
        AnsiConsole.WriteLine("----------------------------------------");

        var phase = "[olive]Open RAW NSP file-system[/]";
        
        using var file = new LocalStorage(nspFullPath, FileAccess.Read);
        
        
        // else if (!ResultFs.PartitionSignatureVerificationFailed.Includes(res))
        // {
        //     res.ThrowIfFailure();
        // }
        // 

        IFileSystem fs = null;
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
            Console.WriteLine("Using HFS");
            fs = hfs.Get;
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

                var name = StringUtils.Utf8ZToString(dirEntry.Name);
                _rawContents.Add(name,dirEntry.Size);
                
                foundTree.AddNode(name + " - " + dirEntry.Size + " (" + dirEntry.Size.BytesToHumanReadable() + ")");
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
                _possibleUnlocker = true;
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

            _baseTitleId = _titleId;
            _titleId = cnmt.TitleId.ToString("X16");
            
            _titleVersion = $"v{cnmt.TitleVersion.Version}";
            _titleType = cnmt.Type.ToString().ToUpperInvariant();
            
            if (cnmt.Type != ContentMetaType.Patch && cnmt.Type != ContentMetaType.Application && cnmt.Type != ContentMetaType.Delta && cnmt.Type != ContentMetaType.AddOnContent)
            {
                AnsiConsole.MarkupLine($"[[[red]ERROR[/]]] -> Unsupported type {cnmt.Type}");
                return 1;
            }
            
            var foundContentTree = new Tree("Metadata Content:");
            
            foreach (var contentEntry in cnmt.ContentEntries)
            {
                var filename = $"{contentEntry.NcaId.ToHexString().ToLower()}.nca";
                
                if(contentEntry.NcaId.ToHexString() != contentEntry.Hash.Take(16).ToArray().ToHexString())
                {
                    AnsiConsole.MarkupLine($"[[[red]ERROR[/]]] -> {phase} - Hash Mis-match {filename}.");
                    return 1;
                }

                if (!_rawContents.ContainsKey(filename))
                {
                    foundContentTree.AddNode("[red][[X]][/] " + filename + " -> " + contentEntry.Type + " (Missing)");
                    _metaMissing = true;
                    if (contentEntry.Type != ContentType.DeltaFragment)
                    {
                        _metaMissingNonDelta = true;
                    }
                }
                else
                {
                    if (_rawContents[filename] != contentEntry.Size)
                    {
                        _metaSizeMismatch = true;
                        foundContentTree.AddNode("[red][[X]][/] " + filename + " -> " + contentEntry.Type + " (Size Mis-match)");
                    }
                    else
                    {
                        foundContentTree.AddNode("[green][[V]][/] " +filename + " -> " + contentEntry.Type);
                    }
                }
            }
            
            AnsiConsole.Write(new Padder(foundContentTree).PadRight(1));
            
            phase = $"[olive]Validate NCAs[/]";
            var canExtract = true;
            
            AnsiConsole.Status()
            .Start("Validating NCAs...", ctx =>
            {
                ctx.Spinner(Spinner.Known.Line);
                ctx.SpinnerStyle(Style.Parse("green"));
                
                if (title.Value.Ncas.Count == 0)
                {
                    AnsiConsole.MarkupLine($"[[[red]ERROR[/]]] -> {phase}");
                    return 1;
                }
                
                var foundNcaTree = new Tree("NCAs:");
                foundNcaTree.Expanded = true;
            
                foreach (var fsNca in title.Value.Ncas)
                {
                    Validity validity;
                    var logger = new NsfwProgressLogger();
                
                    try
                    {
                        ctx.Status($"Validating: {fsNca.Filename}");
                        validity = NsfwUtilities.VerifyNca(fsNca, logger);
                    }
                    catch (Exception e)
                    {
                        AnsiConsole.MarkupLine($"[[[red]ERROR[/]]] -> {phase} - {e.Message}");
                        return 1;
                    }
                
                    var node = new TreeNode(new Markup($"{fsNca.Filename} ({fsNca.Nca.Header.ContentType})"));

                    if (validity != Validity.Valid)
                    {
                        canExtract = false;
                        node.AddNodes(logger.GetReport());
                        foundNcaTree.AddNode(node);
                    }
                    else
                    {
                        node.AddNodes(logger.GetReport());
                        foundNcaTree.AddNode(node);
                    }
                }
            
                AnsiConsole.MarkupLine($"[[[green]DONE[/]]] -> {phase}");
                AnsiConsole.Write(new Padder(foundNcaTree).PadRight(1));
                return 0;
            });
            
            var type = _titleType switch
            {
                "PATCH" => "Update",
                "APPLICATION" => "Game",
                "ADDONCONTENT" => "DLC",
                "DELTA" => "DLC Update",
                _ => "UNKNOWN"
            };

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
            
            var titledbPath = System.IO.Path.GetFullPath(_settings.TitleDbFile);
            
            if((_title == "UNKNOWN" || _settings.VerifyTitle) && File.Exists(titledbPath))
            {
                NsfwUtilities.LookUpTitle(titledbPath, _baseTitleId != _titleId ? _baseTitleId : _titleId, out _titleDbTitle, out _fromTitleDb);
            }
            
            var table = new Table
            {
                ShowHeaders = false
            };
            table.AddColumn("Property");
            table.AddColumn("Value");

            if(_title == "UNKNOWN" && _fromTitleDb)
            {
                table.AddRow("Display Title", _titleDbTitle + " [olive](From TitleDB)[/]");
                _title = _titleDbTitle;
            }
            else
            {
                if (_fromTitleDb)
                {
                    table.AddRow("Display Title", _title + $" (TitleDB: [olive]{_titleDbTitle}[/])");
                }
                else
                {
                    if (_title == "UNKNOWN" && type == "DLC" && inputFilename.Contains('['))
                    {
                        var filenameParts = inputFilename.Split('[', StringSplitOptions.TrimEntries);
                        
                        if(filenameParts.Length > 1)
                        {
                            _title = filenameParts[0];

                            if (!char.IsDigit(filenameParts[1][0]) && filenameParts.Length > 2)
                            {
                                _title += " - " + filenameParts[1].Replace("]",String.Empty).Trim();
                            }
                            table.AddRow("Display Title", _title + " [olive](From Filename)[/]");
                        }
                    }
                    else
                    {
                        table.AddRow("Display Title", _title);
                    }
                }
            }

            if (type == "DLC" && File.Exists(titledbPath))
            {
                var parentTitleId = (cnmt.TitleId & 0xFFFFFFFFFFFFF000 ^ 0x1000).ToString("X16");
                
                var parentResult = NsfwUtilities.LookUpTitle(_settings.TitleDbFile, parentTitleId);

                if (!string.IsNullOrEmpty(parentResult))
                {
                    _parentTitle = parentResult;
                    table.AddRow("Parent Title", parentResult);
                }

                if (_settings.RelatedTitles)
                {
                    var relatedResults = NsfwUtilities.LookUpRelatedTitles(_settings.TitleDbFile, _titleId);

                    if (relatedResults.Length > 0)
                    {
                        table.AddRow(new Text("Related Titles?"),
                            new Rows(relatedResults.Where(x => x != _title).Select(x => new Text(x))));
                    }
                }
            }
            
            table.AddRow("Display Version", _version);

            if (_baseTitleId != _titleId)
            {
                table.AddRow("Base Title ID", _baseTitleId);
            }

            table.AddRow("Title ID", _titleId);
            table.AddRow("Title Type", type + " (" + _titleType + ")");
            table.AddRow("Title Version", _titleVersion);
            table.AddRow("Rights ID", mainNca.Nca.Header.RightsId.IsZeros() ? "EMPTY" : mainNca.Nca.Header.RightsId.ToHexString());

            if (_titleKeyDec != null)
            {
                table.AddRow("TitleKey (Enc)", _titleKeyEnc.ToHexString());
                table.AddRow("TitleKey (Dec)", _titleKeyDec.ToHexString());
                if (_isFixedSignature)
                {
                    table.AddRow("Ticket Signature?", "Normalised");
                }
                else
                {
                    table.AddRow("Ticket Signature?", _isTicketSignatureValid ? "Valid" : "Invalid");
                }
                table.AddRow("MasterKey Revision", _masterKeyRevision.ToString());
            }
            
            var formattedName = NsfwUtilities.BuildName(_title, _version, _titleId, _titleVersion, _titleType, _parentTitle);
            
            if(_settings.Extract || _settings.Convert)
            {
                table.AddRow("Output Name", formattedName.EscapeMarkup());
            }

            if (_possibleUnlocker && type == "DLC")
            {
                canExtract = false;
                table.AddRow(string.Empty, string.Empty);
                table.AddRow("[red]Warning[/]", "This appears to be a [olive]Homebrew DLC Unlocker[/]. Conversion will lose the ticket + cert.");
            }

            if (_metaMissing)
            {
                table.AddRow(string.Empty, string.Empty);
                if (_metaMissingNonDelta)
                {
                    table.AddRow("[red]Warning[/]", "NSP file-system is missing files listed in the CNMT. Conversion will fail.");
                    canExtract = false;
                }
                else
                {
                    table.AddRow("[olive]Notice[/]", "NSP file-system is missing delta fragments listed in CNMT. These errors can be ignored if you do not need them.");
                }
                
            }

            if (_metaSizeMismatch)
            {
                table.AddRow(string.Empty, string.Empty);
                table.AddRow("[red]Warning[/]", "NSP file-system entries have different sizes to those listed in CNMT. Conversion will fail.");
                canExtract = false;
            }
            
            AnsiConsole.Write(new Padder(table).PadRight(1));

            if (!_settings.Extract && !_settings.Convert)
            {
                return 0;
            }

            if (!canExtract)
            {
                AnsiConsole.MarkupLine($"[[[red]WARN[/]]] - Exiting - NSP Validation failed.");
                return 1;
            }

            
            
            if (_hasTitleKeyCrypto && !_isTicketSignatureValid)
            {
                _ticket = NsfwUtilities.CreateTicket(_masterKeyRevision, _ticket.RightsId, _titleKeyEnc);
                AnsiConsole.WriteLine("[[[green]DONE[/]]] -> Generated new normalised ticket.");
            }
            
            if(_settings.Extract)
            {
                phase = $"[olive]Extracting[/]";
                AnsiConsole.MarkupLine($"{phase}..");
                
                var outDir = System.IO.Path.Combine(_settings.CdnDirectory, formattedName);
                
                if(_settings.DryRun)
                {
                    AnsiConsole.MarkupLine($"[[[green]DRYRUN[/]]] -> Would create: [olive]{outDir.EscapeMarkup()}[/]");
                }
                else
                {
                    Directory.CreateDirectory(outDir);
                }
                
                foreach (var nca in title.Value.Ncas)
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

            if (_settings.Convert)
            {
                phase = $"[olive]Converting[/]";
                AnsiConsole.MarkupLine($"{phase}..");

                if (_settings.DryRun)
                {
                    AnsiConsole.MarkupLine($"[[[green]DRYRUN[/]]] -> Would create: [olive]{formattedName.EscapeMarkup()}.nsp[/]");
                }
                
                var builder = new PartitionFileSystemBuilder();

                foreach (var nca in title.Value.Ncas)
                {
                    if (_settings.DryRun)
                    {
                        AnsiConsole.MarkupLine($"[[[green]DRYRUN[/]]] -> Would convert: [olive]{nca.Filename}[/]");
                    }
                    else
                    {
                        builder.AddFile(nca.Filename, nca.Nca.BaseStorage.AsFile(OpenMode.Read));
                    }
                }

                if (_hasTitleKeyCrypto)
                {
                    if (_settings.DryRun)
                    {
                        AnsiConsole.MarkupLine($"[[[green]DRYRUN[/]]] -> Would convert: [olive]{_ticket.RightsId.ToHexString().ToLower()}.tik[/]");
                        AnsiConsole.MarkupLine($"[[[green]DRYRUN[/]]] -> Would convert: [olive]{_ticket.RightsId.ToHexString().ToLower()}.cert[/]");
                    }
                    else
                    {
                        builder.AddFile($"{_ticket.RightsId.ToHexString().ToLower()}.tik", new MemoryStorage(_ticket.File).AsFile(OpenMode.Read));
                        builder.AddFile($"{_ticket.RightsId.ToHexString().ToLower()}.cert", new LocalFile(_settings.CertFile, OpenMode.Read));
                    }
                }

                if (_settings.DryRun) return 0;
                
                using var outStream = new FileStream(System.IO.Path.Combine(_settings.NspDirectory, $"{formattedName}.nsp"), FileMode.Create, FileAccess.ReadWrite);
                var builtPfs = builder.Build(PartitionFileSystemType.Standard);
                builtPfs.GetSize(out var pfsSize).ThrowIfFailure();
                builtPfs.CopyToStream(outStream, pfsSize);
                AnsiConsole.MarkupLine($"[[[green]DONE[/]]] -> Converted: [olive]{formattedName.EscapeMarkup()}_conv.nsp[/]");
            }
        }
        else
        {
            AnsiConsole.MarkupLine($"[[[red]ERROR[/]]] -> No NCA files found.");
            return 1;
        }
        
        return 0;
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