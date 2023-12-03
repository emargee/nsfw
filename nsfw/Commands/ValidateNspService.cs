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
    private Validity _headerSignatureValidatity;
    private string _parentTitleId;
    private bool _rebuildTicket;

    public ValidateNspService(ValidateNspSettings settings)
    {
        _settings = settings;
        _keySet = ExternalKeyReader.ReadKeyFile(settings.KeysFile);
        _parentTitle = string.Empty;
    }

    public int Process(string nspFullPath)
    {
        var inputFilename = new DirectoryInfo(nspFullPath).Name;
        var canExtract = true;
        var warnings = new HashSet<string>();
        var notices = new HashSet<string>();

        var languageMode = LanguageMode.Full;

        if (_settings.NoLanguages)
        {
            languageMode = LanguageMode.None;
        }
        
        if(_settings.ShortLanguages)
        {
            languageMode = LanguageMode.Short;
        }

        AnsiConsole.MarkupLine(_settings.Quiet
            ? $"Processing NSP (quiet) : [olive]{inputFilename.EscapeMarkup()}[/]"
            : $"Processing NSP  : [olive]{inputFilename.EscapeMarkup()}[/]");

        AnsiConsole.WriteLine("----------------------------------------");

        var phase = "[olive]Open RAW NSP file-system[/]";
        
        using var file = new LocalStorage(nspFullPath, FileAccess.Read);

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
            
            if(!_settings.Quiet)
            {
                AnsiConsole.MarkupLine($"[[[green]DONE[/]]] -> {phase}");
                AnsiConsole.Write(new Padder(foundTree).PadLeft(1));
            }
        }
        
        if (fs.EnumerateEntries("*.nca", SearchOptions.Default).Any())
        {
            phase = "[olive]Import tickets[/]";
            
            var tickets = ImportTickets(_keySet, fs).ToArray();
            
            if(tickets.Length == 0)
            {
                if (!_settings.Quiet)
                {
                    AnsiConsole.MarkupLine($"[[[green]DONE[/]]] -> {phase} - No valid tickets found.");
                }
            }
            else
            {
                if(tickets.Length > 1)
                {
                    AnsiConsole.MarkupLine($"[[[red]WARN[/]]] -> {phase} - Multiple tickets found. Using first.");
                }
                else
                {
                    if (!_settings.Quiet)
                    {
                        AnsiConsole.MarkupLine($"[[[green]DONE[/]]] -> {phase} (Ticket imported)");
                    }
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

            if (!_settings.Quiet)
            {
                AnsiConsole.MarkupLine($"[[[green]DONE[/]]] -> {phase}");
            }

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
            
            phase = $"[olive]Validate Main[/]";

            var mainNca = title.Value.MainNca;

            if (mainNca == null)
            {
                AnsiConsole.MarkupLine($"[[[red]ERROR[/]]] -> {phase}");
                return 1;
            }

            if (!_settings.Quiet)
            {
                AnsiConsole.MarkupLine($"[[[green]DONE[/]]] -> {phase} ({mainNca.NcaId})");
            }

            // Get info from MainNca
            
            _titleId = mainNca.Nca.Header.TitleId.ToString("X16");
            _hasTitleKeyCrypto = mainNca.Nca.Header.HasRightsId;
            _headerSignatureValidatity = mainNca.Nca.VerifyHeaderSignature();

            if (mainNca.Nca.Header.DistributionType != DistributionType.Download)
            {
                AnsiConsole.MarkupLine($"[[[red]ERROR[/]]] -> {phase} - Wrong distribution type {mainNca.Nca.Header.DistributionType}");
                return -1;
            }

            if (tickets.Length > 0 && !_hasTitleKeyCrypto)
            {
                AnsiConsole.MarkupLine($"[[[red]WARN[/]]] -> {phase} - Has a ticket but no title key crypto.");
                _possibleUnlocker = true;
                canExtract = false;
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
                
                phase = $"[olive]Validate Ticket[/]";
                
                if (_ticket.SignatureType != TicketSigType.Rsa2048Sha256)
                {
                    AnsiConsole.MarkupLine($"[[[red]ERROR[/]]] -> {phase} - Unsupported ticket signature type {_ticket.SignatureType}");
                    return 1;
                }

                if (_ticket.TitleKeyType != TitleKeyType.Common)
                {
                    AnsiConsole.MarkupLine($"[[[red]WARN[/]]] -> {phase} - Personal ticket type.");
                    _rebuildTicket = true;
                }
                
                var propertyMask = (FixedPropertyFlags)_ticket.PropertyMask;
                
                if(_ticket.PropertyMask != 0)
                {
                    AnsiConsole.MarkupLine($"[[[red]WARN[/]]] -> {phase} - Ticket property mask set ({propertyMask})");
                    _rebuildTicket = true;
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

                if (!_settings.Quiet)
                {
                    AnsiConsole.MarkupLine($"[[[green]DONE[/]]] -> {phase}");
                }
            }
            
            phase = $"[olive]Validate Metadata (CNMT)[/]";

            var cnmt = title.Value.Metadata;
            
            if (cnmt == null)
            {
                AnsiConsole.MarkupLine($"[[[red]ERROR[/]]] -> {phase}");
                return 1;
            }

            if (!_settings.Quiet)
            {
                AnsiConsole.MarkupLine($"[[[green]DONE[/]]] -> {phase}");
            }

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
                        canExtract = false;
                    }
                }
                else
                {
                    if (_rawContents[filename] != contentEntry.Size)
                    {
                        warnings.Add("NSP file-system contains files with sizes that do not match the CNMT. Conversion will fail.");
                        foundContentTree.AddNode("[red][[X]][/] " + filename + " -> " + contentEntry.Type + " (Size Mis-match)");
                        canExtract = false;
                    }
                    else
                    {
                        foundContentTree.AddNode("[green][[V]][/] " +filename + " -> " + contentEntry.Type);
                    }
                }
            }
            if (!_settings.Quiet)
            {
                AnsiConsole.Write(new Padder(foundContentTree).PadLeft(1));
            }

            if (!(_settings.Rename && _settings.SkipValidation))
            {
                phase = $"[olive]Validate NCAs[/]";
                
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
                            var logger = new NsfwProgressLogger();
                            
                            ctx.Status($"Validating: {fsNca.Filename}");
                            var validity = NsfwUtilities.VerifyNca(fsNca, logger);

                            var node = new TreeNode(new Markup($"{fsNca.Filename} ({fsNca.Nca.Header.ContentType})"));

                            if (validity != Validity.Valid)
                            {
                                canExtract = false;
                                warnings.Add("NSP file-system contains corrupt NCA files. Conversion will fail.");
                                node.AddNodes(logger.GetReport());
                                foundNcaTree.AddNode(node);
                            }
                            else
                            {
                                node.AddNodes(logger.GetReport());
                                foundNcaTree.AddNode(node);
                            }
                        }

                        if (!_settings.Quiet)
                        {
                            AnsiConsole.MarkupLine($"[[[green]DONE[/]]] -> {phase}");
                            AnsiConsole.Write(new Padder(foundNcaTree).PadLeft(1).PadTop(0).PadBottom(1));
                        }

                        return 0;
                    });
            }
            else
            {
                AnsiConsole.MarkupLine($"[[[red]WARN[/]]] -> Skipping NCA validation.");
            }
            
            phase = $"[olive]Validate Headers[/]";

            foreach (var fsNca in title.Value.Ncas)
            {
                _headerSignatureValidatity = fsNca.Nca.VerifyHeaderSignature();

                if (_headerSignatureValidatity == Validity.Valid) continue;
                
                AnsiConsole.MarkupLine($"[[[red]WARN[/]]] -> {phase} - Header signature is invalid.");
                canExtract = false;
                break;
            }

            var type = _titleType switch
            {
                "PATCH" => "Update",
                "APPLICATION" => "Game",
                "ADDONCONTENT" => "DLC",
                "DELTA" => "DLC Update",
                _ => "UNKNOWN"
            };
            
            var nspLanguageId = -1;
            var titles = new List<TitleInfo>();
            if (title.Value.Control.Value.Title.Items != null)
            {
                foreach (var titleItem in title.Value.Control.Value.Title.Items)
                {
                    nspLanguageId++;

                    if (titleItem.NameString.IsEmpty())
                    {
                        continue;
                    }
                    
                    titles.Add(new TitleInfo
                    {
                        Title = titleItem.NameString.ToString() ?? "UNKNOWN",
                        RegionLanguage = (NacpLanguage) nspLanguageId,
                        Publisher = titleItem.PublisherString.ToString() ?? "UNKNOWN",
                    });
                }
            }

            if (titles.Count != 0)
            {
                _title = titles[0].Title;
            }
            
            if (!string.IsNullOrEmpty(title.Value.Control.Value.DisplayVersionString.ToString()))
            {
                _version = title.Value.Control.Value.DisplayVersionString.ToString()!.Trim();
            }

            if (_hasTitleKeyCrypto && _settings.TicketInfo)
            {
                var tikTable = new Table
                {
                    ShowHeaders = false
                };
                tikTable.AddColumn("Property");
                tikTable.AddColumn("Value");
                
                NsfwUtilities.FormatTicket(tikTable, _ticket);
                
                AnsiConsole.Write(new Padder(tikTable).PadLeft(1).PadRight(0).PadBottom(0).PadTop(0));
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
            
            var quietTable = new Table
            {
                ShowHeaders = false
            };
            quietTable.AddColumn("Property");
            quietTable.AddColumn("Value");

            if(_title == "UNKNOWN" && _fromTitleDb)
            {
                table.AddRow("Display Title", _titleDbTitle.ReplaceLineEndings(string.Empty).EscapeMarkup() + " ([olive]TitleDB[/])");
                _title = _titleDbTitle;
            }
            else
            {
                if (_fromTitleDb)
                {
                    table.AddRow("Display Title", _title + $" (TitleDB: [olive]{_titleDbTitle.EscapeMarkup()}[/])");
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
                            table.AddRow("Display Title", _title.EscapeMarkup() + " ([olive]From Filename[/])");
                        }
                    }
                    else
                    {
                        table.AddRow("Display Title", _title.EscapeMarkup());
                    }
                }
            }

            if (type == "DLC" && File.Exists(titledbPath))
            {
                _parentTitleId = (cnmt.TitleId & 0xFFFFFFFFFFFFF000 ^ 0x1000).ToString("X16");
                
                var parentResult = NsfwUtilities.LookUpTitle(_settings.TitleDbFile, _parentTitleId);

                if (!string.IsNullOrEmpty(parentResult))
                {
                    _parentTitle = parentResult;
                    table.AddRow("Parent Title", parentResult.EscapeMarkup());
                }
            }
            
            if((_settings.RegionalTitles || _settings.RelatedTitles) && File.Exists(titledbPath) && !_settings.Quiet)
            {
                if (_settings.RegionalTitles)
                {
                    var titleResults = NsfwUtilities.GetTitleDbInfo(_settings.TitleDbFile, _baseTitleId != _titleId ? _baseTitleId : _titleId).Result;

                    if (titleResults.Length > 0)
                    {
                        var regionTable = new Table() { ShowHeaders = false };
                        regionTable.AddColumns("Region", "Title");
                        regionTable.AddRow(new Text("Regional Titles"));
                        regionTable.AddEmptyRow();
                        foreach (var titleResult in titleResults.DistinctBy(x => x.RegionLanguage))
                        {
                            regionTable.AddRow(new Markup($"{titleResult.Name!.ReplaceLineEndings(string.Empty).EscapeMarkup()}"), new Markup($"{titleResult.RegionLanguage.ToUpper()}"));
                        }
                        
                        AnsiConsole.Write(new Padder(regionTable).PadLeft(1));
                    }
                }

                if (_settings.RelatedTitles && type == "DLC")
                {
                    var relatedResults = NsfwUtilities.LookUpRelatedTitles(_settings.TitleDbFile, _titleId).Result;

                    if (relatedResults.Length > 0)
                    {
                        var relatedTable = new Table() { ShowHeaders = false };
                        relatedTable.AddColumn("Title");
                        relatedTable.AddRow(new Text("Related DLC Titles"));
                        relatedTable.AddEmptyRow();
                        foreach (var relatedResult in relatedResults.Distinct())
                        {
                            relatedTable.AddRow(new Markup($"{relatedResult.EscapeMarkup()}"));
                        }

                        AnsiConsole.Write(new Padder(relatedTable).PadLeft(1));
                    }
                }
            }
            
            if (_settings.Updates && type == "Game" && File.Exists(_settings.TitleDbFile))
            {
                var versions = NsfwUtilities.LookUpUpdates(_settings.TitleDbFile, _titleId).Result;
                
                if (versions.Length > 0)
                {
                    var tree = new Tree("Updates:");
                    tree.Expanded = true;
                    tree.AddNodes(versions.Select(x => $"v{x.Version} ({x.ReleaseDate})"));
                    AnsiConsole.Write(new Padder(tree).PadLeft(1).PadTop(0).PadBottom(1));
                }
            }

            if (_settings.Updates && type == "Update" && File.Exists(_settings.TitleDbFile))
            {
                var versions = NsfwUtilities.LookUpUpdates(_settings.TitleDbFile, _baseTitleId).Result;
                
                if (versions.Length > 0)
                {
                    var tree = new Tree("Updates:")
                    {
                        Expanded = true
                    };

                    foreach (var version in versions)
                    {
                        if ("v"+version.Version == _titleVersion)
                        {
                            tree.AddNode($"[green]v{version.Version}[/] ({version.ReleaseDate})");
                        }
                        else
                        {
                            tree.AddNode($"v{version.Version} ({version.ReleaseDate})");
                        }
                    }
  
                    AnsiConsole.Write(new Padder(tree).PadLeft(1).PadTop(1).PadBottom(0));
                }
            }

            var languageList = string.Empty;

            if (titles.Count != 0)
            {
                languageList = string.Join(", ", titles.Select(titleInfo => titleInfo.RegionLanguage switch
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
                }));
            }

            table.AddRow("Languages", titles.Count == 0 ? "UNKNOWN" : languageList);

            if (type == "DLC" && !string.IsNullOrEmpty(_parentTitle) && File.Exists(titledbPath))
            {
                var parentLanguages = NsfwUtilities.LookupLanguages(_settings.TitleDbFile, _parentTitleId);
                if (parentLanguages.Length > 0)
                {
                    parentLanguages = string.Join(',',parentLanguages.Split(',').Select(x => string.Concat(x[0].ToString().ToUpper(), x.AsSpan(1))));
                    table.AddRow("Parent Languages", parentLanguages + " ([olive]TitleDB[/])");
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
            table.AddRow("Header Signature", _headerSignatureValidatity == Validity.Valid ? "[green]Valid[/]" : "[red]Invalid[/]");

            if (_titleKeyDec != null)
            {
                table.AddRow("TitleKey (Enc)", _titleKeyEnc.ToHexString());
                table.AddRow("TitleKey (Dec)", _titleKeyDec.ToHexString());
                if (_isFixedSignature)
                {
                    table.AddRow("Ticket Signature", "[olive]Normalised[/]");
                }
                else
                {
                    table.AddRow("Ticket Signature", _isTicketSignatureValid ? "[green]Valid[/]" : "[red]Invalid[/] (Signature Mismatch) - Will generate new ticket.");
                }

                if (_rebuildTicket)
                {
                    table.AddRow("Ticket Validation", "[red]Failed[/] - Will generate new ticket.");
                }
                else
                {
                    table.AddRow("Ticket Validation", "[green]Passed[/]");
                }
                table.AddRow("MasterKey Revision", _masterKeyRevision.ToString());
            }
            
            var formattedName = NsfwUtilities.BuildName(_title, _version, _titleId, _titleVersion, _titleType, _parentTitle, titles, languageMode);
            
            if(_settings.Extract || _settings.Convert || _settings.Rename)
            {
                table.AddRow("Output Name", $"{formattedName.EscapeMarkup()}");
                
                if (_title.Contains('「'))
                {
                    table.AddRow("Trimmed Name", NsfwUtilities.BuildName(NsfwUtilities.TrimTitle(_title), _version, _titleId, _titleVersion, _titleType, _parentTitle, titles).EscapeMarkup()); 
                }
            }

            var titleLength = (formattedName + ".nsp").Length;
            
            if ((_settings.Extract || _settings.Convert || _settings.Rename) && titleLength > 110)
            {
                var message = $"Output name length ({titleLength}) is greater than 110 chars which could cause path length issues on Windows.";
                table.AddRow("[olive]Attention[/]", message);
                quietTable.AddRow("[olive]Attention[/]", message);
            }

            if (_possibleUnlocker && type == "DLC")
            {
                warnings.Add("This appears to be a [olive]Homebrew DLC Unlocker[/]. Conversion will lose the ticket + cert.");
            }

            if (_metaMissing)
            {
                if (_metaMissingNonDelta)
                {
                    warnings.Add("NSP file-system is missing files listed in the CNMT. Conversion will fail.");
                }
                else
                {
                    notices.Add("NSP file-system is missing delta fragments listed in CNMT. These errors can be ignored if you do not need them.");
                }
            }
            
            if(warnings.Count > 0 || notices.Count > 0)
            {
                table.AddRow(string.Empty, string.Empty);
                quietTable.AddRow(string.Empty, string.Empty);
            }

            foreach (var warning in warnings)
            {
                table.AddRow("[red]Warning[/]", warning);
                quietTable.AddRow("[red]Warning[/]", warning);
            }

            foreach (var notice in notices)
            {
                table.AddRow("[olive]Notice[/]", notice);
                quietTable.AddRow("[olive]Notice[/]", notice);
            }

            AnsiConsole.Write(!_settings.Quiet ? new Padder(table).PadLeft(1).PadTop(0) : new Padder(quietTable).PadLeft(1).PadTop(0));
            
            if (!_settings.Extract && !_settings.Convert && !_settings.Rename)
            {
                if(warnings.Count > 0 || _headerSignatureValidatity != Validity.Valid || (_hasTitleKeyCrypto && !_isTicketSignatureValid) || _rebuildTicket)
                {
                    AnsiConsole.MarkupLine($"[[[red]WARN[/]]] - NSP Validation failed. Conversion or Extraction would fail.");
                    return 1;
                }
                return 0;
            }
            
            if (!canExtract)
            {
                AnsiConsole.MarkupLine($"[[[red]WARN[/]]] - NSP Validation failed.");
                return 1;
            }

            if (_settings.Rename)
            {
                fs.Dispose();
                file.Dispose();

                if (inputFilename == formattedName+".nsp")
                {
                    AnsiConsole.MarkupLine($"[[[green]DONE[/]]] -> Renaming skipped. Nothing to do. Filename matches already.");
                    return 0;
                }
                
                var targetDirectory = System.IO.Path.GetDirectoryName(nspFullPath);
                
                if(targetDirectory == null || !Directory.Exists(targetDirectory))
                {
                    AnsiConsole.MarkupLine($"[[[red]ERROR[/]]] -> Failed to open directory.");
                    return 1;
                }
                
                var targetName = System.IO.Path.Combine(targetDirectory, formattedName + ".nsp");

                if (targetName.Length > 254)
                {
                    AnsiConsole.MarkupLine($"[[[red]ERROR[/]]] -> Path too long for Windows ({targetName.Length})");
                    return 1;
                }
                
                if (File.Exists(targetName))
                {
                    AnsiConsole.MarkupLine($"[[[red]ERROR[/]]] -> File with the same name already exists. ({formattedName.EscapeMarkup()}.nsp)");
                    return 1;
                }
                
                if (_settings.DryRun)
                {
                    AnsiConsole.MarkupLine($"[[[green]DRYRUN[/]]] -> Rename FROM: [olive]{inputFilename.EscapeMarkup()}[/]");
                    AnsiConsole.MarkupLine($"[[[green]DRYRUN[/]]] ->   Rename TO: [olive]{formattedName.EscapeMarkup()}.nsp[/]");
                    return 0;
                }

                File.Move(nspFullPath, System.IO.Path.Combine(targetDirectory, formattedName + ".nsp"));
                AnsiConsole.MarkupLine($"[[[green]DONE[/]]] -> Renamed TO: [olive]{formattedName.EscapeMarkup()}[/]");
                return 0;
            }
            
            if (_hasTitleKeyCrypto && (!_isTicketSignatureValid || _rebuildTicket))
            {
                _ticket = NsfwUtilities.CreateTicket(_masterKeyRevision, _ticket.RightsId, _titleKeyEnc);
                AnsiConsole.MarkupLine("[[[green]DONE[/]]] -> Generated new normalised ticket.");
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
                if (!_settings.Quiet)
                {
                    AnsiConsole.MarkupLine($"{phase}..");
                }

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
                        builder.AddFile($"{_ticket.RightsId.ToHexString().ToLower()}.tik", new MemoryStorage(_ticket.GetBytes()).AsFile(OpenMode.Read)); 
                        builder.AddFile($"{_ticket.RightsId.ToHexString().ToLower()}.cert", new LocalFile(_settings.CertFile, OpenMode.Read));
                    }
                }

                var targetName = System.IO.Path.Combine(_settings.NspDirectory, $"{formattedName}.nsp");
                
                if (targetName.Length > 254)
                {
                    AnsiConsole.MarkupLine($"[[[red]ERROR[/]]] -> Path too long for Windows ({targetName.Length})");
                    return 1;
                }

                if (targetName == nspFullPath)
                {
                    AnsiConsole.MarkupLine($"[[[red]ERROR[/]]] -> Trying to save converted file to the same location as the input file.");
                    return 1;
                }

                if (_settings.DryRun) return 0;
                
                using var outStream = new FileStream(targetName, FileMode.Create, FileAccess.ReadWrite);
                var builtPfs = builder.Build(PartitionFileSystemType.Standard);
                builtPfs.GetSize(out var pfsSize).ThrowIfFailure();
                builtPfs.CopyToStream(outStream, pfsSize);
                AnsiConsole.MarkupLine($"[[[green]DONE[/]]] -> Converted: [olive]{formattedName.EscapeMarkup()}.nsp[/]");
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

public enum LanguageMode
{
    Full,
    Short,
    None
}

[Flags]
public enum NacpLanguage : uint
{
    AmericanEnglish = 0,
    BritishEnglish = 1,
    Japanese = 2,
    French = 3,
    German = 4,
    LatinAmericanSpanish = 5,
    Spanish = 6,
    Italian = 7,
    Dutch = 8,
    CanadianFrench = 9,
    Portuguese = 10,
    Russian = 11,
    Korean = 12,
    TraditionalChinese = 13,
    SimplifiedChinese = 14,
    BrazilianPortuguese = 15,
}

public record TitleInfo
{
    public string Title { get; init; }
    public string Publisher { get; init; }
    public NacpLanguage RegionLanguage { get; init; }
}

