using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text.RegularExpressions;
using LibHac.Common;
using LibHac.Common.Keys;
using LibHac.Fs;
using LibHac.Fs.Fsa;
using LibHac.FsSystem;
using LibHac.Ncm;
using LibHac.Ns;
using LibHac.Spl;
using LibHac.Tools.Es;
using LibHac.Tools.Fs;
using LibHac.Tools.FsSystem;
using LibHac.Tools.FsSystem.NcaUtils;
using LibHac.Tools.Ncm;
using LibHac.Util;
using Nsfw.Nsp;
using Serilog;
using Spectre.Console;
using SQLite;
using ContentType = LibHac.Ncm.ContentType;
using NcaFsHeader = LibHac.Tools.FsSystem.NcaUtils.NcaFsHeader;
using Path = System.IO.Path;

namespace Nsfw.Commands;

public class ValidateNspService(ValidateNspSettings settings)
{
    private readonly KeySet _keySet = ExternalKeyReader.ReadKeyFile(settings.KeysFile);
    private bool _batchMode;
    // ReSharper disable once NullableWarningSuppressionIsUsed
    private SQLiteAsyncConnection _dbConnection = null!;

    public (int returnValue, NspInfo? nspInfo) Process(string nspFullPath, bool batchMode, bool cdnMode = false)
    {
        _batchMode = batchMode;
        
        var nspInfo = new NspInfo(nspFullPath);

        if (settings.NoLanguages)
        {
            nspInfo.OutputOptions.LanguageMode = LanguageMode.None;
        }

        if (settings.ShortLanguages)
        {
            nspInfo.OutputOptions.LanguageMode = LanguageMode.Short;
        }

        if (settings.KeepName)
        {
            nspInfo.OutputOptions.KeepName = true;
        }

        var titleDbPath = Path.GetFullPath(settings.TitleDbFile);

        if (File.Exists(titleDbPath))
        {
            nspInfo.OutputOptions.IsTitleDbAvailable = true;
            _dbConnection = new SQLiteAsyncConnection(titleDbPath);
        }

        if (!cdnMode)
        {
            Log.Information($"Validating NSP : [olive]{nspInfo.FileName.EscapeMarkup()}[/]");
        }

        if (settings.Convert && !cdnMode)
        {
            var extra = string.Empty;
            
            if(settings.DryRun)
            {
                extra = " ([olive]Dry Run[/])";
            }
            
            if(settings.ForceConvert)
            {
                extra = " ([olive]Force[/])";
            }
            
            if(settings.DeleteSource)
            {
                extra += " ([olive]Delete Source[/])";
            }
            
            Log.Information($"Output Mode <- [green]CONVERT[/]{extra}");
        }

        if (settings.Extract)
        {
            Log.Information(settings.DryRun
                ? settings.ExtractAll ? "Output Mode <- [green]EXTRACT (ALL)[/] ([olive]Dry Run[/])" : "Output Mode <- [green]EXTRACT[/] ([olive]Dry Run[/])"
                : settings.ExtractAll ? "Output Mode <- [green]EXTRACT (ALL)[/]" : "Output Mode <- [green]EXTRACT[/]");
        }

        if (settings.Rename)
        {
            Log.Information(settings.DryRun
                ? "Output Mode <- [green]RENAME[/] ([olive]Dry Run[/])"
                : "Output Mode <- [green]RENAME[/]");
        }

        var localFile = new LocalFile(nspInfo.FilePath, OpenMode.Read);
        var headerBuffer = new Span<byte>(new byte[4]);
        localFile.Read(out _, 0, headerBuffer);

        if (headerBuffer.ToHexString() != nspInfo.HeaderMagic)
        {
            Log.Error("Cannot mount file-system. Invalid NSP file.");
            return (1, null);
        }
        
        var padding = new byte[57];
        var paddingBuffer = new Span<byte>(padding);
        localFile.GetSize(out var localFileSize);
        localFile.Read(out _, localFileSize-57, paddingBuffer);

        if (paddingBuffer[0] != 125 && paddingBuffer[2] != 1 && paddingBuffer[4] != 1)
        {
            nspInfo.BadPadding = true;
            Log.Warning("NSP has incorrect padding at the end of the file.");
        }
        
        if(settings.CheckPadding)
        {
            if(nspInfo.BadPadding)
            {
                Log.Error("Padding check failed.");
                return (1, null);
            }
            Log.Information("Padding check passed.");
            return (0, null);
        }
        
        var fileStorage = new FileStorage(localFile);
        var fileSystem = new PartitionFileSystem();
        fileSystem.Initialize(fileStorage);

        var nspStructure = new NspStructure();

        var phase = "[olive]Read NCAs[/]";
        
        foreach (var rawFile in fileSystem.EnumerateEntries("*.*", SearchOptions.RecurseSubdirectories))
        {
            if (rawFile.Name.EndsWith(".tik"))
            {
                var tikFile = new UniqueRef<IFile>();
                fileSystem.OpenFile(ref tikFile, rawFile.FullPath.ToU8Span(), OpenMode.Read);
                ImportTicket(new Ticket(tikFile.Get.AsStream()), _keySet, nspInfo);
                Log.Verbose($"Import Tickets <- Ticket ({rawFile.Name}) imported.");
            }
            
            if(rawFile.Name.EndsWith(".cert"))
            {
                if (rawFile.Size != NsfwUtilities.CommonCertSize)
                {
                    Log.Warning($"Certificate ({rawFile.Name}) size is incorrect. Expected 0x700 bytes.");
                    nspInfo.CopyNewCert = true;
                }
                else
                {
                    var certFile = new UniqueRef<IFile>();
                    fileSystem.OpenFile(ref certFile, rawFile.FullPath.ToU8Span(), OpenMode.Read);
                    var validCommonCert = NsfwUtilities.ValidateCommonCert(certFile.Get.AsStream());
                    if (!validCommonCert)
                    {
                        Log.Warning($"Certificate ({rawFile.Name}) does not match common certificate SHA256.");
                        nspInfo.CopyNewCert = true;
                    }
                    certFile.Destroy();
                }
            }
            
            if (rawFile.Name.EndsWith(".nca"))
            {
                var ncaFile = new UniqueRef<IFile>();
                fileSystem.OpenFile(ref ncaFile, rawFile.FullPath.ToU8Span(), OpenMode.Read);

                SwitchFsNca nca;
                try
                {
                    nca = new SwitchFsNca(new Nca(_keySet, ncaFile.Release().AsStorage()))
                    {
                        Filename = rawFile.Name,
                        NcaId = rawFile.Name[..32]
                    };
                }
                catch (Exception e)
                {
                    Log.Fatal($"{phase} <- Error opening NCA ({rawFile.Name}) - {e.Message}");
                    return (1,null);
                }
                
                nspStructure.NcaCollection.Add(nca.NcaId, nca);
                
                if(rawFile.Name.EndsWith(".cnmt.nca"))
                {
                    nspStructure.MetaNca = nca;
                    
                    var fs = nca.OpenFileSystem(NcaSectionType.Data, IntegrityCheckLevel.ErrorOnInvalid);
                    var cnmtPath = fs.EnumerateEntries("/", "*.cnmt").Single().FullPath;

                    using var file = new UniqueRef<IFile>();
                    fs.OpenFile(ref file.Ref, cnmtPath.ToU8Span(), OpenMode.Read).ThrowIfFailure();

                    nspStructure.Metadata = new Cnmt(file.Release().AsStream());
                    if (nspStructure.Metadata.ContentMetaAttributes.HasFlag(ContentMetaAttribute.Compacted))
                    {
                        nspInfo.HasSparseNcas = true;
                        Log.Information($"[olive]{phase}[/] <- Sparse NCAs detected.");
                    }
                }
            }

            var isLooseFile = !(rawFile.Name.EndsWith(".nca") || rawFile.Name.EndsWith(".tik") || rawFile.Name.EndsWith(".cert"));

            if (isLooseFile)
            {
                nspInfo.HasLooseFiles = true;
            }

            nspInfo.RawFileEntries.Add(rawFile.Name,
                new RawContentFileInfo
                {
                    Name = rawFile.Name,
                    Size = rawFile.Size,
                    FullPath = rawFile.FullPath,
                    Type = rawFile.Type,
                    BlockCount = (int)BitUtil.DivideUp(rawFile.Size, nspInfo.DefaultBlockSize),
                    IsLooseFile = isLooseFile,
                    Priority = NsfwUtilities.AssignPriority(rawFile.Name)
                });
        }

        if (!nspInfo.HasTicket)
        {
            Log.Verbose($"{phase} <- No valid tickets found.");
        }
        
        phase = "[olive]NSP File-System[/]";
        
        nspStructure.Build();

        Log.Verbose($"{phase} <- Loaded correctly.");

        phase = "[olive]Validate Metadata (CNMT)[/]";

        var cnmt = nspStructure.Metadata;

        if (cnmt == null)
        {
            Log.Error("Failed to open CNMT.");
            return (1, null);
        }

        nspInfo.TitleId = cnmt.TitleId.ToString("X16");
        nspInfo.BaseTitleId = cnmt.ApplicationTitleId.ToString("X16");

        if (nspInfo.TitleId != nspInfo.BaseTitleId && cnmt.Type == ContentMetaType.Application)
        {
            nspInfo.Warnings.Add(
                $"{phase} - TitleID Mis-match. Expected {nspInfo.BaseTitleId}, found {nspInfo.TitleId}");
        }

        nspInfo.TitleVersion = $"v{cnmt.TitleVersion.Version}";
        nspInfo.TitleType = (FixedContentMetaType)cnmt.Type;
        nspInfo.MinimumApplicationVersion = cnmt.MinimumApplicationVersion != null
            ? cnmt.MinimumApplicationVersion.ToString()
            : "0.0.0";
        nspInfo.MinimumSystemVersion = cnmt.MinimumSystemVersion ?? new TitleVersion(0);

        if (nspInfo.TitleType != FixedContentMetaType.Patch && nspInfo.TitleType != FixedContentMetaType.Application &&
            nspInfo.TitleType != FixedContentMetaType.Delta && nspInfo.TitleType != FixedContentMetaType.AddOnContent && nspInfo.TitleType != FixedContentMetaType.DataPatch)
        {
            Log.Error($"{phase} - Unsupported content type {nspInfo.TitleType}");
            return (1, null);
        }
        
        foreach (var contentEntry in cnmt.ContentEntries)
        {
            var contentFile = new ContentFileInfo
            {
                FileName = $"{contentEntry.NcaId.ToHexString().ToLower()}.nca",
                NcaId = contentEntry.NcaId.ToHexString(),
                Hash = contentEntry.Hash,
                Type = contentEntry.Type,
            };

            if (contentFile.NcaId != contentFile.Hash.Take(16).ToArray().ToHexString())
            {
                Log.Error($"{phase} - Hash part should match NCA ID ({contentFile.NcaId}).");
                return (1, null);
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
                    nspInfo.DeltaCount++;
                }
            }
            else
            {
                if (nspInfo.RawFileEntries[contentFile.FileName].Size != contentEntry.Size)
                {
                    contentFile.SizeMismatch = true;
                    nspInfo.Errors.Add($"{phase} - NSP file-system contains files with sizes that do not match the CNMT.");
                    nspInfo.CanProceed = false;
                }
            }

            nspInfo.ContentFiles.Add(contentFile.FileName, contentFile);
        }

        if (nspInfo.TitleType == FixedContentMetaType.DataPatch)
        {
            AnsiConsole.Write(new Rule());
            Log.Warning("DataPatch detected. This is a pure DLC update. This has not been spotted before!");
            AnsiConsole.Write(new Rule());
        }

        Log.Verbose($"[olive]NSP Type[/] <- {nspInfo.DisplayType}" + (nspInfo.TitleType is FixedContentMetaType.Patch or FixedContentMetaType.DataPatch
            ? $" ({nspInfo.TitleVersion})"
            : string.Empty));

        var title = nspStructure.Titles.First().Value;
        var mainNca = title.MainNca;

        phase = "[olive]Validate Main NCA[/]";

        if (mainNca == null)
        {
            Log.Error($"{phase} - Failed to open Main NCA.");
            return (1, null);
        }
        
        if (!mainNca.Nca.Header.RightsId.IsZeros())
        {
            nspInfo.RightsId = mainNca.Nca.Header.RightsId.ToHexString();
        }

        if (mainNca.Nca.Header.KeyGeneration > 18)
        {
            Log.Error($"Unsupported key generation ({mainNca.Nca.Header.KeyGeneration.ToString()}). Contact author for an update!");
            return (1, null);
        }

        nspInfo.KeyGeneration = (KeyGeneration)mainNca.Nca.Header.KeyGeneration;
        nspInfo.HasTitleKeyCrypto = mainNca.Nca.Header.HasRightsId;

        if (mainNca.Nca.Header.DistributionType != DistributionType.Download)
        {
            Log.Error($"{phase} - Unsupported distribution type : {mainNca.Nca.Header.DistributionType}");
            return (1, null);
        }

        if (nspInfo is { HasTicket: true, HasTitleKeyCrypto: false, IsDLC: true })
        {
            nspInfo.Errors.Add($"{phase} - NSP has ticket but no title key crypto. This is possibly a Homebrew DLC unlocker. Conversion would lose the ticket + cert.");
            nspInfo.PossibleDlcUnlocker = true;
            nspInfo.CanProceed = false;
        }

        if (nspInfo is { HasTicket: false, HasTitleKeyCrypto: true })
        {
            nspInfo.Errors.Add($"{phase} - NSP is TitleKey encrypted but no valid ticket found.");
            nspInfo.CanProceed = false;
        }

        if (nspInfo.HasTitleKeyCrypto && nspInfo.Ticket != null)
        {
            if (mainNca.Nca.Header.RightsId.IsZeros())
            {
                Log.Error($"{phase} - NCA is encrypted but has empty rights ID.");
                return (1, null);
            }

            phase = "[olive]Validate Ticket[/]";

            if (nspInfo.Ticket.SignatureType != TicketSigType.Rsa2048Sha256)
            {
                Log.Error($"{phase} - Unsupported ticket signature type {nspInfo.Ticket.SignatureType}");
                return (1, null);
            }

            var offset = BitConverter.GetBytes(nspInfo.Ticket.SectHeaderOffset);
            Array.Reverse(offset);

            if (!offset.ToHexString().Equals("000002C0"))
            {
                nspInfo.Warnings.Add($"{phase} - Section Records Offset is incorrect.");
                nspInfo.GenerateNewTicket = true; 
            }

            if (nspInfo.Ticket.LicenseType != LicenseType.Permanent)
            {
                nspInfo.Warnings.Add($"{phase} - Incorrect license-type found.");
                nspInfo.GenerateNewTicket = true;
            }

            if (nspInfo.Ticket.TitleKeyType != TitleKeyType.Common)
            {
                nspInfo.Warnings.Add($"{phase} - Personal ticket type found.");
                nspInfo.GenerateNewTicket = true;
            }

            var propertyMask = (FixedPropertyFlags)nspInfo.Ticket.PropertyMask;
            
            if (nspInfo.Ticket.TicketId != 0)
            {
                nspInfo.Warnings.Add($"{phase} - Ticket has ticket ID set ({nspInfo.Ticket.TicketId}).");
                nspInfo.GenerateNewTicket = true;
            }

            if (nspInfo.Ticket.PropertyMask != 0)
            {
                nspInfo.Warnings.Add($"{phase} - Ticket has property mask set ({propertyMask}).");
                nspInfo.GenerateNewTicket = true;
            }

            if (nspInfo.Ticket.AccountId != 0)
            {
                nspInfo.Warnings.Add($"{phase} - Ticket has account ID set ({nspInfo.Ticket.AccountId})");
                nspInfo.GenerateNewTicket = true;
            }

            if (nspInfo.Ticket.DeviceId != 0)
            {
                nspInfo.Warnings.Add($"{phase} - Ticket has device ID set ({nspInfo.Ticket.DeviceId})");
                nspInfo.GenerateNewTicket = true;
            }
            
            nspInfo.IsOldTicketCrypto = mainNca.Nca.Header.KeyGeneration < 3;

            if (!nspInfo.IsOldTicketCrypto && (nspInfo.Ticket.CryptoType != nspInfo.Ticket.RightsId.Last()))
            {
                nspInfo.Warnings.Add($"{phase} - Ticket has mis-matched crypto settings ({nspInfo.Ticket.CryptoType} vs {nspInfo.Ticket.RightsId.Last()})");
                nspInfo.GenerateNewTicket = true;    
            }

            if (nspInfo.IsOldTicketCrypto && nspInfo.Ticket.CryptoType != 0)
            {
                nspInfo.Warnings.Add($"{phase} - Ticket crypto should be set to zero (Keygen < 3)");
                nspInfo.GenerateNewTicket = true;
            }

            nspInfo.TitleKeyEncrypted = nspInfo.Ticket.GetTitleKey(_keySet);
            nspInfo.TitleKeyDecrypted = mainNca.Nca.GetDecryptedTitleKey();

            if (nspInfo.NormalisedSignature.ToHexString() == nspInfo.Ticket.Signature.ToHexString())
            {
                nspInfo.IsNormalisedSignature = true;
            }

            if (nspInfo.TitleType is (FixedContentMetaType.Application or FixedContentMetaType.AddOnContent))
            {
                nspInfo.IsTicketSignatureValid = true;
                if (!nspInfo.IsNormalisedSignature)
                {
                    nspInfo.Warnings.Add($"{phase} - Ticket signature is not normalised.");
                    nspInfo.GenerateNewTicket = true;
                }
            }
            else
            {
                // For Updates and DLC Updates, we validate the ticket signature
                nspInfo.IsTicketSignatureValid = NsfwUtilities.ValidateTicket(nspInfo.Ticket, settings.CertFile);
            }
            
            if(!nspInfo.IsTicketSignatureValid)
            {
                nspInfo.Errors.Add($"{phase} - Ticket signature is invalid.");
                nspInfo.CanProceed = false;
            }

            nspInfo.MasterKeyRevision = Utilities.GetMasterKeyRevision(mainNca.Nca.Header.KeyGeneration);
        }
        
        // DISPLAY TITLE LOOKUP

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

                nspInfo.DisplayTitleLookupSource = LookupSource.Control;
            }
        }

        if (nspInfo.DisplayTitleLookupSource == LookupSource.Control)
        {
            nspInfo.DisplayTitle = nspInfo.ControlTitle;
        }

        if (nspInfo.DisplayTitleLookupSource == LookupSource.Unknown)
        {
            nspInfo.DisplayTitle = nspInfo.FileName
                .Replace("_", " ")
                .Replace(".nsp", string.Empty);
            nspInfo.DisplayTitleLookupSource = LookupSource.FileName;
        }

        if (settings.KeepName)
        {
            var titleName = nspInfo.FileName;
            if (titleName.Contains('('))
            {
                nspInfo.DisplayTitle = titleName.Split('(', StringSplitOptions.TrimEntries)[0];
                nspInfo.DisplayTitleLookupSource = LookupSource.FileTitle;
            }
            else if (titleName.Contains('['))
            {
                nspInfo.DisplayTitle = titleName.Split('[', StringSplitOptions.TrimEntries)[0];
                nspInfo.DisplayTitleLookupSource = LookupSource.FileTitle;
            }
            else
            {
                nspInfo.DisplayTitle = titleName.Replace(".nsp", string.Empty);
                nspInfo.DisplayTitleLookupSource = LookupSource.FileName;
            }
        }

        if (nspInfo.OutputOptions.IsTitleDbAvailable)
        {
            if (settings.VerifyTitle || nspInfo.DisplayTitleLookupSource == LookupSource.FileName)
            {
                var titleDbTitle = string.Empty;

                if (nspInfo.IsDLC)
                {
                    titleDbTitle = NsfwUtilities.LookUpTitle(_dbConnection, nspInfo.TitleId)?.CleanTitle();
                    nspInfo.DisplayParentTitle = NsfwUtilities.LookUpTitle(_dbConnection, nspInfo.BaseTitleId)?.CleanTitle().RemoveBrackets();
                }
                else
                {
                    titleDbTitle = NsfwUtilities.LookUpTitle(_dbConnection, nspInfo.UseBaseTitleId ? nspInfo.BaseTitleId : nspInfo.TitleId);
                }

                if (!settings.KeepName && !string.IsNullOrEmpty(titleDbTitle))
                {
                    var source = LookupSource.TitleDb;
                    
                    if (nspInfo.DisplayTitleLookupSource == LookupSource.Control)
                    {
                        // Prefer English version from control if version from titledb is not english
                        if ((titleDbTitle[0] > 122 && nspInfo.DisplayTitle[0] < 123) || titleDbTitle.Equals(nspInfo.DisplayTitle, StringComparison.InvariantCultureIgnoreCase))
                        {
                            titleDbTitle = nspInfo.DisplayTitle;
                            source = LookupSource.Control;
                        }
                    }
                    
                    nspInfo.DisplayTitle = titleDbTitle.RemoveBrackets();
                    nspInfo.DisplayTitleLookupSource = source;
                }
            }

            var releaseDate = NsfwUtilities.LookUpReleaseDate(_dbConnection, nspInfo.UseBaseTitleId ? nspInfo.BaseTitleId : nspInfo.TitleId);
            
            if (releaseDate != null)
            {
                nspInfo.ReleaseDate = releaseDate;
            }
            
            var region = NsfwUtilities.LookUpRegions(_dbConnection, nspInfo.UseBaseTitleId ? nspInfo.BaseTitleId : nspInfo.TitleId).Result;

            if (region.Item1 != "UNKNOWN")
            {
                nspInfo.DistributionRegion = region.Item1;
                nspInfo.DistributionRegionList = region.Item2;
            }
        }

        if (nspInfo is { DisplayTitleLookupSource: LookupSource.FileName, IsDLC: true } && nspInfo.FileName.Contains('['))
        {
            var filenameParts = nspInfo.FileName.Split('[', StringSplitOptions.RemoveEmptyEntries|StringSplitOptions.TrimEntries);

            if (filenameParts.Length > 1)
            {
                nspInfo.DisplayTitle = filenameParts[0];

                if (!char.IsDigit(filenameParts[1][0]) && filenameParts.Length > 2)
                {
                    nspInfo.DisplayTitle += " - " + filenameParts[1]
                        .Replace("]", string.Empty)
                        .Replace("dlc", "DLC").Trim();
                }
            }
            
            if(nspInfo.DisplayTitle.Contains('(') && nspInfo.DisplayTitle.Contains(')'))
            {
                var match = Regex.Match(nspInfo.DisplayTitle, @"\((.*?)\)");
            
                if (match.Success)
                { 
                    nspInfo.DisplayTitle = nspInfo.DisplayTitle
                        .Replace(match.Value, string.Empty)
                        .Replace("  "," ").Trim();
                }
            }
        }

        if (nspInfo is { IsDLC: true, OutputOptions.IsTitleDbAvailable: true })
        {
            var parentLanguages = NsfwUtilities.LookupLanguages(_dbConnection, nspInfo.BaseTitleId);
            if (parentLanguages.Length > 0)
            {
                var parentLanguagesList = parentLanguages.Distinct()
                        .Select(x => x switch
                        { 
                            "en" => NacpLanguage.AmericanEnglish,
                            "ja" => NacpLanguage.Japanese,
                            "fr" => NacpLanguage.French,
                            "de" => NacpLanguage.German,
                            "it" => NacpLanguage.Italian,
                            "es" => NacpLanguage.Spanish,
                            "zh" => NacpLanguage.SimplifiedChinese,
                            "ko" => NacpLanguage.Korean,
                            "nl" => NacpLanguage.Dutch,
                            "pt" => NacpLanguage.Portuguese,
                            "ru" => NacpLanguage.Russian,
                            _ => NacpLanguage.AmericanEnglish
                        });
                nspInfo.DisplayParentLanguages = string.Join(',', parentLanguages.Select(x => string.Concat(x[0].ToString().ToUpper(), x.AsSpan(1))));
                nspInfo.ParentLanguages = parentLanguagesList;
            }
        }

        if (!string.IsNullOrEmpty(control.DisplayVersionString.ToString()))
        {
            nspInfo.DisplayVersion = control.DisplayVersionString.ToString().Trim();
            
            if (nspInfo.DisplayVersion.StartsWith('v') || nspInfo.DisplayVersion.StartsWith('V') || nspInfo.DisplayVersion.StartsWith('b'))
            {
                nspInfo.DisplayVersion = nspInfo.DisplayVersion[1..];
            }
        }

        if (control.AttributeFlag.HasFlag(ApplicationControlProperty.AttributeFlagValue.Demo))
        {
            nspInfo.IsDemo = true;
        }

        if (control.AttributeFlag.HasFlag(ApplicationControlProperty.AttributeFlagValue.RetailInteractiveDisplay))
        {
            nspInfo.IsRetailDisplay = true;
        }
        
        var outputName = nspInfo.OutputName;

        if (settings.Convert)
        {
            var targetName = Path.Combine(settings.NspDirectory, $"{outputName}.nsp");

            if (File.Exists(targetName) && !settings.Overwrite)
            {
                Log.Error($"File already exists. ({targetName.EscapeMarkup()}). Use [grey]--overwrite[/] to overwrite an existing file.");
                return (2, null);
            }
        }
        
        // VALIDATE NCAS

        phase = "[olive]Validate NCAs[/]";

        if (nspStructure.NcaCollection.Count == 0)
        {
            Log.Error($"{phase} - No NCAs found.");
            return (1, null);
        }

        foreach (var fsNca in nspStructure.NcaCollection.Values)
        {
            var npdmValidity = !nspInfo.HasSparseNcas ? NsfwUtilities.VerifyNpdm(fsNca.Nca) : Validity.Unchecked;
            
            var ncaInfo = new NcaInfo(fsNca)
            {
                IsHeaderValid = fsNca.Nca.VerifyHeaderSignature() == Validity.Valid,
                IsNpdmValid = npdmValidity == Validity.Valid
            };
            
            // Get encrypted keys from header
            var ver = fsNca.Nca.Header.FormatVersion;
            var kCount = ver == NcaVersion.Nca0 ? 2 : 4;

            var encryptedKeys = new string[kCount];
            var decryptedKeys = new string[kCount];
                
            for (var i = 0; i < kCount; i++)
            {
                encryptedKeys[i] = fsNca.Nca.Header.GetEncryptedKey(i).ToArray().ToHexString();
                decryptedKeys[i] = fsNca.Nca.GetDecryptedKey(i).ToArray().ToHexString();
            }

            ncaInfo.EncryptionKeyIndex = fsNca.Nca.Header.KeyAreaKeyIndex;
            ncaInfo.EncryptedKeys = encryptedKeys;
            ncaInfo.DecryptedKeys = decryptedKeys;
            ncaInfo.RawHeader = fsNca.Nca.OpenHeaderStorage(false).ToArray();

            if (!ncaInfo.IsHeaderValid)
            {
                nspInfo.Errors.Add($"{phase} - {ncaInfo.FileName} <- Header signature is invalid.");
                nspInfo.CanProceed = false;
            }
            
            if(!ncaInfo.IsNpdmValid && fsNca.Nca.Header.ContentType == NcaContentType.Program)
            {
                switch (npdmValidity)
                {
                    case Validity.Unchecked:
                        if (!nspInfo.HasSparseNcas) nspInfo.Errors.Add($"{phase} - {ncaInfo.FileName} <- Error opening NPDM.");
                        break;
                    case Validity.Invalid:
                        nspInfo.Errors.Add($"{phase} - {ncaInfo.FileName} <- NPDM signature is invalid.");
                        nspInfo.CanProceed = false;
                        break;
                }
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
                        if (!(fsNca.Nca.Header.ContentType == NcaContentType.Program && nspInfo.HasSparseNcas))
                        {
                            sectionInfo.IsErrored = true;
                            sectionInfo.ErrorMessage = $"Error opening file-system - {exception.Message}";
                            nspInfo.Errors.Add($"{phase} - {ncaInfo.FileName} ({(ncaInfo.Type == NcaContentType.Data ? "Delta" : ncaInfo.Type)}) (Section {sectionInfo.SectionId}) <- Error opening file-system");
                            nspInfo.CanProceed = false;
                        }
                        else
                        {
                            sectionInfo.IsSparse = true;
                        }
                    }
                }

                ncaInfo.Sections.Add(i, sectionInfo);
            }

            if (settings.SkipHash)
            {
                nspInfo.Warnings.Add($"{phase} - Skipped hash check on : {ncaInfo.FileName}.");
                ncaInfo.HashMatch = HashMatchType.Missing;
                nspInfo.NcaFiles.Add(fsNca.Filename, ncaInfo);
                continue;
            }

            if (settings.ForceHash || (!ncaInfo.IsErrored && nspInfo.HeaderSignatureValidity == Validity.Valid))
            {
                if (settings.ForceHash)
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

                AnsiConsole.Progress()
                    .AutoClear(true) // Do not remove the task list when done
                    .HideCompleted(true) // Hide tasks as they are completed
                    .Columns(new SpinnerColumn(), new TaskDescriptionColumn(), new PercentageColumn(), new RemainingTimeColumn())
                    .Start(ctx =>
                    {
                        var hashTask = ctx.AddTask("Hashing ..", new ProgressTaskSettings { MaxValue = blockCount });

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
                            var expectedHash = string.Empty;
                            
                            if(fsNca.Nca.Header.ContentType == NcaContentType.Meta)
                            {
                                expectedHash = fsNca.Filename[..^9];
                                ncaInfo.HashMatch = ncaHash.StartsWith(expectedHash, StringComparison.InvariantCultureIgnoreCase)
                                    ? HashMatchType.Match
                                    : HashMatchType.Mismatch;
                            }
                            else
                            {
                                expectedHash = nspInfo.ContentFiles[fsNca.Filename].Hash.ToHexString();
                                ncaInfo.HashMatch = string.Equals(ncaHash, expectedHash, StringComparison.InvariantCultureIgnoreCase)
                                    ? HashMatchType.Match
                                    : HashMatchType.Mismatch; 
                            }
                            
                            if (ncaInfo.HashMatch == HashMatchType.Mismatch)
                            {
                                nspInfo.Errors.Add($"{phase} - Hash mismatch for NCA {ncaInfo.FileName} - Expected {expectedHash}, got {ncaHash}");
                                nspInfo.CanProceed = false;
                            }
                        }
                    });
            }

            nspInfo.NcaFiles.Add(fsNca.Filename, ncaInfo);
        }
        
        // NCA ORDER CHECK

        if (NsfwUtilities.IsOrderCorrect(nspInfo.RawFileEntries.Values.ToArray()))
        {
            nspInfo.IsFileOrderCorrect = true;
        }
        else
        {
            nspInfo.Warnings.Add("[olive]NCA File Order[/] <- [red]Non-standard[/]");
        }
        
        // VALIDATION CHECK

        if (settings is { SkipHash: true, Rename: false })
        {
            Log.Information("[olive]Validation Complete[/] <- [olive]Unknown - Hashing skipped.[/]");
        }
        else
        {
            if (nspInfo is { HasErrors: false, HeaderSignatureValidity: Validity.Valid, CopyNewCert: false } 
                && nspInfo.NcaFiles.Values.All(x => !x.IsErrored) 
                && nspInfo.NcaFiles.Values.All(x => x.HashMatch != HashMatchType.Mismatch))
            {
                Log.Information("[olive]Validation Complete[/] <- [green]No problems found[/]");
                Log.Information("[olive]Is Standard NSP? [/] <- " + (nspInfo.IsStandardNsp ? "[green]Yes[/]" : "[red]No[/]"));
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

        if(settings.LogLevel != LogLevel.Quiet)
        {
            AnsiConsole.Write(new Rule().RuleStyle("grey"));
        }
        
        // -- DISPLAY SECTION --
        
        // RAW FILE TREE

        if (settings.LogLevel == LogLevel.Full)
        {
            AnsiConsole.Write(new Padder(RenderUtilities.RenderRawFilesTree(nspInfo.RawFileEntries.Values)).PadLeft(1).PadTop(0).PadBottom(0));
        }

        // CNMT TREE

        if (settings.LogLevel == LogLevel.Full)
        {
            AnsiConsole.Write(new Padder(RenderUtilities.RenderCnmtTree(nspInfo.ContentFiles.Values)).PadLeft(1).PadTop(1).PadBottom(0));
        }

        if (settings is { LogLevel: LogLevel.Full, VerifyTitle: true } && nspInfo.OutputOptions.IsTitleDbAvailable)
        {
            var titleDbCnmt = NsfwUtilities.GetCnmtInfo(_dbConnection, nspInfo.TitleId, nspInfo.TitleVersion[1..]);
            if (titleDbCnmt.Length > 0)
            {
                AnsiConsole.Write(new Padder(RenderUtilities.RenderTitleDbCnmtTree(titleDbCnmt,nspInfo.ContentFiles)).PadLeft(1).PadTop(1).PadBottom(0));
            }
        }

        // NCA TREE

        if (settings.LogLevel == LogLevel.Full)
        {
            AnsiConsole.Write(new Padder(RenderUtilities.RenderNcaTree(nspInfo.NcaFiles.Values, settings.ShowKeys)).PadLeft(1).PadTop(1).PadBottom(0));
        }
        
        // TICKET INFO
        
        if (nspInfo.Ticket != null && (settings.LogLevel == LogLevel.Full))
        {
            AnsiConsole.Write(new Padder(RenderUtilities.RenderTicket(nspInfo.Ticket, nspInfo.IsNormalisedSignature, nspInfo.IsTicketSignatureValid, nspInfo.IsOldTicketCrypto)).PadLeft(1).PadRight(0).PadBottom(0).PadTop(1));
        }
        
        // TITLEDB - REGIONAL TITLES
        
        if (nspInfo.OutputOptions.IsTitleDbAvailable && settings.RegionalTitles)
        {
            var titleResults = NsfwUtilities.GetTitleDbInfo(_dbConnection, nspInfo.UseBaseTitleId && nspInfo.IsDLC ? nspInfo.BaseTitleId : nspInfo.TitleId).Result;
        
            if (titleResults.Length > 0)
            {
                AnsiConsole.Write(new Padder(RenderUtilities.RenderRegionalTitles(titleResults)).PadLeft(1).PadTop(1).PadBottom(0));
            }
        }
        
        // TITLEDB - DLC RELATED TITLES 
        
        if (nspInfo.OutputOptions.IsTitleDbAvailable && settings.RelatedTitles && nspInfo.IsDLC)
        {
            var relatedResults = NsfwUtilities.LookUpRelatedTitles(_dbConnection, nspInfo.TitleId).Result;
        
            if (relatedResults.Length > 0)
            {
                AnsiConsole.Write(new Padder(RenderUtilities.RenderDlcRelatedTitles(relatedResults)).PadLeft(1).PadTop(1).PadBottom(0));
            }
        }
        
        // TITLEDB - UPDATES
        
        if (nspInfo.OutputOptions.IsTitleDbAvailable && settings.Updates && nspInfo.TitleType is FixedContentMetaType.Application or FixedContentMetaType.Patch)
        {
            var versions = NsfwUtilities.LookUpUpdates(_dbConnection, nspInfo.UseBaseTitleId ? nspInfo.BaseTitleId : nspInfo.TitleId).Result;
            
            if (versions.Length > 0)
            {
                AnsiConsole.Write(new Padder(RenderUtilities.RenderTitleUpdates(versions, nspInfo.TitleVersion, Path.GetDirectoryName(nspInfo.FilePath) ?? string.Empty,nspInfo.DisplayTitle,nspInfo.ReleaseDate)).PadLeft(1).PadTop(1).PadBottom(1));
            }
        }
        
        // PROPERTIES

        if (settings.LogLevel != LogLevel.Quiet)
        {
            AnsiConsole.Write(new Padder(RenderUtilities.RenderProperties(nspInfo, outputName)).PadLeft(1).PadTop(1).PadBottom(1));
        }
        
        if(!nspInfo.CanProceed && !(settings is { Extract: true, ForceExtract: true }))
        {
            Log.Fatal(!_batchMode && !cdnMode && settings.LogLevel != LogLevel.Full
                ? "NSP Validation failed. Use [grey]--full[/] to see more details."
                : "NSP Validation failed.");

            return (1, null);
        }
        
        if(settings is { Rename: false, Extract: false, Convert: false })
        {
            return (0, nspInfo);
        }
        
        // RENAME
        
        if (settings.Rename)
        {
            fileSystem.Dispose();
            localFile.Dispose();
            
            if (nspInfo.FileName ==  outputName+".nsp")
            {
                Log.Information("Renaming skipped. Nothing to do. Filename matches already.");
                return (0, nspInfo);
            }
            
            var targetDirectory = Path.GetDirectoryName(nspFullPath);
            
            if(targetDirectory == null || !Directory.Exists(targetDirectory))
            {
                Log.Error($"Failed to open directory ({targetDirectory.EscapeMarkup()}).");
                return (1, null);
            }
            
            var targetName = Path.Combine(targetDirectory, outputName + ".nsp");
            
            if (targetName.Length > 258 && RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                Log.Error($"Path too long for Windows ({targetName.Length})");
                return (1, null);
            }
            
            if (File.Exists(targetName) && !settings.Overwrite)
            {
                Log.Error($"File with the same name already exists. ({outputName.EscapeMarkup()}.nsp). Use [grey]--overwrite[/] to overwrite an existing file.");
                return (2, null);
            }
            
            if (settings.DryRun)
            {
                Log.Information($"[[[green]DRYRUN[/]]] -> Rename FROM: [olive]{nspInfo.FileName.EscapeMarkup()}[/]");
                Log.Information($"[[[green]DRYRUN[/]]] ->   Rename TO: [olive]{outputName.EscapeMarkup()}.nsp[/]");
                return (0, nspInfo);
            }

            try
            {
                File.Move(nspFullPath, Path.Combine(targetDirectory, outputName + ".nsp"));
            }
            catch (Exception exception)
            {
                Log.Error($"Failed to rename file. {exception.Message}");
                return (1, null);
            }

            Log.Information($"Renamed TO: [olive]{outputName.EscapeMarkup()}[/]");
            return (0, null);
        }
        
        if (nspInfo is { Ticket: not null, HasTitleKeyCrypto: true } && (!nspInfo.IsTicketSignatureValid || nspInfo.GenerateNewTicket))
        {
            var signature = NsfwUtilities.FixedSignature;
            
            if(nspInfo is { IsTicketSignatureValid: true, TitleType: FixedContentMetaType.Patch or FixedContentMetaType.DataPatch })
            {
                Log.Warning("Attempting to modify ticket for an update.");
                signature = nspInfo.Ticket.Signature;
            }
            
            if(nspInfo is { IsTicketSignatureValid: false, TitleType: FixedContentMetaType.Patch or FixedContentMetaType.DataPatch })
            {
                Log.Error("Unable to update ticket for an update where ticket signature is invalid.");
            }
            else
            {
                nspInfo.Ticket = NsfwUtilities.CreateTicket(nspInfo.MasterKeyRevision, nspInfo.Ticket.RightsId, nspInfo.TitleKeyEncrypted, signature);
                Log.Information("Generated new normalised ticket.");
            }
        }
        
        // EXTRACT
     
        if(settings.Extract)
        {
            var outDir = Path.Combine(settings.CdnDirectory, outputName);
            
            if (outDir.Length > 254)
            {
                Log.Error($"Path too long for Windows ({outDir.Length})");
                return (1, null);
            }

            if (Directory.Exists(outDir) && !settings.Overwrite)
            {
                Log.Error($"Directory with the same name already exists. ({outDir.EscapeMarkup()}). Use [grey]--overwrite[/] to overwrite existing files.");
                return (2, null);
            }
            
            if(settings.DryRun)
            {
                Log.Information($"[[[green]DRYRUN[/]]] -> Would create: [olive]{outDir.EscapeMarkup()}[/]");
            }
            else
            {
                Directory.CreateDirectory(outDir);
            }
            
            foreach (var nca in nspStructure.NcaCollection.Values)
            {
                if(settings.DryRun)
                {
                    Log.Information($"[[[green]DRYRUN[/]]] -> Would extract: [olive]{nca.Filename}[/]");
                    continue;
                }

                try
                {
                    var stream = nca.Nca.BaseStorage.AsStream();
                    var outFile = Path.Combine(outDir, nca.Filename);

                    if (File.Exists(outFile) && !settings.Overwrite)
                    {
                        Log.Error($"Skipping. File already exists. ({outFile.EscapeMarkup()})");
                        continue;
                    }

                    using var outStream = new FileStream(outFile, FileMode.Create, FileAccess.ReadWrite);
                    stream.CopyStream(outStream, stream.Length);
                }
                catch (Exception exception)
                {
                    Log.Error($"Failed to extract file. {exception.Message}");
                    return (1, null);
                }
            }

            if (settings.ExtractAll)
            {
                foreach (var miscFile in nspInfo.RawFileEntries.Values.Where(x => !x.FullPath.EndsWith(".nca") && !x.FullPath.EndsWith(".tik")))
                {
                    if(settings.DryRun)
                    {
                        Log.Information($"[[[green]DRYRUN[/]]] -> Would extract: [olive]{miscFile.Name}[/]");
                        continue;
                    }
                    
                    using var miscFileRef = new UniqueRef<IFile>();
                    fileSystem.OpenFile(ref miscFileRef.Ref, miscFile.FullPath.ToU8Span(), OpenMode.Read).ThrowIfFailure();
                    
                    try
                    {
                        var outFile = Path.Combine(outDir, miscFile.Name);
                        using var outStream = new FileStream(outFile, FileMode.Create, FileAccess.ReadWrite);
                        miscFileRef.Get.GetSize(out var fileSize);
                        miscFileRef.Get.AsStream().CopyStream(outStream, fileSize);
                    }
                    catch (Exception exception)
                    {
                        Log.Error($"Failed to extract file. {exception.Message}");
                        return (1, null);
                    }
                }
            }

            if (settings.DumpHeaders)
            {
                foreach (var info in nspInfo.NcaFiles.Values)
                {
                    var headerFilePath = Path.Combine(outDir, $"{info.FileName}.header");
                    File.WriteAllBytes(headerFilePath, info.RawHeader);
                }
            }
        
            if (nspInfo.HasTitleKeyCrypto && nspInfo.Ticket != null)
            {
                 var decFile = $"{nspInfo.Ticket.RightsId.ToHexString().ToLower()}.dectitlekey.tik";
                 var encFile = $"{nspInfo.Ticket.RightsId.ToHexString().ToLower()}.enctitlekey.tik";
                 
                 if(settings.DryRun)
                 {
                     Log.Information($"[[[green]DRYRUN[/]]] -> Would extract: [olive]{decFile.EscapeMarkup()}[/]");
                     Log.Information($"[[[green]DRYRUN[/]]] -> Would extract: [olive]{encFile.EscapeMarkup()}[/]");
                     Log.Information($"[[[green]DRYRUN[/]]] -> Would extract: [olive]{nspInfo.Ticket.RightsId.ToHexString().ToLower()}.tik[/]");
                     return (0, nspInfo);
                 }

                 try
                 {
                     File.WriteAllBytes(Path.Combine(outDir, decFile), nspInfo.TitleKeyDecrypted);
                     File.WriteAllBytes(Path.Combine(outDir, encFile), nspInfo.TitleKeyEncrypted);
                     File.WriteAllBytes(Path.Combine(outDir, $"{nspInfo.Ticket.RightsId.ToHexString().ToLower()}.tik"), nspInfo.Ticket.File);
                 }
                 catch (Exception exception)
                 {
                     Log.Error($"Failed to extract ticket files. {exception.Message}");
                     return (1, null);
                 }
            }
            
            Log.Information($"[[[green]DONE[/]]] -> Extracted to: [olive]{outDir.EscapeMarkup()}[/]");
        }
        
        // CONVERT

        if (settings.Convert)
        {
            if (nspInfo.IsStandardNsp && !settings.ForceConvert)
            {
                Log.Information("File is already in Standard NSP format. Skipping conversion.");
                return (0, nspInfo);
            }
            
            if (settings.DryRun)
            {
                Log.Information($"[[[green]DRYRUN[/]]] -> Would create: [olive]{outputName.EscapeMarkup()}.nsp[/]");
            }
            
            
            var certFile = new LocalFile(settings.CertFile, OpenMode.Read);
            var ticketFile = nspInfo.Ticket != null ? new MemoryStorage(nspInfo.Ticket.GetBytes()).AsFile(OpenMode.Read) : null;

            var buildStatus = 0;
            
            AnsiConsole.Status()
                .Start($"Building Standard NSP => [olive]{outputName.EscapeMarkup()}[/]", ctx =>
                {
                    ctx.Spinner(Spinner.Known.Ascii);
                    
                    var builder = new PartitionFileSystemBuilder();

                    // Add NCAs in CNMT order
                    foreach (var contentFile in nspInfo.ContentFiles.Values)
                    {
                        if (!settings.KeepDeltas && contentFile.Type == ContentType.DeltaFragment)
                        {
                            Log.Information($"[[[green]SKIP[/]]] -> Skipping delta fragment: [olive]{contentFile.FileName.EscapeMarkup()}[/]");
                            continue;
                        }

                        if (!nspInfo.NcaFiles.TryGetValue(contentFile.FileName, out var ncaInfo))
                        {
                            if (contentFile.Type != ContentType.DeltaFragment)
                            {
                                Log.Error($"Failed to locate NCA file [olive]{contentFile.FileName.EscapeMarkup()}[/] in the NSP file-system.");
                                buildStatus = 1;
                                return;
                            }

                            Log.Information($"[[[green]SKIP[/]]] -> Delta fragment [olive]{contentFile.FileName.EscapeMarkup()}[/] not found in the NSP file-system. Skipping.");
                            continue;
                        }

                        var nca = ncaInfo.FsNca;

                        if (settings.DryRun)
                        {
                            Log.Information($"[[[green]DRYRUN[/]]] -> Would add: [olive]{nca.Filename}[/]");
                        }
                        else
                        {
                            builder.AddFile(nca.Filename, nca.Nca.BaseStorage.AsFile(OpenMode.Read));
                        }
                    }

                    // Add CNMT/MetaNCA
                    if (settings.DryRun)
                    {
                        Log.Information($"[[[green]DRYRUN[/]]] -> Would add: [olive]{nspStructure.MetaNca?.Filename}[/]");
                    }
                    else
                    {
                        builder.AddFile(nspStructure.MetaNca?.Filename, nspStructure.MetaNca?.Nca.BaseStorage.AsFile(OpenMode.Read));
                    }

                    if (nspInfo is { HasTitleKeyCrypto: true, Ticket: not null })
                    {
                        if (settings.DryRun)
                        {
                            Log.Information($"[[[green]DRYRUN[/]]] -> Would add: [olive]{nspInfo.Ticket.RightsId.ToHexString().ToLower()}.tik[/]");
                            Log.Information($"[[[green]DRYRUN[/]]] -> Would add: [olive]{nspInfo.Ticket.RightsId.ToHexString().ToLower()}.cert[/]");
                        }
                        else
                        {
                            builder.AddFile($"{nspInfo.Ticket.RightsId.ToHexString().ToLower()}.tik", ticketFile);
                            builder.AddFile($"{nspInfo.Ticket.RightsId.ToHexString().ToLower()}.cert", certFile);
                        }
                    }

                    var targetName = Path.Combine(settings.NspDirectory, $"{outputName}.nsp");

                    if (targetName.Length > 254)
                    {
                        Log.Error($"Path too long for Windows ({targetName.Length})");
                        buildStatus = 1;
                        return;
                    }

                    if (targetName == nspFullPath)
                    {
                        Log.Error("Trying to save converted file to the same location as the input file.");
                        buildStatus = 1;
                        return;
                    }

                    if (File.Exists(targetName) && !settings.Overwrite)
                    {
                        Log.Error($"File already exists. ({targetName.EscapeMarkup()}). Use [grey]--overwrite[/] to overwrite an existing file.");
                        buildStatus = 2;
                        return;
                    }

                    if (settings.DryRun) return;

                    try
                    {
                        using var outStream = new FileStream(targetName, FileMode.Create, FileAccess.ReadWrite);
                        var builtPfs = builder.Build(PartitionFileSystemType.Standard);
                        builtPfs.GetSize(out var pfsSize).ThrowIfFailure();
                        builtPfs.CopyToStream(outStream, pfsSize);
                    }
                    catch (Exception exception)
                    {
                        Log.Error($"Failed to convert file. {exception.Message}");
                        buildStatus = 1;
                    }
                });
            
            fileSystem.Dispose();
            localFile.Dispose();
            certFile.Dispose();
            ticketFile?.Dispose();

            if(buildStatus != 0)
            {
                return (buildStatus, null);
            }

            Log.Information($"Converted: [olive]{outputName.EscapeMarkup()}.nsp[/]");
            
            if (settings.DeleteSource)
            {
                try
                {
                    File.Delete(nspFullPath);
                    Log.Information($"Cleaned  : [olive]{Path.GetFileName(nspFullPath.EscapeMarkup())}[/]");
                }
                catch (Exception exception)
                {
                    Log.Error($"Failed to delete file. {exception.Message}");
                    return (1, null);
                }
            }
        }

        return (0, nspInfo);
    }
    
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
            Log.Warning("[olive]Import tickets[/] - Multiple tickets found. Using first.");
            return;
        }
        
        nspInfo.Ticket = ticket;
    }
    
}