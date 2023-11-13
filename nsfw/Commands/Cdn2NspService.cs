using System.Diagnostics.CodeAnalysis;
using LibHac.Common;
using LibHac.Common.Keys;
using LibHac.Fs;
using LibHac.Fs.Fsa;
using LibHac.FsSystem;
using LibHac.Ncm;
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
    
    public KeySet KeySet { get; }
    public string Title { get; private set; }
    public string TitleId { get; private set; }
    public string TitleVersion { get; private set; }
    public string Version { get; private set; }
    public string TitleType { get; private set; }
    public bool HasRightsId { get; private set; }
    public string CertFile { get; private set; }
    public string TicketFile { get; private set; }
    
    public Dictionary<string, long> ContentFiles { get; private set; } = new();
    
    public Cdn2NspService(Cdn2NspSettings settings)
    {
        _settings = settings;
        KeySet = ExternalKeyReader.ReadKeyFile(settings.KeysFile);
        TitleId = "UNKNOWN";
        TitleVersion = "UNKNOWN";
        TitleType = "UNKNOWN";
        Title = "UNKNOWN";
        Version = "UNKNOWN";
        CertFile = string.Empty;
        TicketFile = string.Empty;
    }
    
    public int Process(string currentDirectory, string metaNcaFileName)
    {
        var metaNcaFilePath = Path.Combine(currentDirectory, metaNcaFileName);
        
        if (_settings.Verbose)
        {
            AnsiConsole.MarkupLine($"Processing Dir  : [olive]{currentDirectory}[/]");
            AnsiConsole.MarkupLine($"Processing CNMT : [olive]{metaNcaFileName}[/]");
        }
        
        var nca = new Nca(KeySet, new LocalStorage(metaNcaFilePath, FileAccess.Read));
        
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
        
        TitleId = cnmt.TitleId.ToString("X16");
        TitleVersion = $"v{cnmt.TitleVersion.Version}";
        TitleType = cnmt.Type.ToString().ToUpperInvariant();
        
        if (_settings.Verbose)
        {
            AnsiConsole.MarkupLine($"Title ID        : [olive]{TitleId}[/]");
            AnsiConsole.MarkupLine($"Title Version   : [olive]{TitleVersion}[/]");
            AnsiConsole.MarkupLine($"Title Type      : [olive]{TitleType}[/]");
        }
        
        if (cnmt.Type != ContentMetaType.Patch && cnmt.Type != ContentMetaType.Application && cnmt.Type != ContentMetaType.Delta)
        {
            AnsiConsole.MarkupLine($"[red]Unsupported rebuild type {cnmt.Type}[/]");
            return 1;
        }
            
        foreach (var contentEntry in cnmt.ContentEntries)
        {
            var fileName = $"{contentEntry.NcaId.ToHexString().ToLower()}.nca";
            if(!File.Exists(Path.Combine(currentDirectory, fileName)) && contentEntry.Type != ContentType.DeltaFragment)
            {
                AnsiConsole.MarkupLine($"[red]Cannot find {fileName}[/]");
                return 1;
            }
            var contentNca = new Nca(KeySet, new LocalStorage(Path.Combine(currentDirectory, fileName), FileAccess.Read));
            if (contentNca.Header.HasRightsId)
            {
                HasRightsId = contentNca.Header.HasRightsId;
                TicketFile = Path.Combine(currentDirectory, $"{contentNca.Header.RightsId.ToHexString()}.tik".ToLowerInvariant());
                CertFile = Path.Combine(currentDirectory, $"{contentNca.Header.RightsId.ToHexString()}.cert".ToLowerInvariant());
            }
         
            //var sha256 = SHA256.HashData(File.ReadAllBytes(Path.Combine(currentDirectory, fileName)));
            //if(contentEntry.NcaId.ToHexString() != contentEntry.Hash.Take(16).ToArray().ToHexString() || contentEntry.NcaId.ToHexString() != sha256.Take(16).ToArray().ToHexString())
                
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
             
                Title = title.Control.Value.Title[0].NameString.ToString() ?? "UNKNOWN";
                Version = title.Control.Value.DisplayVersionString.ToString() ?? "UNKNOWN";
            }
         
            ContentFiles.Add(Path.Combine(currentDirectory, $"{contentEntry.NcaId.ToHexString().ToLower()}.nca"), contentEntry.Size);
        }
        
        if (_settings.Verbose)
        {
            AnsiConsole.MarkupLine($"Display Title   : [olive]{Title}[/]");
            AnsiConsole.MarkupLine($"Display Version : [olive]{Version}[/]");
        }
        
        ContentFiles.Add(Path.GetFullPath(metaNcaFilePath), new FileInfo(Path.Combine(metaNcaFilePath)).Length);
        
        if (HasRightsId && !string.IsNullOrEmpty(TicketFile) && File.Exists(TicketFile))
        {
            File.Copy(_settings.CertFile, CertFile, true);
            ContentFiles.Add(TicketFile, new FileInfo(TicketFile).Length);
            ContentFiles.Add(CertFile, new FileInfo(_settings.CertFile).Length);
        }
        
        var nspFilename = $"{Title} [{Version}][{TitleId}][{TitleVersion}][{TitleType}].nsp";
        if (_settings.Verbose)
        {
            var nspFilenameEsc = $"{Title} [[{Version}]][[{TitleId}]][[{TitleVersion}]][[{TitleType}]].nsp";
            var root = new Tree($"[olive]{nspFilenameEsc}[/]");
            foreach (var contentFile in ContentFiles)
            {
                root.AddNode($"{Path.GetFileName(contentFile.Key)} -> {contentFile.Value} bytes");
            }
        
            AnsiConsole.Write(root);
        }
        
        AnsiConsole.WriteLine($"Creating  : {nspFilename}");

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

        foreach (var contentFile in ContentFiles)
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
}