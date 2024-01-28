using System.ComponentModel;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using LibHac.Util;
using Microsoft.VisualBasic;
using Serilog;
using Serilog.Events;
using Serilog.Sinks.Spectre;
using Spectre.Console;
using Spectre.Console.Cli;

namespace Nsfw.Commands;

public class HashCommand : Command<HashSettings>
{
    public readonly int DefaultBlockSize = 0x8000;
    
    public override int Execute(CommandContext context, HashSettings settings)
    {
        var logLevel = LogEventLevel.Verbose;
        
        Log.Logger = new LoggerConfiguration()
            .MinimumLevel.Is(logLevel)
            .WriteTo.Spectre(outputTemplate: "[{Level:u3}] {Message:lj} {NewLine}{Exception}{Elapsed}")
            .CreateLogger();
        
        AnsiConsole.Write(new Rule($"[[[blue]N$FW[/]]][[{Program.GetVersion()}]]").LeftJustified());
        Log.Information($"NSP Directory : [olive]{settings.NspDirectory}[/]");
        
        var allFiles = Directory.EnumerateFiles(settings.NspDirectory, "*.nsp").ToArray();
        
        Console.WriteLine(allFiles.Length + " files found.");

        var entryCollection = new List<Entry>();
        
        //TODO: Check if file already exists + also if "Append" is set

        foreach (var file in allFiles)
        {
            var fileInfo = new FileInfo(file);
            var fileName = fileInfo.Name;
            var blockCount = (int)BitUtil.DivideUp(fileInfo.Length, DefaultBlockSize);
            
            var squareRegEx = new Regex(@"\[([0-9.].*)\]\[([0-9A-F]{16})\]\[(v[0-9].*)\]\[(.*)\]");
            var match = squareRegEx.Match(fileName);
            
            var displayVersion = match.Groups[1];
            var titleId = match.Groups[2];
            var internalVersion = match.Groups[3];
            var type = match.Groups[4];

            var titleRegEx = new Regex(@"(.*) \((.*)\)\((.*)\)");
            var titleMatch = titleRegEx.Match(fileName);
            
            var trimmedTitle = titleMatch.Groups[1];
            var region = titleMatch.Groups[2]; 
            var languages = titleMatch.Groups[3];
            
            AnsiConsole.Progress()
                .AutoClear(true) // Do not remove the task list when done
                .HideCompleted(true) // Hide tasks as they are completed
                .Columns(new SpinnerColumn(), new TaskDescriptionColumn(), new PercentageColumn(), new RemainingTimeColumn())
                .Start(ctx =>
                {
                    var hashTask = ctx.AddTask($"Hashing [[{fileName.EscapeMarkup()}]]..", new ProgressTaskSettings { MaxValue = blockCount });

                    while (!ctx.IsFinished)
                    {
                        var sha256 = SHA256.Create();
                        var sha1 = SHA1.Create();
                        var md5 = MD5.Create();
                        var crc32 = new Crc32();

                        var fs = new FileStream(file, FileMode.Open, FileAccess.Read);
                        
                        var buffer = new byte[DefaultBlockSize];
                        int read;
                        while ((read = fs.Read(buffer, 0, buffer.Length)) > 0)
                        {
                            sha256.TransformBlock(buffer, 0, read, null, 0);
                            sha1.TransformBlock(buffer, 0, read, null, 0);
                            md5.TransformBlock(buffer, 0, read, null, 0);
                            crc32.TransformBlock(buffer, 0, read, null, 0);
                            hashTask.Increment(1);
                        }

                        sha256.TransformFinalBlock(Array.Empty<byte>(), 0, 0);
                        sha1.TransformFinalBlock(Array.Empty<byte>(), 0, 0);
                        md5.TransformFinalBlock(Array.Empty<byte>(), 0, 0);
                        crc32.TransformFinalBlock(Array.Empty<byte>(), 0, 0);
                        
                        entryCollection.Add(new Entry
                        {
                            Name = fileName,
                            Sha256 = sha256.Hash.ToHexString(),
                            Sha1 = sha1.Hash.ToHexString(),
                            Md5 = md5.Hash.ToHexString(),
                            Crc32 = crc32.Hash.ToHexString(),
                            Size = fileInfo.Length,
                            TitleId = titleId.Value,
                            DisplayVersion = displayVersion.Value,
                            InternalVersion = internalVersion.Value,
                            Type = type.Value,
                            Languages = languages.Value,
                            TrimmedTitle = trimmedTitle.Value,
                            Region = region.Value
                        }); 
                    }
                });
        }
        
        var header = $"""<?xml version="1.0"?><datafile xmlns:xsi="http://www.w3.org/2001/XMLSchema-instance" xsi:schemaLocation="https://datomatic.no-intro.org/stuff https://datomatic.no-intro.org/stuff/schema_nointro_datfile_v3.xsd"><header><name>Nintendo - Nintendo Switch (Digital) (Standard)</name><description>Nintendo - Nintendo Switch (Digital) (Standard)</description><version>{DateTime.Now.ToString()}</version><author>[mRg]</author></header>""";
        var footer = "</datafile>";

        var builder = new StringBuilder();
        builder.Append(header);
        builder.AppendJoin('\n', entryCollection.Select(x => x.ToString()));
        builder.Append(footer);
        Console.WriteLine(builder.ToString());
        
        return 0;
    }
}

public class Entry
{
    public string Name { get; set; } = string.Empty;
    public string Sha256 { get; set; } = string.Empty;
    public string Sha1 { get; set; } = string.Empty;
    public string Md5 { get; set; } = string.Empty;
    public string Crc32 { get; set; } = string.Empty;
    public long Size { get; set; }
    public string TitleId { get; set; } = string.Empty;
    public string DisplayVersion { get; set; } = string.Empty;
    public string InternalVersion { get; set; } = string.Empty;
    public string Type { get; set; } = string.Empty;
    public string Languages { get; set; } = string.Empty;
    public string TrimmedTitle { get; set; } = string.Empty;
    public string Region { get; set; } = string.Empty;
    
    public string DisplayType => Type switch
    {
        "DLC" => "(DLC)",
        "UPD" => "(Update)",
        "DLCUPD" => "(DLC Update)",
        _ => ""
    };

    public override string ToString()
    {
        var internalVersion = InternalVersion == "v0" ? "" : $"({InternalVersion})";
        return $"<game name=\"{TrimmedTitle} ({Region}) ({Languages}) (v{DisplayVersion}) {internalVersion} {DisplayType}\">\n<game_id>{TitleId}</game_id>\n<version1>{InternalVersion}</version1>\n<version2>v{DisplayVersion}</version2>\n<languages>{Languages}</languages>\n<rom name=\"{Name}\" size=\"{Size}\" sha256=\"{Sha256}\" sha1=\"{Sha1}\" md5=\"{Md5}\" crc32=\"{Crc32}\"/></game>\n";
    }
}

public class HashSettings : CommandSettings
{

    [CommandOption("-i|--nspdir <DIR>")]
    [Description("Path to standardised NSP input directory.")]
    [DefaultValue("./nsp")]
    public string NspDirectory { get; set; } = string.Empty;
    
    [CommandOption("-o|--output <FILENAME>")]
    [Description("Output filename for the DAT.")]
    [DefaultValue("./nsfw-hashes.xml")]
    public string OutputFile { get; set; } = string.Empty;
    
    public override ValidationResult Validate()
    {
        if(NspDirectory.StartsWith('~'))
        {
            NspDirectory = NspDirectory.Replace("~", Environment.GetFolderPath(Environment.SpecialFolder.UserProfile));
        }
        
        NspDirectory = Path.GetFullPath(NspDirectory);
        
        var attr = File.GetAttributes(NspDirectory);
        
        if(!attr.HasFlag(FileAttributes.Directory) || !Directory.Exists(NspDirectory))
        {
            return ValidationResult.Error($"NSP directory '{NspDirectory}' does not exist.");
        }
        
        return base.Validate();
    }
}