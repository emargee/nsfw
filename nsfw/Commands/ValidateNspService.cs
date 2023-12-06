using System.ComponentModel;
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

        if (_settings.ShortLanguages)
        {
            nspInfo.OutputOptions.LanguageMode = LanguageMode.Short;
        }

        nspInfo.LogLevel = _settings.LogLevel;

        var titleDbPath = System.IO.Path.GetFullPath(_settings.TitleDbFile);

        if (File.Exists(titleDbPath))
        {
            nspInfo.OutputOptions.IsTitleDbAvailable = true;
            nspInfo.OutputOptions.TitleDbPath = titleDbPath;
        }

        Log.Information($"Validating NSP : [olive]{nspInfo.FileName.EscapeMarkup()}[/]");

        //AnsiConsole.WriteLine("----------------------------------------");

        if (_settings.Convert)
        {
            Log.Information(_settings.DryRun
                ? "Output Mode <- [green]CONVERT[/] ([olive]Dry Run[/])"
                : "Output Mode <- [green]CONVERT[/]");
        }

        if (_settings.Extract)
        {
            Log.Information(_settings.DryRun
                ? "Output Mode <- [green]EXTRACT[/] ([olive]Dry Run[/])"
                : "Output Mode <- [green]EXTRACT[/]");
        }

        if (_settings.Rename)
        {
            Log.Information(_settings.DryRun
                ? "Output Mode <- [green]RENAME[/] ([olive]Dry Run[/])"
                : "Output Mode <- [green]RENAME[/]");
        }

        var localFile = new LocalFile(nspInfo.FilePath, OpenMode.Read);
        var headerBuffer = new Span<byte>(new byte[4]);
        localFile.Read(out var bytesRead, 0, headerBuffer);

        if (headerBuffer.ToHexString() != nspInfo.HeaderMagic)
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
                Log.Verbose($"{phase} <- Ticket ({rawFile.Name}) imported.");
            }

            nspInfo.RawFileEntries.Add(rawFile.Name,
                new RawContentFile
                {
                    Name = rawFile.Name, Size = rawFile.Size, FullPath = rawFile.FullPath, Type = rawFile.Type,
                    BlockCount = (int)BitUtil.DivideUp(rawFile.Size, nspInfo.DefaultBlockSize)
                });
        }

        if (!nspInfo.HasTicket)
        {
            Log.Verbose($"{phase} <- No valid tickets found.");
        }

        phase = "[olive]NSP File-System[/]";

        var switchFs = SwitchFs.OpenNcaDirectory(_keySet, fileSystem);

        if (switchFs == null)
        {
            Log.Error($"{phase} - Failed to open NSP as SwitchFS.");
            return 1;
        }

        Log.Verbose($"{phase} <- Loaded correctly.");

        phase = "[olive]Validate NSP[/]";

        if (switchFs.Applications.Count != 1)
        {
            Log.Error($"{phase} - Expected 1 Application, found {switchFs.Applications.Count}");
            return 1;
        }

        if (switchFs.Applications.Count != switchFs.Titles.Count)
        {
            nspInfo.Warnings.Add(
                $"{phase} - Title count ({switchFs.Titles.Count}) does not match Application count ({switchFs.Applications.Count})");
        }

        var title = switchFs.Titles.First().Value;

        phase = $"[olive]Validate Metadata (CNMT)[/]";

        var cnmt = title.Metadata;

        if (cnmt == null)
        {
            Log.Error($"{phase} - Unable to load CNMT.");
            return 1;
        }

        nspInfo.TitleId = cnmt.TitleId.ToString("X16");
        nspInfo.BaseTitleId = cnmt.ApplicationTitleId.ToString("X16");

        if (nspInfo.TitleId != nspInfo.BaseTitleId && cnmt.Type == ContentMetaType.Application)
        {
            nspInfo.Warnings.Add(
                $"{phase} - TitleID Mis-match. Expected {nspInfo.BaseTitleId}, found {nspInfo.TitleId}");
        }

        nspInfo.TitleVersion = $"v{cnmt.TitleVersion.Version}";
        nspInfo.TitleType = cnmt.Type;
        nspInfo.MinimumApplicationVersion = cnmt.MinimumApplicationVersion != null
            ? cnmt.MinimumApplicationVersion.ToString()
            : "0.0.0";
        nspInfo.MinimumSystemVersion =
            cnmt.MinimumSystemVersion != null ? cnmt.MinimumSystemVersion.ToString() : "0.0.0";

        if (nspInfo.TitleType != ContentMetaType.Patch && nspInfo.TitleType != ContentMetaType.Application &&
            nspInfo.TitleType != ContentMetaType.Delta && nspInfo.TitleType != ContentMetaType.AddOnContent)
        {
            Log.Error($"{phase} - Unsupported type {nspInfo.TitleType}");
            return 1;
        }

        var metaContent = new ContentFile
        {
            FileName = title.MetaNca.Filename,
            NcaId = title.MetaNca.NcaId,
            Hash = cnmt.Hash,
            Type = ContentType.Meta,
            IsMissing = !nspInfo.RawFileEntries.ContainsKey(title.MetaNca.Filename)
        };

        nspInfo.ContentFiles.Add(metaContent.FileName, metaContent);

        foreach (var contentEntry in cnmt.ContentEntries)
        {
            var contentFile = new ContentFile
            {
                FileName = $"{contentEntry.NcaId.ToHexString().ToLower()}.nca",
                NcaId = contentEntry.NcaId.ToHexString(),
                Hash = contentEntry.Hash,
                Type = contentEntry.Type
            };

            if (contentFile.NcaId != contentFile.Hash.Take(16).ToArray().ToHexString())
            {
                Log.Error($"{phase} - Hash part should match NCA ID ({contentFile.NcaId}).");
                return 1;
            }

            if (!nspInfo.RawFileEntries.ContainsKey(contentFile.FileName))
            {
                contentFile.IsMissing = true;

                if (contentFile.Type != ContentType.DeltaFragment)
                {
                    nspInfo.Errors.Add($"{phase} - NSP file-system is missing content file : " + contentFile.FileName);
                    nspInfo.CanProceed = false;
                }
                else
                {
                    nspInfo.Warnings.Add($"{phase} - NSP file-system is missing delta fragments listed in CNMT.");
                }
            }
            else
            {
                if (nspInfo.RawFileEntries[contentFile.FileName].Size != contentEntry.Size)
                {
                    contentFile.SizeMismatch = true;
                    nspInfo.Errors.Add(
                        $"{phase} - NSP file-system contains files with sizes that do not match the CNMT.");
                    nspInfo.CanProceed = false;
                }
            }

            nspInfo.ContentFiles.Add(contentFile.FileName, contentFile);
        }

        Log.Verbose($"[olive]NSP Type[/] <- {nspInfo.DisplayType}" + (nspInfo.TitleType == ContentMetaType.Patch
            ? $" ({nspInfo.TitleVersion})"
            : string.Empty));

        var mainNca = title.MainNca;

        phase = "[olive]Validate Main NCA[/]";

        if (mainNca == null)
        {
            Log.Error($"{phase} - Failed to open Main NCA.");
            return 1;
        }

        if (!mainNca.Nca.Header.RightsId.IsZeros())
        {
            nspInfo.RightsId = mainNca.Nca.Header.RightsId.ToHexString();
        }

        nspInfo.HasTitleKeyCrypto = mainNca.Nca.Header.HasRightsId;

        if (mainNca.Nca.Header.DistributionType != DistributionType.Download)
        {
            Log.Error($"{phase} - Unsupported distribution type : {mainNca.Nca.Header.DistributionType}");
            return -1;
        }

        if (nspInfo is { HasTicket: true, HasTitleKeyCrypto: false, TitleType: ContentMetaType.AddOnContent })
        {
            nspInfo.Errors.Add(
                $"{phase} - NSP has ticket but no title key crypto. This is possibly a Homebrew DLC unlocker. Conversion would lose the ticket + cert.");
            nspInfo.PossibleDlcUnlocker = true;
            nspInfo.CanProceed = false;
        }

        if (nspInfo is { HasTicket: false, HasTitleKeyCrypto: true })
        {
            nspInfo.Errors.Add($"{phase} - NSP is TitleKey encrypted but no valid ticket found.");
            nspInfo.CanProceed = false;
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

            if (nspInfo.Ticket!.PropertyMask != 0)
            {
                nspInfo.Warnings.Add($"{phase} - Ticket has property mask set ({propertyMask}).");
                nspInfo.GenerateNewTicket = true;
            }

            if (nspInfo.Ticket!.AccountId != 0)
            {
                nspInfo.Warnings.Add($"{phase} - Ticket has account ID set ({nspInfo.Ticket!.AccountId})");
                nspInfo.GenerateNewTicket = true;
            }

            if (nspInfo.Ticket!.DeviceId != 0)
            {
                nspInfo.Warnings.Add($"{phase} - Ticket has device ID set ({nspInfo.Ticket!.DeviceId})");
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
                Log.Error(
                    $"{phase} - Invalid rights ID key generation! Got {ticketMasterKey}, expected {nspInfo.MasterKeyRevision}.");
                return 1;
            }
        }

        phase = $"[olive]Validate NCAs[/]";

        if (title.Ncas.Count == 0)
        {
            Log.Error($"{phase} - No NCAs found.");
            return 1;
        }

        foreach (var fsNca in title.Ncas)
        {
            if (fsNca.Nca.Header.ContentType == NcaContentType.Meta)
            {
                continue;
            }

            var ncaInfo = new NcaInfo(fsNca.Filename)
            {
                IsHeaderValid = fsNca.Nca.VerifyHeaderSignature() == Validity.Valid
            };

            if (!ncaInfo.IsHeaderValid)
            {
                nspInfo.Errors.Add($"{phase} - {ncaInfo.FileName} - Header signature is invalid.");
                nspInfo.CanProceed = false;
            }

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
                        nspInfo.Errors.Add(
                            $"{phase} - {ncaInfo.FileName} (Section {sectionInfo.SectionId}) <- Error opening file-system");
                    }
                }

                ncaInfo.Sections.Add(i, sectionInfo);
            }

            if (_settings.SkipHash)
            {
                nspInfo.Warnings.Add($"{phase} - Skipped hash check on : {ncaInfo.FileName}.");
                continue;
            }

            if (_settings.ForceHash || (!ncaInfo.IsErrored && nspInfo.HeaderSignatureValidity == Validity.Valid))
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
                    .AutoClear(true) // Do not remove the task list when done
                    .HideCompleted(true) // Hide tasks as they are completed
                    .Columns(new ProgressColumn[]
                    {
                        new SpinnerColumn(),
                        new TaskDescriptionColumn(), // Task description
                        //new ProgressBarColumn(),      // Progress bar
                        new PercentageColumn(), // Percentage
                        new RemainingTimeColumn(), // Remaining time
                    })
                    .Start(ctx =>
                    {
                        var hashTask = ctx.AddTask($"Hashing ..", new ProgressTaskSettings { MaxValue = blockCount });

                        while (!ctx.IsFinished)
                        {
                            var sha256 = SHA256.Create();

                            var ncaFile = new UniqueRef<IFile>();
                            fileSystem.OpenFile(ref ncaFile, (rawFile.FullPath).ToU8Span(), OpenMode.Read);

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
                            ncaInfo.HashMatch = string.Equals(ncaHash, expectedHash, StringComparison.OrdinalIgnoreCase)
                                ? HashMatchType.Match
                                : HashMatchType.Mismatch;

                            if (ncaInfo.HashMatch == HashMatchType.Mismatch)
                            {
                                nspInfo.Errors.Add($"{phase} - Hash mismatch for NCA {ncaInfo.FileName}");
                                nspInfo.CanProceed = false;
                            }
                        }
                    });
            }

            nspInfo.NcaFiles.Add(fsNca.Filename, ncaInfo);
        }

        var control = title.Control.Value;

        var nspLanguageId = -1;

        if (control.Title.Items != null)
        {
            foreach (var titleItem in control.Title.Items)
            {
                nspLanguageId++;

                if (titleItem.NameString.IsEmpty())
                {
                    continue;
                }

                var language = (NacpLanguage)nspLanguageId;

                nspInfo.Titles.Add(language, new TitleInfo
                {
                    Title = titleItem.NameString.ToString() ?? "UNKNOWN",
                    RegionLanguage = (NacpLanguage)nspLanguageId,
                    Publisher = titleItem.PublisherString.ToString() ?? "UNKNOWN",
                });

                nspInfo.DisplayTitleSource = Source.Control;
            }
        }

        if (nspInfo.DisplayTitleSource == Source.Control)
        {
            nspInfo.DisplayTitle = nspInfo.ControlTitle;
        }

        if (nspInfo.DisplayTitleSource == Source.Unknown)
        {
            nspInfo.DisplayTitle = nspInfo.FileName.Replace(".nsp", string.Empty);
            nspInfo.DisplayTitleSource = Source.FileName;
        }

        if (nspInfo.OutputOptions.IsTitleDbAvailable)
        {
            if (_settings.VerifyTitle || nspInfo.DisplayTitleSource == Source.FileName)
            {
                var titleDbTitle = string.Empty;

                if (nspInfo.TitleType == ContentMetaType.AddOnContent)
                {
                    titleDbTitle = NsfwUtilities.LookUpTitle(nspInfo.OutputOptions.TitleDbPath, nspInfo.TitleId)
                        ?.CleanTitle();
                    nspInfo.DisplayParentTitle = NsfwUtilities
                        .LookUpTitle(nspInfo.OutputOptions.TitleDbPath, nspInfo.BaseTitleId)?.CleanTitle();
                }
                else
                {
                    titleDbTitle = NsfwUtilities.LookUpTitle(nspInfo.OutputOptions.TitleDbPath,
                        nspInfo.UseBaseTitleId ? nspInfo.BaseTitleId : nspInfo.TitleId);
                }

                if (!string.IsNullOrEmpty(titleDbTitle))
                {
                    nspInfo.DisplayTitle = titleDbTitle;
                    nspInfo.DisplayTitleSource = Source.TitleDb;
                }
            }
        }

        if (nspInfo.DisplayTitleSource == Source.FileName && nspInfo.TitleType == ContentMetaType.AddOnContent &&
            nspInfo.FileName.Contains('['))
        {
            var filenameParts = nspInfo.FileName.Split('[', StringSplitOptions.TrimEntries);

            if (filenameParts.Length > 1)
            {
                nspInfo.DisplayTitle = filenameParts[0];

                if (!char.IsDigit(filenameParts[1][0]) && filenameParts.Length > 2)
                {
                    nspInfo.DisplayTitle += " - " + filenameParts[1]
                        .Replace("]", String.Empty)
                        .Replace("dlc", "DLC").Trim();
                }
            }
        }

        if (!string.IsNullOrEmpty(control.DisplayVersionString.ToString()))
        {
            nspInfo.DisplayVersion = control.DisplayVersionString.ToString()!.Trim();
        }

        if (_settings.SkipHash && !_settings.Rename)
        {
            Log.Information("[olive]Validation Complete[/] <- [olive]Unknown - Hashing skipped.[/]");
        }
        else
        {
            if (nspInfo is { HasErrors: false, HeaderSignatureValidity: Validity.Valid } &&
                nspInfo.NcaFiles.Values.Any(x => !x.IsErrored) &&
                nspInfo.NcaFiles.Values.Any(x => x.HashMatch == HashMatchType.Match))
            {
                Log.Information("[olive]Validation Complete[/] <- [green]No problems found[/]");
            }
            else
            {
                Log.Information("[olive]Validation Complete[/] <- [red]Problems found[/]");
            }
        }

        foreach (var warning in nspInfo.Warnings)
        {
            Log.Warning(warning);
        }

        foreach (var error in nspInfo.Errors)
        {
            Log.Error(error);
        }

        AnsiConsole.WriteLine("----------------------------------------");

        // -- DISPLAY SECTION --

        // RAW FILE TREE

        if (_settings.LogLevel == LogLevel.Full)
        {
            var rawFileTree = new Tree("PFS0:")
            {
                Expanded = true,
                Guide = TreeGuide.Line
            };
            foreach (var rawFile in nspInfo.RawFileEntries.Values)
            {
                rawFileTree.AddNode($"{rawFile.Name} - {rawFile.DisplaySize}");
            }

            AnsiConsole.Write(new Padder(rawFileTree).PadLeft(1).PadTop(0).PadBottom(0));
        }

        // CNMT TREE

        if (_settings.LogLevel == LogLevel.Full)
        {
            var metaTree = new Tree("Metadata Content:")
            {
                Expanded = true,
                Guide = TreeGuide.Line
            };
            foreach (var contentFile in nspInfo.ContentFiles.Values)
            {
                var status = contentFile.IsMissing || contentFile.SizeMismatch ? "[red][[X]][/]" : "[green][[V]][/]";
                var error = contentFile.IsMissing ? "<- Missing" :
                    contentFile.SizeMismatch ? "<- Size Mismatch" : string.Empty;
                metaTree.AddNode($"{status} - {contentFile.FileName} [[{contentFile.Type}]] {error}");
            }

            AnsiConsole.Write(new Padder(metaTree).PadLeft(1).PadTop(1).PadBottom(0));
        }

        // NCA TREE

        if (_settings.LogLevel == LogLevel.Full)
        {

            var ncaTree = new Tree("NCAs:")
            {
                Expanded = true,
                Guide = TreeGuide.Line
            };
            foreach (var ncaFile in nspInfo.NcaFiles.Values)
            {
                var status = !ncaFile.IsHeaderValid ? "[[[red]H[/]]]" : "[[[green]H[/]]]";
                var hashStatus = ncaFile.HashMatch switch
                {
                    HashMatchType.Match => "[[[green]V[/]]]",
                    HashMatchType.Mismatch => "[[[red]X[/]]]",
                    _ => "[[[olive]-[/]]]"
                };
                var ncaNode = new TreeNode(new Markup($"{status}{hashStatus} {ncaFile.FileName} ({ncaFile.Type})"));
                ncaNode.Expanded = true;

                foreach (var section in ncaFile.Sections.Values)
                {
                    var sectionStatus = section.IsErrored ? "[[[red]X[/]]]" :
                        section.IsPatchSection ? "[[[olive]P[/]]]" : "[[[green]V[/]]]";

                    if (section.IsErrored)
                    {
                        ncaNode.AddNode(
                            $"{sectionStatus} Section {section.SectionId} <- [red]{section.ErrorMessage}[/]");
                    }
                    else
                    {
                        ncaNode.AddNode(
                            $"{sectionStatus} Section {section.SectionId} [grey]({section.EncryptionType})[/]");
                    }

                }

                ncaTree.AddNode(ncaNode);
            }
            AnsiConsole.Write(new Padder(ncaTree).PadLeft(1).PadTop(1).PadBottom(0));
        }
        
        // TICKET INFO
        
        if (nspInfo.Ticket != null && (_settings.LogLevel == LogLevel.Full || _settings.TicketInfo))
        {
            var tikTable = new Table
            {
                ShowHeaders = false
            };
            tikTable.AddColumn("Property");
            tikTable.AddColumn("Value");
            
            NsfwUtilities.RenderTicket(tikTable, nspInfo.Ticket);
            
            AnsiConsole.Write(new Padder(tikTable).PadLeft(1).PadRight(0).PadBottom(0).PadTop(1));
        }
        
        // TITLEDB - REGIONAL TITLES
        
        if (nspInfo.OutputOptions.IsTitleDbAvailable && _settings.RegionalTitles)
        {
            var titleResults = NsfwUtilities.GetTitleDbInfo(_settings.TitleDbFile, nspInfo.UseBaseTitleId && nspInfo.TitleType != ContentMetaType.AddOnContent ? nspInfo.BaseTitleId : nspInfo.TitleId).Result;
        
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
                
                AnsiConsole.Write(new Padder(regionTable).PadLeft(1).PadTop(1).PadBottom(0));
            }
        }
        
        // TITLEDB - DLC RELATED TITLES 
        
        if (nspInfo.OutputOptions.IsTitleDbAvailable && _settings.RelatedTitles && nspInfo.TitleType == ContentMetaType.AddOnContent)
        {
            var relatedResults = NsfwUtilities.LookUpRelatedTitles(_settings.TitleDbFile, nspInfo.TitleId).Result;
        
            if (relatedResults.Length > 0)
            {
                var relatedTable = new Table() { ShowHeaders = false };
                relatedTable.AddColumn("Title");
                relatedTable.AddRow(new Text("Related DLC Titles"));
                relatedTable.AddEmptyRow();
                foreach (var relatedResult in relatedResults.Distinct())
                {
                    relatedTable.AddRow(new Markup($"{relatedResult.ReplaceLineEndings(string.Empty).EscapeMarkup()}"));
                }
        
                AnsiConsole.Write(new Padder(relatedTable).PadLeft(1).PadTop(1).PadBottom(0));
            }
        }
        
        // TITLEDB - UPDATES
        
        if (nspInfo.OutputOptions.IsTitleDbAvailable && _settings.Updates && nspInfo.TitleType is ContentMetaType.Application or ContentMetaType.Patch)
        {
            var versions = NsfwUtilities.LookUpUpdates(_settings.TitleDbFile, nspInfo.UseBaseTitleId ? nspInfo.BaseTitleId : nspInfo.TitleId).Result;
            
            if (versions.Length > 0)
            {
                var updateTable = new Table() { ShowHeaders = false };
                updateTable.AddColumn("Version");
                updateTable.AddColumn("Date");
                updateTable.AddRow(new Text("Updates"));
                updateTable.AddEmptyRow();
                foreach (var version in versions)
                {
                    if ("v"+version.Version == nspInfo.TitleVersion)
                    {
                        updateTable.AddRow($"[green]v{version.Version}[/]", $"[green]{version.ReleaseDate}[/]");
                    }
                    else
                    {
                        updateTable.AddRow($"v{version.Version}", $"{version.ReleaseDate}");
                    }
                }
                AnsiConsole.Write(new Padder(updateTable).PadLeft(1).PadTop(1).PadBottom(1));
            }
        }
        
        // PROPERTIES
        
        var outputName = nspInfo.OutputName;

        if (_settings.LogLevel != LogLevel.Quiet)
        {
            var propertiesTable = new Table() { ShowHeaders = false };
            propertiesTable.AddColumns("Name", "Value");

            propertiesTable.AddRow("Title",
                $"[olive]{nspInfo.DisplayTitle.EscapeMarkup()}[/]" + " (From " + nspInfo.DisplayTitleSource + ")");

            if (nspInfo.TitleType == ContentMetaType.AddOnContent && !string.IsNullOrEmpty(nspInfo.DisplayParentTitle))
            {
                propertiesTable.AddRow("Parent Title",
                    $"[olive]{nspInfo.DisplayParentTitle.EscapeMarkup()}[/]" + " (From " + nspInfo.DisplayTitleSource + ")");
            }

            if (nspInfo.HasLanguages)
            {
                if (nspInfo.OutputOptions.LanguageMode == LanguageMode.Full)
                {
                    propertiesTable.AddRow("Languages", string.Join(",", nspInfo.LanguagesFull));
                }

                if (nspInfo.OutputOptions.LanguageMode == LanguageMode.Short)
                {
                    propertiesTable.AddRow("Languages", string.Join(",", nspInfo.LanguagesShort));
                }
            }

            if (nspInfo.TitleType == ContentMetaType.AddOnContent && nspInfo.OutputOptions.IsTitleDbAvailable)
            {
                var parentLanguages = NsfwUtilities.LookupLanguages(_settings.TitleDbFile, nspInfo.BaseTitleId);
                if (parentLanguages.Length > 0)
                {
                    parentLanguages = string.Join(',',
                        parentLanguages.Split(',').Distinct()
                            .Select(x => string.Concat(x[0].ToString().ToUpper(), x.AsSpan(1))));
                    propertiesTable.AddRow("Parent Languages", parentLanguages + " ([olive]TitleDB[/])");
                }
            }

            propertiesTable.AddRow("Title ID", nspInfo.TitleId);
            if (nspInfo.UseBaseTitleId)
            {
                propertiesTable.AddRow("Base Title ID", nspInfo.BaseTitleId);
            }

            propertiesTable.AddRow("Title Type", nspInfo.DisplayType);
            propertiesTable.AddRow("Title Version", nspInfo.TitleVersion == "v0" ? "BASE (v0)" : nspInfo.TitleVersion);
            propertiesTable.AddRow("NSP Version", nspInfo.DisplayVersion);
            propertiesTable.AddRow("Rights ID", nspInfo.RightsId);
            propertiesTable.AddRow("Header Validity", nspInfo.HeaderSignatureValidity == Validity.Valid ? "[green]Valid[/]" : "[red]Invalid[/]");
            propertiesTable.AddRow("NCA Validity", nspInfo.NcaValidity == Validity.Valid ? "[green]Valid[/]" : "[red]Invalid[/]");
            propertiesTable.AddRow("Meta Validity", nspInfo.ContentValidity == Validity.Valid ? "[green]Valid[/]" : "[red]Invalid[/]");
            if (nspInfo.TitleKeyDecrypted.Length > 0)
            {
                propertiesTable.AddRow("TitleKey (Enc)", nspInfo.TitleKeyEncrypted.ToHexString());
                propertiesTable.AddRow("TitleKey (Dec)", nspInfo.TitleKeyDecrypted.ToHexString());

                if (nspInfo.IsNormalisedSignature)
                {
                    propertiesTable.AddRow("Ticket Signature", "[green]Normalised[/]");
                }
                else
                {
                    propertiesTable.AddRow("Ticket Signature",
                        nspInfo.IsTicketSignatureValid
                            ? "[green]Valid[/]"
                            : "[red]Invalid[/] (Signature Mismatch) - Will generate new ticket.");
                }

                if (nspInfo.GenerateNewTicket)
                {
                    propertiesTable.AddRow("Ticket Validation", "[red]Non-Standard[/] - Will generate new ticket.");
                }
                else
                {
                    propertiesTable.AddRow("Ticket Validation", "[green]Passed[/]");
                }

                propertiesTable.AddRow("MasterKey Revision", nspInfo.MasterKeyRevision.ToString());
                propertiesTable.AddRow("Minimum Application Version", nspInfo.MinimumApplicationVersion == "0.0.0" ? "None" : nspInfo.MinimumApplicationVersion);
                propertiesTable.AddRow("Minimum System Version", nspInfo.MinimumSystemVersion == "0.0.0" ? "None" : nspInfo.MinimumSystemVersion);
            }

            propertiesTable.AddRow("Output Name", $"[olive]{outputName.EscapeMarkup()}[/]");
            propertiesTable.AddRow("[olive]Validation[/]", nspInfo.CanProceed ? "[green]Passed[/]" : "[red]Failed[/]");

            AnsiConsole.Write(new Padder(propertiesTable).PadLeft(1).PadTop(1).PadBottom(1));
            AnsiConsole.WriteLine("----------------------------------------");
        }
        
        if(!nspInfo.CanProceed)
        {
            Log.Fatal("NSP Validation failed.");
            return 1;
        }
        
        if (_settings.Rename)
        {
            fileSystem.Dispose();
            localFile.Dispose();
            
            if (nspInfo.FileName ==  outputName+".nsp")
            {
                Log.Information($"Renaming skipped. Nothing to do. Filename matches already.");
                return 0;
            }
            
            var targetDirectory = System.IO.Path.GetDirectoryName(nspFullPath);
            
            if(targetDirectory == null || !Directory.Exists(targetDirectory))
            {
                Log.Error($"Failed to open directory ({targetDirectory.EscapeMarkup()}).");
                return 1;
            }
            
            var targetName = System.IO.Path.Combine(targetDirectory, outputName + ".nsp");
            
            if (targetName.Length > 254)
            {
                Log.Error($"Path too long for Windows ({targetName.Length})");
                return 1;
            }
            
            if (File.Exists(targetName))
            {
                Log.Error($"File with the same name already exists. ({outputName.EscapeMarkup()}.nsp)");
                return 1;
            }
            
            if (_settings.DryRun)
            {
                Log.Information($"[[[green]DRYRUN[/]]] -> Rename FROM: [olive]{nspInfo.FileName.EscapeMarkup()}[/]");
                Log.Information($"[[[green]DRYRUN[/]]] ->   Rename TO: [olive]{outputName.EscapeMarkup()}.nsp[/]");
                return 0;
            }
            
            File.Move(nspFullPath, System.IO.Path.Combine(targetDirectory, outputName + ".nsp"));
            Log.Information($"Renamed TO: [olive]{outputName.EscapeMarkup()}[/]");
            return 0;
        }
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

