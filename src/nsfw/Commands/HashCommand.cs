using System.Runtime.InteropServices.ComTypes;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using LibHac.Util;
using Nsfw.Nsp;
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

        var extra = string.Empty;
        
        if(settings.Overwrite)
        {
            extra += "(Overwrite) ";
        }
        
        if(settings.IsBatchMode)
        {
            extra += $"(Batch of {settings.Batch}) ";
        }
        
        Log.Information($"Hashing {allFiles.Length} NSPs {extra}..");
        
        var datName = settings.DatName ?? $"Nintendo - Nintendo Switch (Digital) (Standard) ({DateTime.Now.ToString("yyyyMMdd-HHmmss")}).xml";
        var datPath = Path.Combine(settings.OutputDirectory, datName);

        var alreadyHashed = new Dictionary<string, string>();
        if (File.Exists(datPath) && !settings.Overwrite)
        {
            var xDoc = XDocument.Load(datPath);
            foreach (var rom in xDoc.Descendants("rom"))
            {
                var name = rom.Attribute("name");
                if (name != null && rom.Parent != null)
                {
                    alreadyHashed.Add(name.Value, rom.Parent.ToString()); 
                }
            }
            Log.Information($"Loaded {alreadyHashed.Count} existing entries from DAT..");
        }
        
        Log.Warning("Press [green]ESC[/] to cancel (and save current progress).");
        
        AnsiConsole.Write(new Rule());
        
        var entryCollection = new List<Entry>();

        var exitLoop = false;

        var batchCount = 0;

        foreach (var file in allFiles)
        {
            if (Console.KeyAvailable)
            {
                var key = Console.ReadKey(true);
                switch (key.Key)
                {
                    case ConsoleKey.Escape:
                        Log.Warning("Exiting..");
                        exitLoop = true;
                        break;
                }
            }
            
            if(batchCount >= settings.Batch && settings.IsBatchMode)
            {
                Log.Warning($"Batch mode enabled. Stopping after hashing {settings.Batch} files.");
                break;
            }
            
            if(exitLoop)
            {
                break;
            }
            
            var fileInfo = new FileInfo(file);
            var fileName = fileInfo.Name;
            var fileNameParts = fileName.Split('[');

            if(!settings.Overwrite && alreadyHashed.TryGetValue(fileName, out var value))
            {
                entryCollection.Add(new XmlEntry { Xml = value });
                Log.Warning($"Skipping [olive]{fileName.EscapeMarkup()}[/]..");
                continue;
            }
            
            var blockCount = (int)BitUtil.DivideUp(fileInfo.Length, DefaultBlockSize);
            var size = fileInfo.Length.BytesToHumanReadable();
            
            var squareRegEx = new Regex(@"\[([0-9.].*)\]\[([0-9A-F]{16})\]\[(v[0-9].*)\]\[(.*)\]");
            var match = squareRegEx.Match(fileName);
            
            var displayVersion = match.Groups[1];
            var titleId = match.Groups[2];
            var internalVersion = match.Groups[3];
            var type = match.Groups[4];
            var languages = "UNKNOWN";
            var isDemo = false;
            
            var titleParts = fileNameParts[0].Split('(', StringSplitOptions.TrimEntries);
            var trimmedTitle = titleParts[0];
            var region = titleParts[1].Replace(")",string.Empty);
            
            if (titleParts.Length == 2)
            {
                languages = "";
            }
            
            if (titleParts.Length > 2)
            {
                languages = titleParts[2].Replace(")",string.Empty);
            }
            
            if(titleParts.Length > 3)
            {
                isDemo = titleParts[3].Contains("Demo", StringComparison.InvariantCultureIgnoreCase);
            }
            
            AnsiConsole.Progress()
                .AutoClear(true) // Do not remove the task list when done
                .HideCompleted(true) // Hide tasks as they are completed
                .Columns(new SpinnerColumn { Spinner = Spinner.Known.Ascii }, new TaskDescriptionColumn(), new ProgressBarColumn(), new RemainingTimeColumn())
                .Start(ctx =>
                {
                    var hashTask = ctx.AddTask($"{fileName.EscapeMarkup()} ({size})", new ProgressTaskSettings { MaxValue = blockCount });

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
                            File = file,
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
                            Languages = languages,
                            TrimmedTitle = trimmedTitle,
                            Region = region,
                            IsDemo = isDemo
                        });
                    }
                    
                    Log.Information($"[green]{fileName.EscapeMarkup()}[/]");
                    batchCount++;
                });
        }
        AnsiConsole.Write(new Rule());
        
        var header = $"""<?xml version="1.0"?><datafile xmlns:xsi="http://www.w3.org/2001/XMLSchema-instance" xsi:schemaLocation="https://datomatic.no-intro.org/stuff https://datomatic.no-intro.org/stuff/schema_nointro_datfile_v3.xsd"><header><name>Nintendo - Nintendo Switch (Digital) (Standard)</name><description>Nintendo - Nintendo Switch (Digital) (Standard)</description><version>{DateTime.Now.ToString("yyyyMMdd-HHmmss")}</version><author>[mRg]</author></header>""";
        var footer = "</datafile>";

        var builder = new StringBuilder();
        builder.AppendLine(header);
        builder.AppendJoin('\n', entryCollection.Select(x => x.ToString()));
        builder.Append(footer);
        File.WriteAllText(datPath, builder.ToString());
        Log.Information($"{entryCollection.Count} entries written to DAT successfully! ({datPath})");
        AnsiConsole.Write(new Rule());
        return 0;
    }
}

public class XmlEntry : Entry
{
    public string Xml { get; set; } = string.Empty;

    public override string ToString()
    {
        return Xml;
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
        "DLC" => " (DLC)",
        "UPD" => " (Update)",
        "DLCUPD" => " (DLC Update)",
        _ => ""
    };
    public string File { get; set; } = string.Empty;
    public bool IsDemo { get; set; }

    public override string ToString()
    {
        var internalVersion = InternalVersion == "v0" ? "" : $" ({InternalVersion})";
        var demo = IsDemo ? " (Demo)" : "";

        var regionLanguages = Languages;
        if (string.IsNullOrWhiteSpace(regionLanguages))
        {
           regionLanguages = Region.ToUpperInvariant() switch {
                "KOREA" => "Ko",
                "TAIWAN" => "Zh-Hant",
                "CHINA" => "Zh-Hans",
                "JAPAN" => "Jp",
                "USA" => "En",
                "FRENCH" => "Fr",
                "CANADA" => "Fr-CA",
                "UNITED KINGDOM" => "En-GB",
                "NETHERLANDS" => "Nl",
                "SPAIN" => "Es",
                "ITALY" => "It",
                "GERMANY" => "De",
                "PORTUGAL" => "Pt",
                "BRAZIL" => "Pt-BR",
                "RUSSIA" => "Ru",
                _ => ""
            };
        }
        
        var category = Type switch
        {
            "DLC" => "DLC",
            "UPD" => "Update",
            "DLCUPD" => "DLC Update",
            _ => "Game"
        };

        var outputLanguages = string.IsNullOrWhiteSpace(Languages) ? " " : $" ({Languages}) ";

        var name = $"{TrimmedTitle.Replace("&","&amp;")} ({Region}){outputLanguages}(v{DisplayVersion}){internalVersion}{demo}{DisplayType}";
        
        return $"<game name=\"{name}\">\n<description>{name}</description>\n<game_id>{TitleId}</game_id>\n<version1>{InternalVersion}</version1>\n<version2>v{DisplayVersion}</version2>\n<languages>{regionLanguages}</languages>\n<isDemo>{IsDemo.ToString().ToLower()}</isDemo>\n<category>{category}</category>\n<rom name=\"{Name.Replace("&","&amp;")}\" size=\"{Size}\" sha256=\"{Sha256}\" sha1=\"{Sha1}\" md5=\"{Md5}\" crc=\"{Crc32}\"/></game>\n";
    }
}