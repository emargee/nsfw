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
using Serilog;
using Serilog.Events;
using Serilog.Sinks.Spectre;
using Spectre.Console;
using ContentType = LibHac.Ncm.ContentType;
using Path = System.IO.Path;

namespace Nsfw.Commands;

public class Cdn2NspService
{
    private readonly Cdn2NspSettings _settings;
    private readonly KeySet _keySet;
    private string _titleId;
    private bool _hasRightsId;
    private string _certFile;
    private string _ticketFile;
    private readonly Dictionary<string, long> _contentFiles = new();
    private readonly byte[] _titleKeyEnc;

    public Cdn2NspService(Cdn2NspSettings settings)
    {
        _settings = settings;
        _keySet = ExternalKeyReader.ReadKeyFile(settings.KeysFile);
        _titleId = "UNKNOWN";
        _certFile = string.Empty;
        _ticketFile = string.Empty;
        _titleKeyEnc = Array.Empty<byte>();
    }
    
    public int Process(string currentDirectory, string metaNcaFileName)
    {
        var logLevel = LogEventLevel.Information;
        
        Log.Logger = new LoggerConfiguration()
            .MinimumLevel.Is(logLevel)
            .WriteTo.Spectre(outputTemplate: "[{Level:u3}] {Message:lj} {NewLine}{Exception}{Elapsed}")
            .CreateLogger();
        
        var metaNcaFilePath = Path.Combine(currentDirectory, metaNcaFileName);
        
        if(!File.Exists(metaNcaFilePath))
        {
            Log.Fatal($"[red]Cannot find {metaNcaFileName}[/]");
            return 1;
        }
        
        Log.Information($"Processing Dir  : [olive]{new DirectoryInfo(currentDirectory).Name.EscapeMarkup()}[/]");
        Log.Information($"Processing CNMT : [olive]{metaNcaFileName}[/]");

        var metaFile = new LocalStorage(metaNcaFilePath, FileAccess.Read);
        var nca = new Nca(_keySet, metaFile);
        
        if(!nca.CanOpenSection(0))
        {
            Log.Fatal("[red]Cannot open section 0[/]");
            return 1;
        }
        
        var fs = nca.OpenFileSystem(NcaSectionType.Data, IntegrityCheckLevel.ErrorOnInvalid);
        var fsCnmtPath = fs.EnumerateEntries("/", "*.cnmt").Single().FullPath;
        
        using var file = new UniqueRef<IFile>();
        fs.OpenFile(ref file.Ref, fsCnmtPath.ToU8Span(), OpenMode.Read).ThrowIfFailure();
        
        var cnmt = new Cnmt(file.Release().AsStream());
        
        _titleId = cnmt.TitleId.ToString("X16");
        
        if (cnmt.Type != ContentMetaType.Patch && cnmt.Type != ContentMetaType.Application && cnmt.Type != ContentMetaType.Delta && cnmt.Type != ContentMetaType.AddOnContent)
        {
            Log.Error($"[red]Unsupported rebuild type {cnmt.Type}[/]");
            return 1;
        }
            
        foreach (var contentEntry in cnmt.ContentEntries)
        {
            if(contentEntry.Type == ContentType.DeltaFragment)
            {
                continue;
            }
            
            var fileName = $"{contentEntry.NcaId.ToHexString().ToLower()}.nca";
            if(!File.Exists(Path.Combine(currentDirectory, fileName)))
            {
                Log.Error($"[red]Cannot find {fileName}[/]");
                return 1;
            }

            var localFile = new LocalStorage(Path.Combine(currentDirectory, fileName), FileAccess.Read);
            var contentNca = new Nca(_keySet, localFile);
            if (contentNca.Header.HasRightsId && _titleKeyEnc.Length == 0)
            {
                _hasRightsId = contentNca.Header.HasRightsId;
                _ticketFile = Path.Combine(currentDirectory, $"{contentNca.Header.RightsId.ToHexString()}.tik".ToLowerInvariant());
                _certFile = Path.Combine(currentDirectory, $"{contentNca.Header.RightsId.ToHexString()}.cert".ToLowerInvariant());

                if (!File.Exists(_ticketFile))
                {
                    Log.Error($"[red]Cannot find ticket file - {_ticketFile}[/]");
                    return 1;
                }
            }
            _contentFiles.Add(Path.Combine(currentDirectory, $"{contentEntry.NcaId.ToHexString().ToLower()}.nca"), contentEntry.Size);
            localFile.Dispose();
        }
        
        _contentFiles.Add(Path.GetFullPath(metaNcaFilePath), new FileInfo(Path.Combine(metaNcaFilePath)).Length);
        
        if (_hasRightsId && !string.IsNullOrEmpty(_ticketFile) && File.Exists(_ticketFile))
        {
            _contentFiles.Add(_ticketFile, new FileInfo(_ticketFile).Length);
            File.Copy(_settings.CertFile, _certFile, true);
            _contentFiles.Add(_certFile, new FileInfo(_settings.CertFile).Length);
        }

        metaFile.Dispose();
        file.Destroy();
        fs.Dispose();
        
        var nspFilename = $"{_titleId}.nsp";
        
        if (_settings.DryRun)
        {
            AnsiConsole.WriteLine(" -> Dry Run. Skipping creation...");
            return 0;    
        }
        
        var tempFilePath = Path.Combine(_settings.OutDirectory, nspFilename);
        
        AnsiConsole.Status()
            .Start("Creating temp NSP...", ctx =>
            {
                ctx.Spinner(Spinner.Known.Ascii);
                var builder = new PartitionFileSystemBuilder();

                var referenceCollection = new List<LocalFile>();
                
                foreach (var contentFile in _contentFiles)
                {
                    var localFile = new LocalFile(contentFile.Key, OpenMode.Read);
                    referenceCollection.Add(localFile);
                    builder.AddFile(Path.GetFileName(contentFile.Key), localFile);
                }
                
                using var outStream = new FileStream(tempFilePath, FileMode.Create, FileAccess.ReadWrite);
                var builtPfs = builder.Build(PartitionFileSystemType.Standard);
                builtPfs.GetSize(out var pfsSize).ThrowIfFailure();
                builtPfs.CopyToStream(outStream, pfsSize);
                
                outStream.Close();
                foreach (var localFile in referenceCollection)
                {
                    localFile.Dispose();
                }
                builtPfs.Dispose();
            });

        // Perform default validation / conversion
        
        var settings = new ValidateNspSettings
        {
            KeysFile = _settings.KeysFile,
            CertFile = _settings.CertFile,
            Convert = true,
            ForceConvert = true,
            IsQuiet = true,
            VerifyTitle = true,
            TitleDbFile = "./titledb/titledb.db",
            NspDirectory = _settings.OutDirectory,
            ShortLanguages = true,
            DryRun = _settings.DryRun
        };

        var validateNspService = new ValidateNspService(settings);
        var result = validateNspService.Process(tempFilePath, false, true);
        if (result.returnValue == 0)
        {
            File.Delete(tempFilePath);
        }
        
        return result.returnValue;
    }
}