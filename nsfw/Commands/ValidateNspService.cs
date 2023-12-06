using System.Security.Cryptography;
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
using Serilog;
using Spectre.Console;
using ContentType = LibHac.Ncm.ContentType;
using NcaFsHeader = LibHac.Tools.FsSystem.NcaUtils.NcaFsHeader;

namespace Nsfw.Commands;

public class ValidateNspService
{
    private readonly ValidateNspSettings _settings;
    private readonly KeySet _keySet;
    // private string _titleId;
    // private string _titleVersion;
    // private string _titleType;
    // private bool _hasTitleKeyCrypto;
    // private Ticket _ticket;
    // private byte[] _titleKeyEnc;
    // private byte[] _titleKeyDec;
    // private readonly byte[] _fixedSignature = Enumerable.Repeat((byte)0xFF, 0x100).ToArray();
    // private bool _isTicketSignatureValid;
    // private bool _isFixedSignature;
    // private int _masterKeyRevision;
    // private string _title = "UNKNOWN";
    // private string _version = "UNKNOWN";
    // private bool _possibleUnlocker;
    // private bool _fromTitleDb;
    // private string _baseTitleId;
    // private string _titleDbTitle;
    // private Dictionary<string, long> _rawContents = new();
    // private bool _metaMissing;
    // private bool _metaMissingNonDelta;
    // private string _parentTitle;
    // private Validity _headerSignatureValidatity;
    // private string _parentTitleId;
    // private bool _rebuildTicket;

    public ValidateNspService(ValidateNspSettings settings)
    {
        _settings = settings;
        _keySet = ExternalKeyReader.ReadKeyFile(settings.KeysFile);
        //_parentTitle = string.Empty;
    }

    public int Process(string nspFullPath)
    {
        var nspInfo = new NspInfo(nspFullPath);

        if (_settings.NoLanguages)
        {
            nspInfo.OutputOptions.LanguageMode = LanguageMode.None;
        }
        
        if(_settings.ShortLanguages)
        {
            nspInfo.OutputOptions.LanguageMode = LanguageMode.Short;
        }
        
        Log.Information($"Validating NSP : [olive]{nspInfo.FileName.EscapeMarkup()}[/]");

        AnsiConsole.WriteLine("----------------------------------------");
        
        var localFile = new LocalFile(nspInfo.FilePath, OpenMode.Read);
        var headerBuffer = new Span<byte>(new byte[4]);
        localFile.Read(out var bytesRead, 0, headerBuffer);
        
        if(headerBuffer.ToHexString() != nspInfo.HeaderMagic)
        {
            Log.Error("Cannot mount file-system. Invalid NSP file.");
            return 1;
        }

        var fileStorage = new FileStorage(localFile);
        var fileSystem = new PartitionFileSystem();
        fileSystem.Initialize(fileStorage);

        var phase = "[olive]Import Tickets[/]";
        
        foreach (var rawFile in fileSystem.EnumerateEntries("*.*", SearchOptions.RecurseSubdirectories))
        {
            if (rawFile.Name.EndsWith(".tik"))
            {
                var tikFile = new UniqueRef<IFile>();
                fileSystem.OpenFile(ref tikFile, rawFile.FullPath.ToU8Span(), OpenMode.Read);
                ImportTicket(new Ticket(tikFile.Get.AsStream()), _keySet, nspInfo);
                Log.Verbose($"{phase} - Ticket ({rawFile.Name}) imported.");
            }
            
            nspInfo.RawFileEntries.Add(rawFile.Name, new RawContentFile { Name = rawFile.Name, Size = rawFile.Size, Type = rawFile.Type, BlockCount = (int)BitUtil.DivideUp(rawFile.Size, nspInfo.DefaultBlockSize) });
        }

        if (!nspInfo.HasTicket)
        {
            Log.Verbose($"{phase} - No valid tickets found.");
        }

        phase = "[olive]NSP File-System[/]";
        
        var switchFs = SwitchFs.OpenNcaDirectory(_keySet, fileSystem);
        
        if (switchFs == null)
        {
            Log.Error($"{phase} - Failed to open NSP as SwitchFS.");
            return 1;
        }
        
        Log.Verbose($"{phase} - Loaded correctly.");

        phase = "[olive]Validate NSP[/]";
        
        if (switchFs.Applications.Count != 1)
        {
            Log.Error($"{phase} - Expected 1 Application, found {switchFs.Applications.Count}");
            return 1;
        }
        
        if (switchFs.Applications.Count != switchFs.Titles.Count)
        {
            nspInfo.Warnings.Add($"{phase} - Title count ({switchFs.Titles.Count}) does not match Application count ({switchFs.Applications.Count})");
        }

        var title = switchFs.Titles.First().Value;
        
        var mainNca = title.MainNca;
        
        phase = "[olive]Validate Main NCA[/]";
        
        if (mainNca == null)
        {
            Log.Error($"{phase} - Failed to open Main NCA.");
            return 1;
        }
        
        nspInfo.BaseTitleId = mainNca.Nca.Header.TitleId.ToString("X16");
        nspInfo.HasTitleKeyCrypto = mainNca.Nca.Header.HasRightsId;
        
        if (mainNca.Nca.Header.DistributionType != DistributionType.Download)
        {
            Log.Error($"{phase} - Unsupported distribution type : {mainNca.Nca.Header.DistributionType}");
            return -1;
        }
        
        if (nspInfo is { HasTicket: true, HasTitleKeyCrypto: false })
        {
            nspInfo.Warnings.Add($"{phase} - NSP has ticket but no title key crypto. This is possibly a Homebrew DLC unlocker.");
            nspInfo.PossibleDlcUnlocker = true;
            nspInfo.CanConvert = false;
        }
        
        if(nspInfo is { HasTicket: false, HasTitleKeyCrypto: true })
        {
            nspInfo.Warnings.Add($"{phase} - NSP is TitleKey encrypted but no valid ticket found.");
            nspInfo.CanConvert = false;
        }
        
        if (nspInfo.HasTitleKeyCrypto)
        {
            if (mainNca.Nca.Header.RightsId.IsZeros())
            {
                Log.Error($"{phase} - NCA is encrypted but has empty rights ID.");
                return 1; 
            }
            
            phase = $"[olive]Validate Ticket[/]";
            
            if (nspInfo.Ticket!.SignatureType != TicketSigType.Rsa2048Sha256)
            {
                Log.Error($"{phase} - Unsupported ticket signature type {nspInfo.Ticket!.SignatureType}");
                return 1;
            }
        
            if (nspInfo.Ticket!.TitleKeyType != TitleKeyType.Common)
            {
                nspInfo.Warnings.Add($"{phase} - Personal ticket type found.");
                nspInfo.GenerateNewTicket = true;
            }
            
            var propertyMask = (FixedPropertyFlags)nspInfo.Ticket!.PropertyMask;
            
            if(nspInfo.Ticket!.PropertyMask != 0)
            {
                nspInfo.Warnings.Add($"{phase} - Ticket has property mask set ({propertyMask}).");
                nspInfo.GenerateNewTicket = true;
            }
        
            if (nspInfo.Ticket!.AccountId != 0)
            {
                nspInfo.Warnings.Add($"{phase} - Ticket has account ID set ({nspInfo.Ticket!.AccountId})");
                nspInfo.GenerateNewTicket = true;
            }
            
            if(nspInfo.Ticket!.DeviceId != 0)
            {
                AnsiConsole.MarkupLine($"[[[red]WARN[/]]] -> {phase} - Ticket has device ID set ({nspInfo.Ticket!.DeviceId})");
                nspInfo.GenerateNewTicket = true;
            }
            
            nspInfo.TitleKeyEncrypted = nspInfo.Ticket.GetTitleKey(_keySet);
            nspInfo.TitleKeyDecrypted = mainNca.Nca.GetDecryptedTitleKey();
            
            if (nspInfo.NormalisedSignature.ToHexString() == nspInfo.Ticket!.Signature.ToHexString())
            {
                nspInfo.IsTicketSignatureValid = true;
                nspInfo.IsNormalisedSignature = true;
            }
            else
            {
                nspInfo.IsTicketSignatureValid = NsfwUtilities.ValidateTicket(nspInfo.Ticket!, _settings.CertFile);
            }
            
            nspInfo.MasterKeyRevision = Utilities.GetMasterKeyRevision(mainNca.Nca.Header.KeyGeneration);
        
            var ticketMasterKey = Utilities.GetMasterKeyRevision(nspInfo.Ticket!.RightsId.Last());
        
            if (nspInfo.MasterKeyRevision != ticketMasterKey)
            {
                Log.Error($"{phase} - Invalid rights ID key generation! Got {ticketMasterKey}, expected {nspInfo.MasterKeyRevision}.");
                return 1;
            }
        }
        
        phase = $"[olive]Validate Metadata (CNMT)[/]";
            
        var cnmt = title.Metadata;
            
        if (cnmt == null)
        {
            Log.Error($"{phase} - Unable to load CNMT.");
            return 1;
        }
            
        if (cnmt.TitleId.ToString("X16") != nspInfo.BaseTitleId && title.Metadata.Type == ContentMetaType.Application)
        {
            nspInfo.Warnings.Add($"{phase} - TitleID Mis-match. Expected {nspInfo.BaseTitleId}, found {cnmt.TitleId:X16}");
        }
        
        nspInfo.TitleId = cnmt.TitleId.ToString("X16");
        nspInfo.TitleVersion = $"v{cnmt.TitleVersion.Version}";
        nspInfo.TitleType = cnmt.Type;
        
        if (nspInfo.TitleType != ContentMetaType.Patch && nspInfo.TitleType != ContentMetaType.Application && nspInfo.TitleType != ContentMetaType.Delta && nspInfo.TitleType != ContentMetaType.AddOnContent)
        {
            Log.Error($"{phase} - Unsupported type {nspInfo.TitleType}");
            return 1;
        }
        
        foreach (var contentEntry in cnmt.ContentEntries)
        {
            var contentFile = new ContentFile
            {
                FileName = $"{contentEntry.NcaId.ToHexString().ToLower()}.nca",
                NcaId = contentEntry.NcaId.ToHexString(),
                Hash = contentEntry.Hash,
                Type = contentEntry.Type
            };
            
            if(contentFile.NcaId != contentFile.Hash.Take(16).ToArray().ToHexString())
            {
                Log.Error($"{phase} - Hash part should match NCA ID ({contentFile.NcaId}).");
                return 1;
            }
        
            if (!nspInfo.RawFileEntries.ContainsKey(contentFile.FileName))
            {
                contentFile.IsMissing = true;

                if (contentFile.Type != ContentType.DeltaFragment)
                {
                    nspInfo.Warnings.Add("NSP file-system is missing content file " + contentFile.FileName);
                    nspInfo.CanExtract = false;
                }
            }
            else
            {
                if (nspInfo.RawFileEntries[contentFile.FileName].Size != contentEntry.Size)
                {
                    nspInfo.Warnings.Add("NSP file-system contains files with sizes that do not match the CNMT. Conversion will fail.");
                    nspInfo.CanConvert = false;
                }
            }
            
            nspInfo.ContentFiles.Add(contentFile.FileName, contentFile);
        }

        if (!(_settings is { Rename: true, SkipValidation: true }))
        {
            phase = $"[olive]Validate NCAs[/]";
            
            if (title.Ncas.Count == 0)
            {
                Log.Error($"{phase} - No NCAs found.");
                return 1;
            }

            foreach (var fsNca in title.Ncas)
            {
                var ncaInfo = new NcaInfo(fsNca.Filename)
                {
                    IsHeaderValid = fsNca.Nca.VerifyHeaderSignature() == Validity.Valid
                };

                ncaInfo.Type = fsNca.Nca.Header.ContentType;

                for (var i = 0; i < 4; i++)
                {
                    if (!fsNca.Nca.Header.IsSectionEnabled(i) || !fsNca.Nca.CanOpenSection(i))
                    {
                        continue;
                    }

                    var sectionInfo = new NcaSectionInfo(i);

                    NcaFsHeader header = default;
                    
                    try
                    {
                        header = fsNca.Nca.GetFsHeader(i);
                        sectionInfo.EncryptionType = header.EncryptionType;
                        sectionInfo.FormatType = header.FormatType;
                    }
                    catch (Exception exception)
                    {
                        sectionInfo.IsErrored = true;
                        sectionInfo.ErrorMessage = $"Unable to open header - {exception.Message}";
                    }

                    if (header.IsPatchSection())
                    {
                        sectionInfo.IsPatchSection = true;

                        //TODO: Save encryption details
                        //var patchInfo = header.GetPatchInfo();
                    }
                    else
                    {
                        try
                        {
                            fsNca.Nca.OpenFileSystem(i, IntegrityCheckLevel.ErrorOnInvalid);
                        }
                        catch (Exception exception)
                        {
                            sectionInfo.IsErrored = true;
                            sectionInfo.ErrorMessage = $"Error opening file-system - {exception.Message}";
                        }
                    }
                    ncaInfo.Sections.Add(i, sectionInfo);
                }

                if (ncaInfo.Type != NcaContentType.Meta && (_settings.ForceHash || (!ncaInfo.IsErrored && nspInfo.HeaderSignatureValidity == Validity.Valid)))
                {
                    if (_settings.ForceHash)
                    {
                        Log.Warning($"{phase} - Forcing hash check.");
                    }
                    
                    var rawFile = nspInfo.RawFileEntries[fsNca.Filename]; //TODO: Check if exists?
                    
                    var size = rawFile.Size;
                    
                    if (size == 0)
                    {
                        continue;
                    }

                    var blockCount = rawFile.BlockCount;
                    
                    var expectedHash = nspInfo.ContentFiles[fsNca.Filename].Hash.ToHexString();
                    
                    AnsiConsole.Progress()
                        .AutoClear(true)   // Do not remove the task list when done
                        .HideCompleted(true)   // Hide tasks as they are completed
                        .Columns(new ProgressColumn[] 
                        {
                            new SpinnerColumn(),
                            new TaskDescriptionColumn(),    // Task description
                            //new ProgressBarColumn(),      // Progress bar
                            new PercentageColumn()          // Percentage
                            //new RemainingTimeColumn(),    // Remaining time
                        })
                        .Start(ctx => 
                        {
                            var hashTask = ctx.AddTask($"Hashing ..", new ProgressTaskSettings { MaxValue = blockCount });

                            while(!ctx.IsFinished) 
                            {
                                var sha256 = SHA256.Create();
                                
                                var ncaFile = new UniqueRef<IFile>();
                                fileSystem.OpenFile(ref ncaFile, ("/"+ncaInfo.FileName).ToU8Span(), OpenMode.Read);

                                var ncaStream = ncaFile.Get.AsStream();
                                var buffer = new byte[0x4000];
                                int read;
                                while ((read = ncaStream.Read(buffer, 0, buffer.Length)) > 0)
                                {
                                    sha256.TransformBlock(buffer, 0, read, null, 0);
                                    hashTask.Increment(1);
                                }
                                
                                sha256.TransformFinalBlock(Array.Empty<byte>(), 0, 0);
                                var ncaHash = sha256.Hash.ToHexString();
                                ncaInfo.HashMatch = ncaHash == expectedHash;
                                
                                if(!ncaInfo.HashMatch)
                                {
                                    Log.Error($"Hash mismatch for NCA {ncaInfo.FileName}");
                                }
                                
                            }
                        });
                }
                
                nspInfo.NcaFiles.Add(fsNca.Filename, ncaInfo);
            }

            if (nspInfo.HeaderSignatureValidity == Validity.Valid && nspInfo.NcaFiles.Values.Any(x => !x.IsErrored) && nspInfo.NcaFiles.Values.Any(x => x.HashMatch))
            {
                Log.Verbose($"{phase} - All NCAs validated correctly.");
            }
            else
            {
                Log.Warning($"{phase} - NSP contains some invalid NCAs.");
                nspInfo.CanConvert = false;
            }
            
            //AnsiConsole.Status()
            //    .Start("Validating NCAs...", ctx =>
            //    {
            //        ctx.Spinner(Spinner.Known.Line);
            //        ctx.SpinnerStyle(Style.Parse("green"));
        

        
                    //var foundNcaTree = new Tree("NCAs:");
                    //foundNcaTree.Expanded = true;
        
                    //foreach (var fsNca in title.Ncas)
                    //{
                    //    var logger = new NsfwProgressLogger();
                        
                    //    ctx.Status($"Validating: {fsNca.Filename}");
                    //    var validity = NsfwUtilities.VerifyNca(fsNca, logger);
        
                        //var node = new TreeNode(new Markup($"{fsNca.Filename} ({fsNca.Nca.Header.ContentType})"));
        
                        // if (validity != Validity.Valid)
                        // {
                        //     canExtract = false;
                        //     warnings.Add("NSP file-system contains corrupt NCA files. Conversion will fail.");
                        //     node.AddNodes(logger.GetReport());
                        //     foundNcaTree.AddNode(node);
                        // }
                        // else
                        // {
                        //     node.AddNodes(logger.GetReport());
                        //     foundNcaTree.AddNode(node);
                        // }
                    //}
        
                    //return 0;
                //});
        }
        else
        {
            Log.Warning("Skipping NCA validation.");
        }
    //         
    //         phase = $"[olive]Validate Headers[/]";
    //
    //         foreach (var fsNca in title.Value.Ncas)
    //         {
    //             _headerSignatureValidatity = fsNca.Nca.VerifyHeaderSignature();
    //
    //             if (_headerSignatureValidatity == Validity.Valid) continue;
    //             
    //             AnsiConsole.MarkupLine($"[[[red]WARN[/]]] -> {phase} - Header signature is invalid.");
    //             canExtract = false;
    //             break;
    //         }
    //
    //         var type = _titleType switch
    //         {
    //             "PATCH" => "Update",
    //             "APPLICATION" => "Game",
    //             "ADDONCONTENT" => "DLC",
    //             "DELTA" => "DLC Update",
    //             _ => "UNKNOWN"
    //         };
    //         
    //         var nspLanguageId = -1;
    //         var titles = new List<TitleInfo>();
    //         if (title.Value.Control.Value.Title.Items != null)
    //         {
    //             foreach (var titleItem in title.Value.Control.Value.Title.Items)
    //             {
    //                 nspLanguageId++;
    //
    //                 if (titleItem.NameString.IsEmpty())
    //                 {
    //                     continue;
    //                 }
    //                 
    //                 titles.Add(new TitleInfo
    //                 {
    //                     Title = titleItem.NameString.ToString() ?? "UNKNOWN",
    //                     RegionLanguage = (NacpLanguage) nspLanguageId,
    //                     Publisher = titleItem.PublisherString.ToString() ?? "UNKNOWN",
    //                 });
    //             }
    //         }
    //
    //         if (titles.Count != 0)
    //         {
    //             _title = titles[0].Title;
    //         }
    //         
    //         if (!string.IsNullOrEmpty(title.Value.Control.Value.DisplayVersionString.ToString()))
    //         {
    //             _version = title.Value.Control.Value.DisplayVersionString.ToString()!.Trim();
    //         }
    //
    //         if (_hasTitleKeyCrypto && _settings.TicketInfo)
    //         {
    //             var tikTable = new Table
    //             {
    //                 ShowHeaders = false
    //             };
    //             tikTable.AddColumn("Property");
    //             tikTable.AddColumn("Value");
    //             
    //             NsfwUtilities.FormatTicket(tikTable, _ticket);
    //             
    //             AnsiConsole.Write(new Padder(tikTable).PadLeft(1).PadRight(0).PadBottom(0).PadTop(0));
    //         }
    //         
    //         var titledbPath = System.IO.Path.GetFullPath(_settings.TitleDbFile);
    //         
    //         if((_title == "UNKNOWN" || _settings.VerifyTitle) && File.Exists(titledbPath))
    //         {
    //             NsfwUtilities.LookUpTitle(titledbPath, _baseTitleId != _titleId ? _baseTitleId : _titleId, out _titleDbTitle, out _fromTitleDb);
    //         }
    //         
    //         var table = new Table
    //         {
    //             ShowHeaders = false
    //         };
    //         table.AddColumn("Property");
    //         table.AddColumn("Value");
    //         
    //         var quietTable = new Table
    //         {
    //             ShowHeaders = false
    //         };
    //         quietTable.AddColumn("Property");
    //         quietTable.AddColumn("Value");
    //
    //         if(_title == "UNKNOWN" && _fromTitleDb)
    //         {
    //             table.AddRow("Display Title", _titleDbTitle.ReplaceLineEndings(string.Empty).EscapeMarkup() + " ([olive]TitleDB[/])");
    //             _title = _titleDbTitle;
    //         }
    //         else
    //         {
    //             if (_fromTitleDb)
    //             {
    //                 table.AddRow("Display Title", _title + $" (TitleDB: [olive]{_titleDbTitle.EscapeMarkup()}[/])");
    //             }
    //             else
    //             {
    //                 if (_title == "UNKNOWN" && type == "DLC" && inputFilename.Contains('['))
    //                 {
    //                     var filenameParts = inputFilename.Split('[', StringSplitOptions.TrimEntries);
    //                     
    //                     if(filenameParts.Length > 1)
    //                     {
    //                         _title = filenameParts[0];
    //
    //                         if (!char.IsDigit(filenameParts[1][0]) && filenameParts.Length > 2)
    //                         {
    //                             _title += " - " + filenameParts[1].Replace("]",String.Empty).Trim();
    //                         }
    //                         table.AddRow("Display Title", _title.EscapeMarkup() + " ([olive]From Filename[/])");
    //                     }
    //                 }
    //                 else
    //                 {
    //                     table.AddRow("Display Title", _title.EscapeMarkup());
    //                 }
    //             }
    //         }
    //
    //         if (type == "DLC" && File.Exists(titledbPath))
    //         {
    //             _parentTitleId = (cnmt.TitleId & 0xFFFFFFFFFFFFF000 ^ 0x1000).ToString("X16");
    //             
    //             var parentResult = NsfwUtilities.LookUpTitle(_settings.TitleDbFile, _parentTitleId);
    //
    //             if (!string.IsNullOrEmpty(parentResult))
    //             {
    //                 _parentTitle = parentResult;
    //                 table.AddRow("Parent Title", parentResult.EscapeMarkup());
    //             }
    //         }
    //         
    //         if((_settings.RegionalTitles || _settings.RelatedTitles) && File.Exists(titledbPath) && !_settings.Quiet)
    //         {
    //             if (_settings.RegionalTitles)
    //             {
    //                 var titleResults = NsfwUtilities.GetTitleDbInfo(_settings.TitleDbFile, _baseTitleId != _titleId ? _baseTitleId : _titleId).Result;
    //
    //                 if (titleResults.Length > 0)
    //                 {
    //                     var regionTable = new Table() { ShowHeaders = false };
    //                     regionTable.AddColumns("Region", "Title");
    //                     regionTable.AddRow(new Text("Regional Titles"));
    //                     regionTable.AddEmptyRow();
    //                     foreach (var titleResult in titleResults.DistinctBy(x => x.RegionLanguage))
    //                     {
    //                         regionTable.AddRow(new Markup($"{titleResult.Name!.ReplaceLineEndings(string.Empty).EscapeMarkup()}"), new Markup($"{titleResult.RegionLanguage.ToUpper()}"));
    //                     }
    //                     
    //                     AnsiConsole.Write(new Padder(regionTable).PadLeft(1));
    //                 }
    //             }
    //
    //             if (_settings.RelatedTitles && type == "DLC")
    //             {
    //                 var relatedResults = NsfwUtilities.LookUpRelatedTitles(_settings.TitleDbFile, _titleId).Result;
    //
    //                 if (relatedResults.Length > 0)
    //                 {
    //                     var relatedTable = new Table() { ShowHeaders = false };
    //                     relatedTable.AddColumn("Title");
    //                     relatedTable.AddRow(new Text("Related DLC Titles"));
    //                     relatedTable.AddEmptyRow();
    //                     foreach (var relatedResult in relatedResults.Distinct())
    //                     {
    //                         relatedTable.AddRow(new Markup($"{relatedResult.EscapeMarkup()}"));
    //                     }
    //
    //                     AnsiConsole.Write(new Padder(relatedTable).PadLeft(1));
    //                 }
    //             }
    //         }
    //         
    //         if (_settings.Updates && type == "Game" && File.Exists(_settings.TitleDbFile))
    //         {
    //             var versions = NsfwUtilities.LookUpUpdates(_settings.TitleDbFile, _titleId).Result;
    //             
    //             if (versions.Length > 0)
    //             {
    //                 var tree = new Tree("Updates:");
    //                 tree.Expanded = true;
    //                 tree.AddNodes(versions.Select(x => $"v{x.Version} ({x.ReleaseDate})"));
    //                 AnsiConsole.Write(new Padder(tree).PadLeft(1).PadTop(0).PadBottom(1));
    //             }
    //         }
    //
    //         if (_settings.Updates && type == "Update" && File.Exists(_settings.TitleDbFile))
    //         {
    //             var versions = NsfwUtilities.LookUpUpdates(_settings.TitleDbFile, _baseTitleId).Result;
    //             
    //             if (versions.Length > 0)
    //             {
    //                 var tree = new Tree("Updates:")
    //                 {
    //                     Expanded = true
    //                 };
    //
    //                 foreach (var version in versions)
    //                 {
    //                     if ("v"+version.Version == _titleVersion)
    //                     {
    //                         tree.AddNode($"[green]v{version.Version}[/] ({version.ReleaseDate})");
    //                     }
    //                     else
    //                     {
    //                         tree.AddNode($"v{version.Version} ({version.ReleaseDate})");
    //                     }
    //                 }
    //
    //                 AnsiConsole.Write(new Padder(tree).PadLeft(1).PadTop(1).PadBottom(0));
    //             }
    //         }
    //
    //         var languageList = string.Empty;
    //
    //         if (titles.Count != 0)
    //         {
    //             languageList = string.Join(", ", titles.Select(titleInfo => titleInfo.RegionLanguage switch
    //             {
    //                 NacpLanguage.AmericanEnglish => "English (America)",
    //                 NacpLanguage.BritishEnglish => "English (Great Britain)",
    //                 NacpLanguage.Japanese => "Japanese",
    //                 NacpLanguage.French => "French (France)",
    //                 NacpLanguage.CanadianFrench => "French (Canada)",
    //                 NacpLanguage.German => "German",
    //                 NacpLanguage.Italian => "Italian",
    //                 NacpLanguage.Spanish => "Spanish (Spain)",
    //                 NacpLanguage.LatinAmericanSpanish => "Spanish (Latin America)",
    //                 NacpLanguage.SimplifiedChinese => "Chinese (Simplified)",
    //                 NacpLanguage.TraditionalChinese => "Chinese (Traditional)",
    //                 NacpLanguage.Korean => "Korean",
    //                 NacpLanguage.Dutch => "Dutch",
    //                 NacpLanguage.Portuguese => "Portuguese (Portugal)",
    //                 NacpLanguage.BrazilianPortuguese => "Portuguese (Brazil)",
    //                 NacpLanguage.Russian => "Russian",
    //                 _ => "Unknown"
    //             }));
    //         }
    //
    //         table.AddRow("Languages", titles.Count == 0 ? "UNKNOWN" : languageList);
    //
    //         if (type == "DLC" && !string.IsNullOrEmpty(_parentTitle) && File.Exists(titledbPath))
    //         {
    //             var parentLanguages = NsfwUtilities.LookupLanguages(_settings.TitleDbFile, _parentTitleId);
    //             if (parentLanguages.Length > 0)
    //             {
    //                 parentLanguages = string.Join(',',parentLanguages.Split(',').Select(x => string.Concat(x[0].ToString().ToUpper(), x.AsSpan(1))));
    //                 table.AddRow("Parent Languages", parentLanguages + " ([olive]TitleDB[/])");
    //             }
    //         }
    //         
    //         table.AddRow("Display Version", _version);
    //
    //         if (_baseTitleId != _titleId)
    //         {
    //             table.AddRow("Base Title ID", _baseTitleId);
    //         }
    //
    //         table.AddRow("Title ID", _titleId);
    //         table.AddRow("Title Type", type + " (" + _titleType + ")");
    //         table.AddRow("Title Version", _titleVersion == "v0" ? "BASE (v0)" : _titleVersion);
    //         table.AddRow("Rights ID", mainNca.Nca.Header.RightsId.IsZeros() ? "EMPTY" : mainNca.Nca.Header.RightsId.ToHexString());
    //         table.AddRow("Header Signature", _headerSignatureValidatity == Validity.Valid ? "[green]Valid[/]" : "[red]Invalid[/]");
    //
    //         if (_titleKeyDec != null)
    //         {
    //             table.AddRow("TitleKey (Enc)", _titleKeyEnc.ToHexString());
    //             table.AddRow("TitleKey (Dec)", _titleKeyDec.ToHexString());
    //             if (_isFixedSignature)
    //             {
    //                 table.AddRow("Ticket Signature", "[olive]Normalised[/]");
    //             }
    //             else
    //             {
    //                 table.AddRow("Ticket Signature", _isTicketSignatureValid ? "[green]Valid[/]" : "[red]Invalid[/] (Signature Mismatch) - Will generate new ticket.");
    //             }
    //
    //             if (_rebuildTicket)
    //             {
    //                 table.AddRow("Ticket Validation", "[red]Failed[/] - Will generate new ticket.");
    //             }
    //             else
    //             {
    //                 table.AddRow("Ticket Validation", "[green]Passed[/]");
    //             }
    //             table.AddRow("MasterKey Revision", _masterKeyRevision.ToString());
    //         }
    //         
    //         var formattedName = NsfwUtilities.BuildName(_title, _version, _titleId, _titleVersion, _titleType, _parentTitle, titles, languageMode);
    //         
    //         if(_settings.Extract || _settings.Convert || _settings.Rename)
    //         {
    //             table.AddRow("Output Name", $"{formattedName.EscapeMarkup()}");
    //             
    //             if (_title.Contains('「'))
    //             {
    //                 table.AddRow("Trimmed Name", NsfwUtilities.BuildName(NsfwUtilities.TrimTitle(_title), _version, _titleId, _titleVersion, _titleType, _parentTitle, titles).EscapeMarkup()); 
    //             }
    //         }
    //
    //         var titleLength = (formattedName + ".nsp").Length;
    //         
    //         if ((_settings.Extract || _settings.Convert || _settings.Rename) && titleLength > 110)
    //         {
    //             var message = $"Output name length ({titleLength}) is greater than 110 chars which could cause path length issues on Windows.";
    //             table.AddRow("[olive]Attention[/]", message);
    //             quietTable.AddRow("[olive]Attention[/]", message);
    //         }
    //
    //         if (_possibleUnlocker && type == "DLC")
    //         {
    //             warnings.Add("This appears to be a [olive]Homebrew DLC Unlocker[/]. Conversion will lose the ticket + cert.");
    //         }
    //
    //         if (_metaMissing)
    //         {
    //             if (_metaMissingNonDelta)
    //             {
    //                 warnings.Add("NSP file-system is missing files listed in the CNMT. Conversion will fail.");
    //             }
    //             else
    //             {
    //                 notices.Add("NSP file-system is missing delta fragments listed in CNMT. These errors can be ignored if you do not need them.");
    //             }
    //         }
    //         
    //         if(warnings.Count > 0 || notices.Count > 0)
    //         {
    //             table.AddRow(string.Empty, string.Empty);
    //             quietTable.AddRow(string.Empty, string.Empty);
    //         }
    //
    //         foreach (var warning in warnings)
    //         {
    //             table.AddRow("[red]Warning[/]", warning);
    //             quietTable.AddRow("[red]Warning[/]", warning);
    //         }
    //
    //         foreach (var notice in notices)
    //         {
    //             table.AddRow("[olive]Notice[/]", notice);
    //             quietTable.AddRow("[olive]Notice[/]", notice);
    //         }
    //
    //         AnsiConsole.Write(!_settings.Quiet ? new Padder(table).PadLeft(1).PadTop(0) : new Padder(quietTable).PadLeft(1).PadTop(0));
    //         
    //         if (!_settings.Extract && !_settings.Convert && !_settings.Rename)
    //         {
    //             if(warnings.Count > 0 || _headerSignatureValidatity != Validity.Valid || (_hasTitleKeyCrypto && !_isTicketSignatureValid) || _rebuildTicket)
    //             {
    //                 AnsiConsole.MarkupLine($"[[[red]WARN[/]]] - NSP Validation failed. Conversion or Extraction would fail.");
    //                 return 1;
    //             }
    //             return 0;
    //         }
    //         
    //         if (!canExtract)
    //         {
    //             AnsiConsole.MarkupLine($"[[[red]WARN[/]]] - NSP Validation failed.");
    //             return 1;
    //         }
    //
    //         if (_settings.Rename)
    //         {
    //             fs.Dispose();
    //             file.Dispose();
    //
    //             if (inputFilename == formattedName+".nsp")
    //             {
    //                 AnsiConsole.MarkupLine($"[[[green]DONE[/]]] -> Renaming skipped. Nothing to do. Filename matches already.");
    //                 return 0;
    //             }
    //             
    //             var targetDirectory = System.IO.Path.GetDirectoryName(nspFullPath);
    //             
    //             if(targetDirectory == null || !Directory.Exists(targetDirectory))
    //             {
    //                 AnsiConsole.MarkupLine($"[[[red]ERROR[/]]] -> Failed to open directory.");
    //                 return 1;
    //             }
    //             
    //             var targetName = System.IO.Path.Combine(targetDirectory, formattedName + ".nsp");
    //
    //             if (targetName.Length > 254)
    //             {
    //                 AnsiConsole.MarkupLine($"[[[red]ERROR[/]]] -> Path too long for Windows ({targetName.Length})");
    //                 return 1;
    //             }
    //             
    //             if (File.Exists(targetName))
    //             {
    //                 AnsiConsole.MarkupLine($"[[[red]ERROR[/]]] -> File with the same name already exists. ({formattedName.EscapeMarkup()}.nsp)");
    //                 return 1;
    //             }
    //             
    //             if (_settings.DryRun)
    //             {
    //                 AnsiConsole.MarkupLine($"[[[green]DRYRUN[/]]] -> Rename FROM: [olive]{inputFilename.EscapeMarkup()}[/]");
    //                 AnsiConsole.MarkupLine($"[[[green]DRYRUN[/]]] ->   Rename TO: [olive]{formattedName.EscapeMarkup()}.nsp[/]");
    //                 return 0;
    //             }
    //
    //             File.Move(nspFullPath, System.IO.Path.Combine(targetDirectory, formattedName + ".nsp"));
    //             AnsiConsole.MarkupLine($"[[[green]DONE[/]]] -> Renamed TO: [olive]{formattedName.EscapeMarkup()}[/]");
    //             return 0;
    //         }
    //         
    //         if (_hasTitleKeyCrypto && (!_isTicketSignatureValid || _rebuildTicket))
    //         {
    //             _ticket = NsfwUtilities.CreateTicket(_masterKeyRevision, _ticket.RightsId, _titleKeyEnc);
    //             AnsiConsole.MarkupLine("[[[green]DONE[/]]] -> Generated new normalised ticket.");
    //         }
    //         
    //         if(_settings.Extract)
    //         {
    //             phase = $"[olive]Extracting[/]";
    //             AnsiConsole.MarkupLine($"{phase}..");
    //             
    //             var outDir = System.IO.Path.Combine(_settings.CdnDirectory, formattedName);
    //             
    //             if(_settings.DryRun)
    //             {
    //                 AnsiConsole.MarkupLine($"[[[green]DRYRUN[/]]] -> Would create: [olive]{outDir.EscapeMarkup()}[/]");
    //             }
    //             else
    //             {
    //                 Directory.CreateDirectory(outDir);
    //             }
    //             
    //             foreach (var nca in title.Value.Ncas)
    //             {
    //                 if(_settings.DryRun)
    //                 {
    //                     AnsiConsole.MarkupLine($"[[[green]DRYRUN[/]]] -> Would export: [olive]{nca.Filename}[/]");
    //                     continue;
    //                 }
    //                 
    //                 var stream = nca.Nca.BaseStorage.AsStream();
    //                 var outFile = System.IO.Path.Combine(outDir, nca.Filename);
    //
    //                 using var outStream = new FileStream(outFile, FileMode.Create, FileAccess.ReadWrite);
    //                 stream.CopyStream(outStream, stream.Length);
    //             }
    //
    //             if (_hasTitleKeyCrypto)
    //             {
    //                  var decFile = $"{_ticket.RightsId.ToHexString().ToLower()}.dectitlekey.tik";
    //                  var encFile = $"{_ticket.RightsId.ToHexString().ToLower()}.enctitlekey.tik";
    //                  
    //                  if(_settings.DryRun)
    //                  {
    //                      AnsiConsole.MarkupLine($"[[[green]DRYRUN[/]]] -> Would export: [olive]{decFile.EscapeMarkup()}[/]");
    //                      AnsiConsole.MarkupLine($"[[[green]DRYRUN[/]]] -> Would export: [olive]{encFile.EscapeMarkup()}[/]");
    //                      AnsiConsole.MarkupLine($"[[[green]DRYRUN[/]]] -> Would export: [olive]{_ticket.RightsId.ToHexString().ToLower()}.tik[/]");
    //                  }
    //                  else
    //                  {
    //                      File.WriteAllBytes(System.IO.Path.Combine(outDir, decFile), _titleKeyDec);
    //                      File.WriteAllBytes(System.IO.Path.Combine(outDir, encFile), _titleKeyEnc);
    //                      File.WriteAllBytes(System.IO.Path.Combine(outDir, $"{_ticket.RightsId.ToHexString().ToLower()}.tik"), _ticket.File);
    //                  }
    //             }
    //             
    //             if(!_settings.DryRun)
    //             {
    //                 AnsiConsole.MarkupLine($"[[[green]DONE[/]]] -> Exported: [olive]{outDir.EscapeMarkup()}[/]");
    //             }
    //         }
    //
    //         if (_settings.Convert)
    //         {
    //             phase = $"[olive]Converting[/]";
    //             if (!_settings.Quiet)
    //             {
    //                 AnsiConsole.MarkupLine($"{phase}..");
    //             }
    //
    //             if (_settings.DryRun)
    //             {
    //                 AnsiConsole.MarkupLine($"[[[green]DRYRUN[/]]] -> Would create: [olive]{formattedName.EscapeMarkup()}.nsp[/]");
    //             }
    //             
    //             var builder = new PartitionFileSystemBuilder();
    //
    //             foreach (var nca in title.Value.Ncas)
    //             {
    //                 if (_settings.DryRun)
    //                 {
    //                     AnsiConsole.MarkupLine($"[[[green]DRYRUN[/]]] -> Would convert: [olive]{nca.Filename}[/]");
    //                 }
    //                 else
    //                 {
    //                     builder.AddFile(nca.Filename, nca.Nca.BaseStorage.AsFile(OpenMode.Read));
    //                 }
    //             }
    //
    //             if (_hasTitleKeyCrypto)
    //             {
    //                 if (_settings.DryRun)
    //                 {
    //                     AnsiConsole.MarkupLine($"[[[green]DRYRUN[/]]] -> Would convert: [olive]{_ticket.RightsId.ToHexString().ToLower()}.tik[/]");
    //                     AnsiConsole.MarkupLine($"[[[green]DRYRUN[/]]] -> Would convert: [olive]{_ticket.RightsId.ToHexString().ToLower()}.cert[/]");
    //                 }
    //                 else
    //                 {
    //                     builder.AddFile($"{_ticket.RightsId.ToHexString().ToLower()}.tik", new MemoryStorage(_ticket.GetBytes()).AsFile(OpenMode.Read)); 
    //                     builder.AddFile($"{_ticket.RightsId.ToHexString().ToLower()}.cert", new LocalFile(_settings.CertFile, OpenMode.Read));
    //                 }
    //             }
    //
    //             var targetName = System.IO.Path.Combine(_settings.NspDirectory, $"{formattedName}.nsp");
    //             
    //             if (targetName.Length > 254)
    //             {
    //                 AnsiConsole.MarkupLine($"[[[red]ERROR[/]]] -> Path too long for Windows ({targetName.Length})");
    //                 return 1;
    //             }
    //
    //             if (targetName == nspFullPath)
    //             {
    //                 AnsiConsole.MarkupLine($"[[[red]ERROR[/]]] -> Trying to save converted file to the same location as the input file.");
    //                 return 1;
    //             }
    //
    //             if (_settings.DryRun) return 0;
    //             
    //             using var outStream = new FileStream(targetName, FileMode.Create, FileAccess.ReadWrite);
    //             var builtPfs = builder.Build(PartitionFileSystemType.Standard);
    //             builtPfs.GetSize(out var pfsSize).ThrowIfFailure();
    //             builtPfs.CopyToStream(outStream, pfsSize);
    //             AnsiConsole.MarkupLine($"[[[green]DONE[/]]] -> Converted: [olive]{formattedName.EscapeMarkup()}.nsp[/]");
    //         }
    //     }
    //     else
    //     {
    //         AnsiConsole.MarkupLine($"[[[red]ERROR[/]]] -> No NCA files found.");
    //         return 1;
    //     }
    //     
         return 0;
    }

    // private static IFileSystem? OpenFileSystem(NspInfo nspInfo)
    // {
    //     using var file = new LocalStorage(nspInfo.FilePath, FileAccess.Read);
    //
    //     IFileSystem? fs = null;
    //     using var pfs = new UniqueRef<PartitionFileSystem>();
    //     using var hfs = new UniqueRef<Sha256PartitionFileSystem>();
    //
    //     pfs.Reset(new PartitionFileSystem());
    //     var res = pfs.Get.Initialize(file);
    //
    //     try
    //     {
    //         if (res.IsSuccess())
    //         {
    //             nspInfo.FileSystemType = FileSystemType.Partition;
    //             fs = pfs.Get;
    //         }
    //         else if (!ResultFs.PartitionSignatureVerificationFailed.Includes(res))
    //         {
    //             res.ThrowIfFailure();
    //         }
    //         else
    //         {
    //             // Reading the input as a PartitionFileSystem didn't work. Try reading it as an Sha256PartitionFileSystem
    //             hfs.Reset(new Sha256PartitionFileSystem());
    //             res = hfs.Get.Initialize(file);
    //             if (res.IsFailure())
    //             {
    //                 if (ResultFs.Sha256PartitionSignatureVerificationFailed.Includes(res))
    //                 {
    //                     ResultFs.PartitionSignatureVerificationFailed.Value.ThrowIfFailure();
    //                 }
    //
    //                 res.ThrowIfFailure();
    //             }
    //
    //             nspInfo.FileSystemType = FileSystemType.Sha256Partition;
    //             fs = hfs.Get;
    //         }
    //     }
    //     catch (Exception exception)
    //     {
    //         Log.Error("Unable to open NSP file-system. {0}", exception);
    //     }
    //
    //     return fs;
    // }
    
    private void ImportTicket(Ticket ticket, KeySet keySet, NspInfo nspInfo)
    {
        if (ticket.RightsId.IsZeros())
        {
            Log.Warning("[olive]Import tickets[/] - Empty Rights ID. Skipping");
            return;
        }

        byte[] key = ticket.GetTitleKey(keySet);
        if (key is null)
        {   
            Log.Warning("[olive]Import tickets[/] - TitleKey not found. Skipping");
            return;
        }

        var rightsId = new RightsId(ticket.RightsId);
        var accessKey = new AccessKey(key);
        
        keySet.ExternalKeySet.Add(rightsId, accessKey).ThrowIfFailure();

        if (nspInfo.Ticket != null)
        {
            Log.Warning($"[olive]Import tickets[/] - Multiple tickets found. Using first.");
            return;
        }
        
        nspInfo.Ticket = ticket;
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
    public string Title { get; init; } = string.Empty;
    public string Publisher { get; init; } = string.Empty;
    public NacpLanguage RegionLanguage { get; init; }
}

