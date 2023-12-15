

using System.Diagnostics.CodeAnalysis;
using System.Runtime.InteropServices.ComTypes;
using System.Security.Cryptography;
using LibHac.Common;
using LibHac.Common.Keys;
using LibHac.Fs;
using LibHac.Fs.Fsa;
using LibHac.FsSystem;
using LibHac.Ncm;
using LibHac.Tools.FsSystem;
using LibHac.Tools.FsSystem.NcaUtils;
using LibHac.Tools.Ncm;
using LibHac.Util;
using Nsfw.Nsp;
using Spectre.Console;
using Spectre.Console.Cli;
using Path = System.IO.Path;

namespace Nsfw.Commands;

public class MetaPropertiesCommand : Command<MetaPropertiesSettings>
{
    public override int Execute([NotNull] CommandContext context, [NotNull] MetaPropertiesSettings settings)
    {
        var keySet = ExternalKeyReader.ReadKeyFile(settings.KeysFile);
        
        var metaNca = new Nca(keySet, new LocalStorage(settings.CnmtFile, FileAccess.Read));

        if(!metaNca.CanOpenSection(0))
        {
            AnsiConsole.MarkupLine("[red]Cannot open section 0[/]");
            return 1;
        }
        
        var nspInfo = new NspInfo(settings.CnmtFile);
        
        var fs = metaNca.OpenFileSystem(NcaSectionType.Data, IntegrityCheckLevel.ErrorOnInvalid);
        var fsCnmtPath = fs.EnumerateEntries("/", "*.cnmt").Single().FullPath;
        
        using var file = new UniqueRef<IFile>();
        fs.OpenFile(ref file.Ref, fsCnmtPath.ToU8Span(), OpenMode.Read).ThrowIfFailure();
        
        var cnmt = new Cnmt(file.Release().AsStream());
        
        foreach (var contentEntry in cnmt.ContentEntries)
        {
            var contentFile = new ContentFileInfo
            {
                FileName = $"{contentEntry.NcaId.ToHexString().ToLower()}.nca",
                NcaId = contentEntry.NcaId.ToHexString(),
                Hash = contentEntry.Hash,
                Type = contentEntry.Type
            };
            
            nspInfo.ContentFiles.Add(contentFile.FileName, contentFile);
        }
        
        var sha256 = SHA256.Create();

        var ncaStream = metaNca.BaseStorage.AsStream();
        var buffer = new byte[0x4000];
        int read;
        while ((read = ncaStream.Read(buffer, 0, buffer.Length)) > 0)
        {
            sha256.TransformBlock(buffer, 0, read, null, 0);
        }

        sha256.TransformFinalBlock(Array.Empty<byte>(), 0, 0);
        var ncaHash = sha256.Hash?.Take(16).ToArray().ToHexString();
        
        var propertiesTable = new Table { ShowHeaders = false };
        propertiesTable.AddColumns("Name", "Value");

        propertiesTable.AddRow("Filename", Path.GetFileName(settings.CnmtFile));
        propertiesTable.AddRow("SHA256 Hash", ncaHash?.ToLowerInvariant() ?? "UNKNOWN");
        propertiesTable.AddRow("Header Validity", metaNca.VerifyHeaderSignature().ToString());
        propertiesTable.AddRow("Type", cnmt.Type.ToString());
        propertiesTable.AddRow("Title Version", cnmt.TitleVersion.ToString());
        propertiesTable.AddRow("Title Id", cnmt.TitleId.ToString("x16"));
        propertiesTable.AddRow("Content Count", cnmt.ContentEntryCount.ToString());
        propertiesTable.AddRow("Extended Data", (cnmt.ExtendedData == null).ToString());
        propertiesTable.AddRow("FieldD", "0x" + cnmt.FieldD.ToString("x8"));
        propertiesTable.AddRow("Table Offset",cnmt.TableOffset.ToString());
        propertiesTable.AddRow("App Title Id", cnmt.ApplicationTitleId.ToString("x16"));
        propertiesTable.AddRow("MetaEntryCount", cnmt.MetaEntryCount.ToString());
        propertiesTable.AddRow("Meta Attributes - None", cnmt.ContentMetaAttributes.HasFlag(ContentMetaAttribute.None).ToString());
        propertiesTable.AddRow("Meta Attributes - Compacted", cnmt.ContentMetaAttributes.HasFlag(ContentMetaAttribute.Compacted).ToString());
        propertiesTable.AddRow("Meta Attributes - Rootless", cnmt.ContentMetaAttributes.HasFlag(ContentMetaAttribute.Rebootless).ToString());
        propertiesTable.AddRow("Meta Attributes - IncludesExFatDriver", cnmt.ContentMetaAttributes.HasFlag(ContentMetaAttribute.IncludesExFatDriver).ToString());
        propertiesTable.AddRow("Minimum App Version", cnmt.MinimumApplicationVersion?.ToString() ?? "NOT SET");
        propertiesTable.AddRow("Minimum System Version", cnmt.MinimumSystemVersion + " (0x" + cnmt.MinimumSystemVersion.Version.ToString("x8") + ")");
        propertiesTable.AddRow("Patch Title Id",cnmt.PatchTitleId.ToString("x8"));
        
        const string validationFail = "[red][[X]][/]";
        const string validationPass = "[green][[V]][/]";
        
        var metaTree = new Tree("Metadata Content:")
        {
            Expanded = true,
            Guide = TreeGuide.Line
        };
        foreach (var contentFile in nspInfo.ContentFiles.Values)
        {
            var status = contentFile.IsMissing || contentFile.SizeMismatch ? validationFail : validationPass;
            var error = contentFile.IsMissing ? "<- Missing" :
                contentFile.SizeMismatch ? "<- Size Mismatch" : string.Empty;
            metaTree.AddNode($"{status} {contentFile.FileName} [[{contentFile.Type}]] {error}");
        }

        AnsiConsole.Write(new Padder(metaTree).PadLeft(1).PadTop(1).PadBottom(0));
        
        AnsiConsole.Write(new Padder(propertiesTable).PadLeft(1).PadTop(1).PadBottom(1));
        
        return 0;
    }
}