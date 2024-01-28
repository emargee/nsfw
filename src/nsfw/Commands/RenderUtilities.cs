using LibHac.Common;
using LibHac.Ncm;
using LibHac.Tools.Es;
using LibHac.Util;
using Nsfw.Nsp;
using Spectre.Console;
using Spectre.Console.Rendering;

namespace Nsfw.Commands;

public static class RenderUtilities
{
    private const string ValidationFail = "[red][[X]][/]";
    private const string ValidationPass = "[green][[V]][/]";
    private const string HeaderPass = "[green][[H]][/]";
    private const string HeaderFail = "[red][[H]][/]";
    private const string ValidationSkipped = "[olive][[-]][/]";
    private const string HashPass = "[green][[PASS]][/]";
    private const string HashFail = "[red][[FAIL]][/]";
    private const string HashSkip = "[olive][[SKIP]][/]";
    private const string PlainValid = "[green]Valid[/]";
    private const string PlainInValid = "[red]Invalid[/]";
    
    public static Tree RenderRawFilesTree(IEnumerable<RawContentFileInfo> rawFileValues)
    {
        var rawFileTree = new Tree("PFS0:")
        {
            Expanded = true,
            Guide = TreeGuide.Line
        };
        foreach (var rawFile in rawFileValues)
        {
            var displayLine = $"{rawFile.Name} ({rawFile.DisplaySize})";
                
            if (rawFile.IsLooseFile)
            {
                rawFileTree.AddNode($"[grey]{displayLine}[/]");
            }
            else
            {
                rawFileTree.AddNode($"{displayLine}");
            }
        }

        return rawFileTree;
    }

    public static Tree RenderCnmtTree(IEnumerable<ContentFileInfo> contentFiles)
    {
        var metaTree = new Tree("Metadata Content:")
        {
            Expanded = true,
            Guide = TreeGuide.Line
        };
        foreach (var contentFile in contentFiles)
        {
            if(contentFile.Type == ContentType.Meta)
            {
                continue;
            }
                
            var status = contentFile.IsMissing || contentFile.SizeMismatch ? ValidationFail : ValidationPass;
            var error = contentFile.IsMissing ? "<- Missing" : contentFile.SizeMismatch ? "<- Size Mismatch" : string.Empty;
            metaTree.AddNode($"{status} {contentFile.FileName} [[{contentFile.Type}]] {error}");
        }

        return metaTree;
    }

    public static Tree RenderTitleDbCnmtTree(IEnumerable<CnmtInfo> titleDbCnmt, Dictionary<string, ContentFileInfo> contentFiles)
    {
        var titleDbTree = new Tree("TitleDB CNMT:")
        {
            Expanded = true,
            Guide = TreeGuide.Line
        };
        foreach (var contentEntry in titleDbCnmt)
        {
            var status = contentFiles.ContainsKey(contentEntry.NcaId + ".nca") ? ValidationPass : ValidationFail;
            titleDbTree.AddNode($"{status} {contentEntry.NcaId} [[{(ContentType)contentEntry.NcaType}]]");
        }
        
        return titleDbTree;
    }

    public static Tree RenderNcaTree(IEnumerable<NcaInfo> ncaFiles, bool showKeys)
    {
        var ncaTree = new Tree("NCAs:")
        {
            Expanded = true,
            Guide = TreeGuide.Line
        };
        foreach (var ncaFile in ncaFiles)
        {
            var status = !ncaFile.IsHeaderValid ? HeaderFail : HeaderPass;
            var hashStatus = ncaFile.HashMatch switch
            {
                HashMatchType.Match => HashPass,
                HashMatchType.Mismatch => HashFail,
                _ => HashSkip
            };
            var ncaNode = new TreeNode(new Markup($"{status}{hashStatus} {ncaFile.FileName}"));
            ncaNode.Expanded = true;

            foreach (var section in ncaFile.Sections.Values)
            {
                var sectionStatus = section.IsErrored ? ValidationFail : section.IsPatchSection ? ValidationSkipped : ValidationPass;

                if (section.IsErrored)
                {
                    ncaNode.AddNode($"{sectionStatus} Section {section.SectionId} <- [red]{section.ErrorMessage}[/]");
                }
                else
                {
                    var sparse = section.IsSparse ? "[grey](Sparse)[/]" : string.Empty;
                    var subNode = new TreeNode(new Markup($"{sectionStatus} Section {section.SectionId} [grey]({section.EncryptionType})[/]{sparse}"));
                    
                    ncaNode.AddNode(subNode);
                }

            }

            if (showKeys)
            {
                var keyNode = new TreeNode(new Markup("[grey][[KEYS]][/]"))
                {
                    Expanded = true
                };
                for (var i = 0; i < ncaFile.EncryptedKeys.Length; i++)
                {
                    keyNode.AddNode($"[grey][[{i}]] {ncaFile.EncryptedKeys[i]}[/]");
                }
                
                ncaNode.AddNode(keyNode);
            }

            ncaTree.AddNode(ncaNode);
        }

        return ncaTree;
    }

    public static Table RenderTicket(Ticket ticket, bool isNormalised, bool isTicketSignatureValid, bool? isOldTicketCrypto)
    {
        var tikTable = new Table
        {
            ShowHeaders = false
        };
        tikTable.AddColumn("Property");
        tikTable.AddColumn("Value");
        
        if (isNormalised)
        {
            tikTable.AddRow("Ticket Signature", "[green]Normalised[/]");
        }
        else
        {
            tikTable.AddRow("Ticket Signature", isTicketSignatureValid ? "[green]Valid[/]" : "[red]Invalid[/]");
        }
        
        tikTable.AddRow("Issuer", ticket.Issuer == "Root-CA00000003-XS00000020" ? $"[green]{ticket.Issuer}[/]" : $"[red]{ticket.Issuer}[/]");
        tikTable.AddRow("Format Version", "0x" + ticket.FormatVersion.ToString("X"));
        tikTable.AddRow("TitleKey Type", ticket.TitleKeyType == TitleKeyType.Common ? $"[green]{ticket.TitleKeyType}[/]" : $"[red]{ticket.TitleKeyType}[/]");
        tikTable.AddRow("Ticket Id", ticket.TicketId == 0 ? "[green]Not Set[/]" : $"[red]Set ({ticket.TicketId:X})[/]");
        tikTable.AddRow("Ticket Version", ticket.TicketVersion == 0 ? "[green]Not Set[/]" : $"[red]Set ({ticket.TicketVersion:X})[/]");
        tikTable.AddRow("License Type", ticket.LicenseType == LicenseType.Permanent ? $"[green]{ticket.LicenseType}[/]" : $"[red]{ticket.LicenseType}[/]");

        if (isOldTicketCrypto != null)
        {
            if (!isOldTicketCrypto.Value)
            {
                tikTable.AddRow("Crypto Revision", ticket.CryptoType == ticket.RightsId.Last() ? $"[green]0x{ticket.CryptoType:X}[/]" : $"[red]0x{ticket.CryptoType:X}[/]");
            }
            else
            {
                tikTable.AddRow("Crypto Revision", ticket.CryptoType == 0 ? $"[green]0x{ticket.CryptoType:X}[/]" : $"[red]0x{ticket.CryptoType:X}[/]");
            }
        }
        else
        {
            tikTable.AddRow("Crypto Revision", ticket.CryptoType.ToString("X8"));
        }

        tikTable.AddRow("Device Id", ticket.DeviceId == 0 ? "[green]Not Set[/]" : $"[red]Set ({ticket.DeviceId:X})[/]");
        tikTable.AddRow("Account Id", ticket.AccountId == 0 ? "[green]Not Set[/]" : $"[red]Set ({ticket.AccountId:X})[/]");
        tikTable.AddRow("Rights Id", ticket.RightsId.ToHexString());
        tikTable.AddRow("Signature Type", ticket.SignatureType.ToString());
        
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
        
        tikTable.AddRow(new Text("Properties"), propertyTable);

        return tikTable;
    }

    public static Table RenderRegionalTitles(IEnumerable<GameInfo> titleResults)
    {
        var regionTable = new Table { ShowHeaders = false };
        regionTable.AddColumns("Region", "Title");
        regionTable.AddRow(new Text("Regional Titles"));
        regionTable.AddEmptyRow();
        foreach (var titleResult in titleResults.DistinctBy(x => x.RegionLanguage))
        {
            regionTable.AddRow(new Markup($"{titleResult.Name!.ReplaceLineEndings(string.Empty).EscapeMarkup()}"), new Markup($"{titleResult.RegionLanguage.ToUpper()}"));
        }

        return regionTable;
    }

    public static Table RenderDlcRelatedTitles(IEnumerable<string> relatedTitles)
    {
        var relatedTable = new Table() { ShowHeaders = false };
        relatedTable.AddColumn("Title");
        relatedTable.AddRow(new Text("Related DLC Titles"));
        relatedTable.AddEmptyRow();
        foreach (var relatedResult in relatedTitles.Distinct())
        {
            relatedTable.AddRow(new Markup($"{relatedResult.ReplaceLineEndings(string.Empty).EscapeMarkup()}"));
        }

        return relatedTable;
    }

    public static Table RenderTitleUpdates(IEnumerable<TitleVersions> versions, string titleVersion, string rootDirectory, string displayTitle, DateTime? releaseDate)
    {
        var updateTable = new Table() { ShowHeaders = false };
        updateTable.AddColumn("Version");
        updateTable.AddColumn("Date");
        updateTable.AddColumn("File");
        updateTable.AddRow(new Text("Updates"));
        updateTable.AddEmptyRow();

        var cleanTitle = displayTitle.CleanTitle();
        var relDate = releaseDate?.ToString("yyyy-MM-dd") ?? "????-??-??";
        
        var foundBaseFile = Directory.GetFiles(rootDirectory, cleanTitle + "*[v0][BASE]*").FirstOrDefault();
        if (foundBaseFile != null)
        {
            updateTable.AddRow("[green]v0[/]", relDate, foundBaseFile.Replace(rootDirectory, string.Empty).TrimStart('\\').EscapeMarkup());
        }
        else
        {
            updateTable.AddRow("v0", relDate, "[grey]MISSING[/]");
        }

        foreach (var version in versions)
        {
            var foundFile = Directory.GetFiles(rootDirectory, cleanTitle + $"*[v{version.Version}]*").FirstOrDefault();

            if (foundFile != null)
            {
                updateTable.AddRow($"[green]v{version.Version}[/]", $"{version.ReleaseDate}", foundFile.Replace(rootDirectory, string.Empty).TrimStart('\\').EscapeMarkup());
            }
            else
            {
                if ("v"+version.Version == titleVersion)
                {
                    updateTable.AddRow($"[green]v{version.Version}[/]", $"[green]{version.ReleaseDate}[/]");
                }
                else
                {
                    updateTable.AddRow($"v{version.Version}", $"{version.ReleaseDate}", "[grey]MISSING[/]");
                }
            }
        }

        return updateTable;
    }

    public static Table RenderProperties(NspInfo nspInfo, string outputName)
    {
        var propertiesTable = new Table() { ShowHeaders = false };
        propertiesTable.AddColumns("Name", "Value");

        propertiesTable.AddRow("Title",
            $"[olive]{nspInfo.DisplayTitle.EscapeMarkup()}[/]" + " (From " + nspInfo.DisplayTitleLookupSource + ")");

        if (nspInfo.IsDLC && !string.IsNullOrEmpty(nspInfo.DisplayParentTitle))
        {
            propertiesTable.AddRow("Parent Title",
                $"[olive]{nspInfo.DisplayParentTitle.EscapeMarkup()}[/]" + " (From " + nspInfo.DisplayTitleLookupSource + ")");
        }

        if (nspInfo.HasLanguages)
        {
            propertiesTable.AddRow("Languages", string.Join(",", nspInfo.LanguagesFull));
            propertiesTable.AddRow("Languages (Short)", string.Join(",", nspInfo.LanguagesShort));
        }

        if (nspInfo.IsDLC && nspInfo.DisplayParentLanguages != NspInfo.Unknown)
        {
            propertiesTable.AddRow("Parent Languages", nspInfo.DisplayParentLanguages + " (From TitleDb)");
        }
        
        propertiesTable.AddRow("Languages Output", nspInfo.OutputOptions.LanguageMode.ToString());

        propertiesTable.AddRow("Title ID", nspInfo.TitleId);
        if (nspInfo.UseBaseTitleId)
        {
            propertiesTable.AddRow("Base Title ID", nspInfo.BaseTitleId);
        }

        propertiesTable.AddRow("Title Type", nspInfo.DisplayType);
        propertiesTable.AddRow("Title Version", nspInfo.TitleVersion == "v0" ? "BASE (v0)" : nspInfo.TitleVersion);
        propertiesTable.AddRow("Is Interactive Display ?", nspInfo.IsRetailDisplay ? "[olive]Yes[/]" : "No");
        propertiesTable.AddRow("Is Demo ?", nspInfo.IsDemo ? "[olive]Yes[/]" : "No");
        if (nspInfo is { ReleaseDate: not null, TitleType: FixedContentMetaType.Application })
        {
            propertiesTable.AddRow("Release Date", nspInfo.ReleaseDate.Value.ToString("yyyy-MM-dd"));
        }
        propertiesTable.AddRow("Has Sparse NCAs ?", nspInfo.HasSparseNcas ? "[olive]Yes[/]" : "No");
        propertiesTable.AddRow("Key Generation", nspInfo.DisplayKeyGeneration);
        propertiesTable.AddRow("NSP Version", nspInfo.DisplayVersion);
        propertiesTable.AddRow("Rights ID", nspInfo.RightsId);
        propertiesTable.AddRow("Header Validity", nspInfo.HeaderSignatureValidity == Validity.Valid ? PlainValid : PlainInValid);
        propertiesTable.AddRow("NCA Validity", nspInfo.NcaValidity == Validity.Valid ? PlainValid : PlainInValid);
        propertiesTable.AddRow("Meta Validity", nspInfo.ContentValidity == Validity.Valid ? PlainValid : PlainInValid);
        propertiesTable.AddRow("Raw File Count", nspInfo.RawFileEntries.Count + $" ({nspInfo.RawFileEntries.Keys.Count(x => x.EndsWith(".nca"))} NCAs" + (nspInfo.DeltaCount > 0 ? $" + {nspInfo.DeltaCount} Missing Deltas" : "") + ") ");
        propertiesTable.AddRow("Has loose files ?", nspInfo.HasLooseFiles ? "[red]Yes[/]" : "[green]No[/]");
        propertiesTable.AddRow("NCA File Order", nspInfo.IsFileOrderCorrect ? "[green]Correct[/]" : "[red]Non-Standard[/]");
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
                        ? PlainValid
                        : PlainInValid + " (Signature Mismatch) - Will generate new ticket.");
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
            propertiesTable.AddRow("Minimum System Version", nspInfo.MinimumSystemVersion.Version == 0 ? "None" : nspInfo.MinimumSystemVersion.ToString());
        }

        if (!string.IsNullOrEmpty(outputName))
        {
            propertiesTable.AddRow("Output Name", $"[olive]{outputName.EscapeMarkup()}[/]");
        }

        propertiesTable.AddRow("[olive]Validation[/]", nspInfo.CanProceed ? "[green]Passed[/]" : "[red]Failed[/]");
        propertiesTable.AddRow("[olive]Standard NSP?[/]", nspInfo.IsStandardNsp ? "[green]Passed[/]" : "[red]Failed[/]");
        
        return propertiesTable;
    }
}