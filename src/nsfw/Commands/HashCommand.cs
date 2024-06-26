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

public partial class HashCommand : Command<HashSettings>
{
    public readonly int DefaultBlockSize = 0x8000;
    
    [GeneratedRegex("(v[0-9.]+)")]
    private static partial Regex VersionRegex();
    
    public override int Execute(CommandContext context, HashSettings settings)
    {
        var logLevel = LogEventLevel.Verbose;
        
        Log.Logger = new LoggerConfiguration()
            .MinimumLevel.Is(logLevel)
            .WriteTo.Spectre(outputTemplate: "[{Level:u3}] {Message:lj} {NewLine}{Exception}{Elapsed}")
            .CreateLogger();
        
        AnsiConsole.Write(new Rule($"[[[blue]N$FW[/]]][[{Program.GetVersion()}]]").LeftJustified());
        Log.Information($"NSP Directory : [olive]{settings.NspDirectory}[/]");
        
        var allFiles = Directory.EnumerateFiles(settings.NspDirectory, "*.nsp").Order().ToArray();

        var extra = string.Empty;
        
        if(settings.Overwrite)
        {
            extra += "(Overwrite) ";
        }
        
        if(settings.SkipHash)
        {
            extra += "(Skip Hash) ";
        }
        
        if(settings.DryRun)
        {
            extra += "(Dry Run) ";
        }
        
        if(settings.IsBatchMode)
        {
            extra += $"(Batch of {settings.Batch}) ";
        }
        
        var dlcXml = XDocument.Load(settings.DlcDat);
        Log.Information($"DLC Dat Loaded : [olive]{settings.DlcDat}[/]");

        // Key -> (Name, Id, TitleId)
        var dlcLookup = new Dictionary<string, (string, string, string)>();
        var dlcCount = 0;
        
        foreach (var game in dlcXml.Descendants("game"))
        {
            dlcCount++;
            var nameAttribute = game.Attribute("name");
            var name = nameAttribute != null ? nameAttribute.Value : string.Empty;
            var id = game.Attribute("id")?.Value;
            var gameId = game.Descendants("game_id").FirstOrDefault();
            var titleId = gameId != null ? gameId.Value : string.Empty;
            var version = VersionRegex().IsMatch(name) ? VersionRegex().Match(name).Value : "v0";
            var key = $"{titleId}_{version}".ToLowerInvariant();

            if (string.IsNullOrWhiteSpace(titleId))
            {
                Log.Warning($"[[[red]NO TITLE ID[/]]] => {name}");
                continue;
            }
            
            if (!string.IsNullOrWhiteSpace(name) && id != null && !string.IsNullOrWhiteSpace(titleId))
            {
                if (dlcLookup.ContainsKey(key))
                {
                    Log.Warning($"DUPE {id} + {dlcLookup[key].Item2} have same TitleId => {titleId} => {name} / {dlcLookup[key].Item1}");
                }
                else
                {
                    dlcLookup.Add(key, (name, id, titleId));
                }
            }
        }

        Log.Information($"Loaded {dlcLookup.Count}/{dlcCount} entries from DLC Dat..");
        
        Log.Information($"Hashing {allFiles.Length} NSPs {extra}..");
        
        var timeStamp = DateTime.Now.ToString("yyyyMMdd-HHmmss");
        
        var datName = settings.DatName ?? $"Nintendo - Nintendo Switch (Digital) (Standard) ({timeStamp}).xml";
        var datPath = Path.Combine(settings.OutputDirectory, datName);
        var dlcDatPath = Path.Combine(settings.OutputDirectory, $"Nintendo - Nintendo Switch (Digital) (Standard) (DLC) ({timeStamp}).xml");
        var gameDatPath = Path.Combine(settings.OutputDirectory, $"Nintendo - Nintendo Switch (Digital) (Standard) (Games + Updates) ({timeStamp}).xml");

        var alreadyHashed = new Dictionary<string, (string, string)>();
        var nameCheck = new Dictionary<string, (string, string, string, string)>();
        if (File.Exists(datPath) && !settings.Overwrite)
        {
            var xDoc = XDocument.Load(datPath);
            foreach (var rom in xDoc.Descendants("rom"))
            {
                var name = rom.Attribute("name");
                var size = rom.Attribute("size");
                var gameId = rom.Parent?.Descendants("game_id").FirstOrDefault()?.Value.ToLower();
                var internalVersion = rom.Parent?.Descendants("version1").FirstOrDefault()?.Value.ToLower();
                
                if (name != null && size != null && rom.Parent != null)
                {
                    if(!string.IsNullOrWhiteSpace(gameId) && !(gameId.EndsWith("000") || gameId.EndsWith("800")))
                    {
                        var domElement = new XElement("dom_id", "UNKNOWN");
                        
                        // Match DLC DAT so add id
                        if(dlcLookup.TryGetValue($"{gameId}_{internalVersion}".ToLowerInvariant(), out var value))
                        {
                            domElement.Value = value.Item2;
                        }
                        
                        if(rom.Parent.Descendants("dom_id").Any())
                        {
                            rom.Parent.Descendants("dom_id").Remove();
                        }
                        
                        rom.Parent.Add(domElement);
                    }

                    var parentXml = rom.Parent.ToString();
                    alreadyHashed.Add(name.Value, (size.Value, parentXml));
                    
                    if (settings.SkipHash)
                    {
                        nameCheck.Add(name.Value.Split(")").Last().Trim(), (name.Value.Split("(")[0].Trim(), size.Value, parentXml, name.Value));
                    }
                }
            }
            Log.Information($"Loaded {alreadyHashed.Count} existing entries from DAT..");
        }
        
        Log.Warning("Press [green]ESC[/] to cancel (and save current progress).");
        
        AnsiConsole.Write(new Rule());
        
        var entryCollection = new List<Entry>();

        var exitLoop = false;

        var batchCount = 0;
        
        var skipCount = 0;
        var renameCount = 0;
        var hashCount = 0;
        var errorCount = 0;

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

            if (!File.Exists(file))
            {
                Log.Error($"File not found: {file}");
                continue;
            }
            
            var fileInfo = new FileInfo(file);
            var fileName = fileInfo.Name;
            var fileNameParts = fileName.Replace(".nsp",string.Empty).Split('[');
            var isInModifiedScope = settings.Latest > 0 && fileInfo.LastWriteTime > DateTime.Now.Subtract(new TimeSpan(settings.Latest, 0,0));
            
            if(!settings.Overwrite && alreadyHashed.TryGetValue(fileName, out var value) && !isInModifiedScope)
            {
                if (fileInfo.Length == long.Parse(value.Item1))
                {
                    var element = XElement.Parse(value.Item2);
                    var description = element.Descendants("description").First().Value;
                    var isDlc = element.Descendants("category").First().Value == "DLC";
                    entryCollection.Add(new XmlEntry { Xml = value.Item2, Description = description, IsDlc = isDlc});
                    if(settings.ShowAll)
                    {
                        Log.Warning($"Skipping [olive]{fileName.EscapeMarkup()}[/]..");
                    }
                    skipCount++;
                    continue;
                }
            }
            
            if(!fileName.Contains('(') || !fileName.Contains('['))
            {
                AnsiConsole.Write(new Rule{ Style = Style.Parse("red")});
                Log.Error($"[red]Cannot parse - non-standard filename [[{fileName.EscapeMarkup()}]][/] => Skipping..");
                AnsiConsole.Write(new Rule{ Style = Style.Parse("red")});
                errorCount++;
                continue;
            }
            
            var blockCount = (int)BitUtil.DivideUp(fileInfo.Length, DefaultBlockSize);
            var size = fileInfo.Length.BytesToHumanReadable();
            var displayVersion = string.Empty;
            var offset = 0;
            
            if (fileNameParts.Length == 5) // Game or Update
            {
               displayVersion = fileNameParts[1].Replace("]", string.Empty).Trim();
            }

            if (fileNameParts.Length == 4) // DLC has no display version
            {
                offset = 1;
            }
            
            var titleId = fileNameParts[2-offset].Replace("]", string.Empty).Trim();
            var internalVersion = fileNameParts[3-offset].Replace("]", string.Empty).Trim();
            var type = fileNameParts[4-offset].Replace("]", string.Empty).Trim();
            
            var languages = "UNKNOWN";
            var isDemo = false;

            if (displayVersion.StartsWith('v') || displayVersion.StartsWith('V') || displayVersion.StartsWith('b'))
            {
                displayVersion = displayVersion[1..];
            }
            
            var titleParts = fileNameParts[0].Split('(', StringSplitOptions.TrimEntries);
            var trimmedTitle = titleParts[0];
            var region = titleParts[1].Replace(")",string.Empty);
            
            if (titleParts.Length == 2)
            {
                languages = "";
            }
            
            if (titleParts.Length > 2)
            {
                if (titleParts[2].Contains("Demo", StringComparison.InvariantCultureIgnoreCase))
                {
                    isDemo = true;
                    languages = "";
                }
                else
                {
                    languages = titleParts[2].Replace(")",string.Empty);
                }
            }
            
            if(titleParts.Length > 3)
            {
                isDemo = titleParts[3].Contains("Demo", StringComparison.InvariantCultureIgnoreCase);
            }

            if (fileNameParts.Length is < 4 or > 5)
            {
                AnsiConsole.Write(new Rule{ Style = Style.Parse("red")});
                Log.Error($"[red]Unknown filename format [[{fileName.EscapeMarkup()}]][/] => Skipping..");
                AnsiConsole.Write(new Rule{ Style = Style.Parse("red")});
                errorCount++;
                continue;
            }
            
            if (settings.SkipHash && !isInModifiedScope)
            {
                var target = fileName.Split(")").Last().Trim();

                if (nameCheck.TryGetValue(target, out var existing))
                {
                    if (long.Parse(existing.Item2) == fileInfo.Length)
                    {
                        var element = XElement.Parse(existing.Item3);
                        var romElement = element.Descendants("rom").First();
                        var sha265 = romElement.Attribute("sha256");
                        var sha1 = romElement.Attribute("sha1");
                        var md5 = romElement.Attribute("md5");
                        var crc32 = romElement.Attribute("crc");

                        if(sha265 != null && sha1 != null && md5 != null && crc32 != null)
                        {
                            entryCollection.Add(new Entry
                            {
                                File = file,
                                Name = fileName,
                                Sha256 = sha265.Value,
                                Sha1 = sha1.Value,
                                Md5 = md5.Value,
                                Crc32 = crc32.Value,
                                Size = fileInfo.Length,
                                TitleId = titleId,
                                DisplayVersion = displayVersion,
                                InternalVersion = internalVersion,
                                Type = type,
                                Languages = languages,
                                TrimmedTitle = trimmedTitle,
                                Region = region,
                                IsDemo = isDemo,
                            });
                            
                            Log.Warning($"Renaming [olive]{existing.Item4.Replace(".nsp",string.Empty).EscapeMarkup()}[/] => [blue]{fileName.Replace(".nsp",string.Empty).EscapeMarkup()}[/]..");
                            renameCount++;
                            continue;
                        }
                    }

                    Log.Information("Wrong size .. re-hashing");
                }
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
                            TitleId = titleId,
                            DisplayVersion = displayVersion,
                            InternalVersion = internalVersion,
                            Type = type,
                            Languages = languages,
                            TrimmedTitle = trimmedTitle,
                            Region = region,
                            IsDemo = isDemo
                        });
                    }
                    
                    Log.Information($"[green]{fileName.EscapeMarkup()}[/]");
                    hashCount++;
                    batchCount++;
                });
        }
        
        AnsiConsole.Write(new Rule());
        
        AnsiConsole.WriteLine($"Total: {entryCollection.Count}");
        AnsiConsole.MarkupLine("Skip: [yellow]{0}[/]", skipCount);
        AnsiConsole.MarkupLine("Rename: [blue]{0}[/]", renameCount);
        AnsiConsole.MarkupLine("Hash: [green]{0}[/]", hashCount);
        AnsiConsole.MarkupLine("Error: [red]{0}[/]", errorCount);
        
        AnsiConsole.Write(new Rule());

        if (settings.DryRun)
        {
            Log.Information($"[[[green]DRYRUN[/]]] -> Would have written {entryCollection.Count} entries to DAT..");
            return 0;
        }
        
        var header     = $"""<?xml version="1.0"?><datafile xmlns:xsi="http://www.w3.org/2001/XMLSchema-instance" xsi:schemaLocation="https://datomatic.no-intro.org/stuff https://datomatic.no-intro.org/stuff/schema_nointro_datfile_v3.xsd"><header><name>Nintendo - Nintendo Switch (Digital) (Standard)</name><description>Nintendo - Nintendo Switch (Digital) (Standard)</description><version>{timeStamp}</version><author>[mRg]</author><romvault forcepacking="unzip" /></header>""";
        var headerDlc  = $"""<?xml version="1.0"?><datafile xmlns:xsi="http://www.w3.org/2001/XMLSchema-instance" xsi:schemaLocation="https://datomatic.no-intro.org/stuff https://datomatic.no-intro.org/stuff/schema_nointro_datfile_v3.xsd"><header><name>Nintendo - Nintendo Switch (Digital) (Standard) (DLC)</name><description>Nintendo - Nintendo Switch (Digital) (Standard) (DLC)</description><version>{timeStamp}</version><author>[mRg]</author><romvault forcepacking="unzip" /></header>""";
        var headerGame = $"""<?xml version="1.0"?><datafile xmlns:xsi="http://www.w3.org/2001/XMLSchema-instance" xsi:schemaLocation="https://datomatic.no-intro.org/stuff https://datomatic.no-intro.org/stuff/schema_nointro_datfile_v3.xsd"><header><name>Nintendo - Nintendo Switch (Digital) (Standard) (Games + Updates)</name><description>Nintendo - Nintendo Switch (Digital) (Standard) (Games + Updates)</description><version>{timeStamp}</version><author>[mRg]</author><romvault forcepacking="unzip" /></header>""";
        var footer = "</datafile>";

        var builder = new StringBuilder();
        var dlcBuilder = new StringBuilder();
        var gameBuilder = new StringBuilder();
        builder.AppendLine(header);
        dlcBuilder.AppendLine(headerDlc);
        gameBuilder.AppendLine(headerGame);
        
        var last = string.Empty; 
        var oneGameOneUpdateBase = new Dictionary<string, string>();
        var oneGameOneUpdateUpdate = new Dictionary<string, Dictionary<int, string>>();
        foreach (var entry in entryCollection.OrderBy(x => x.Description, StringComparer.InvariantCultureIgnoreCase))
        {
            var isDupe = false;
            
            if (last.StartsWith(entry.Description.ToLowerInvariant(), StringComparison.InvariantCultureIgnoreCase))
            {
                isDupe = true;
                
                if(last.Contains("(alt)") && !entry.IsAlt)
                {
                    isDupe = false;
                }
                
                if (last.Contains("(demo)") && !entry.IsDemo)
                {
                    isDupe = false;    
                }

                // Allowed duplicate (name the same but different titleId)
                if (last.Length == entry.Description.Length + 19)
                {
                    isDupe = false;
                }
            }

            if (isDupe)
            {
                if (entry.GetType() == typeof(XmlEntry))
                {
                    Log.Warning($"Skipping XML duplicate entry: {entry.Description}. Please re-scan.");
                    continue;
                }
                
                Log.Information($"Duplicate description: {entry.Description}");
                
                entry.Description += $" ({entry.TitleId.ToUpperInvariant()})";  
            }

            if (entry.GetType() == typeof(XmlEntry))
            {
                if (((XmlEntry)entry).IsDlc)
                {
                    dlcBuilder.AppendLine(entry.ToString());
                }
                else
                {
                    gameBuilder.AppendLine(entry.ToString());
                }
            }
            
            if (settings.OneGameOneUpdate && entry.GetType() == typeof(XmlEntry))
            {
                var entryString = entry.ToString();
                var parsedEntry = XElement.Parse(entryString);
                var titleId = parsedEntry.Descendants("game_id").First().Value.ToLower();
                var internalVersion = parsedEntry.Descendants("version1").First().Value.Replace("v", string.Empty);
                var category = parsedEntry.Descendants("category").First().Value.ToLower();
                var altElement = parsedEntry.Descendants("isAlt").FirstOrDefault();
                var isAlt = bool.Parse(altElement == null ? "false" : altElement.Value.ToLower());

                if (!isAlt)
                {
                    switch (category)
                    {
                        case "game":
                            oneGameOneUpdateBase.TryAdd(titleId, entryString);
                            break;
                        case "update":
                            try
                            {
                                titleId = titleId[..^3] + "000";

                                if (oneGameOneUpdateUpdate.TryGetValue(titleId, out var value))
                                {
                                    var intVersion = int.Parse(internalVersion);
                                    if (value.TryAdd(intVersion, entryString))
                                    {
                                        //Console.WriteLine($"ADD EXTRA UPDATE => {titleId} / {intVersion}");
                                    }
                                }
                                else
                                {
                                    oneGameOneUpdateUpdate.Add(titleId, new Dictionary<int, string> { { int.Parse(internalVersion), entryString } });
                                }
                            }
                            catch (Exception e)
                            {
                                Log.Error($"{titleId}/{internalVersion}/{e.Message}");
                            }

                            break;
                    }
                }
            }
            
            builder.AppendLine(entry.ToString());
            last = entry.Description;
        }
        builder.Append(footer);
        dlcBuilder.Append(footer);
        gameBuilder.Append(footer);
        File.WriteAllText(datPath, builder.ToString());
        File.WriteAllText(dlcDatPath, dlcBuilder.ToString());
        File.WriteAllText(gameDatPath, gameBuilder.ToString());
        Log.Information($"{entryCollection.Count} entries written to DAT successfully! ({datPath})");
        AnsiConsole.Write(new Rule());
        
        if (settings.OneGameOneUpdate)
        {
            Log.Information("Generating OneGameOneUpdate DAT..");
            var oneCount = 0;
            var oneBuilder = new StringBuilder();
            var oneGameOneUpdateHeader = $"""<?xml version="1.0"?><datafile xmlns:xsi="http://www.w3.org/2001/XMLSchema-instance" xsi:schemaLocation="https://datomatic.no-intro.org/stuff https://datomatic.no-intro.org/stuff/schema_nointro_datfile_v3.xsd"><header><name>Nintendo - Nintendo Switch (Digital) (Standard) (1G1U)</name><description>Nintendo - Nintendo Switch (Digital) (Standard) (1G1U)</description><version>{DateTime.Now.ToString("yyyyMMdd-HHmmss")}</version><author>[mRg]</author><romvault forcepacking="unzip" /></header>""";
            oneBuilder.AppendLine(oneGameOneUpdateHeader);
            
            // Get latest update for each BASE game
            foreach (var game in oneGameOneUpdateBase)
            {
                oneBuilder.AppendLine(game.Value);
                oneCount++;

                if (oneGameOneUpdateUpdate.TryGetValue(game.Key, out var value))
                {
                    var latestUpdate = value.MaxBy(x => x.Key);
                    oneBuilder.AppendLine(latestUpdate.Value);
                    oneCount++;
                }
            }

            // Check for updates that do not have a BASE game
            foreach (var updateOnly in oneGameOneUpdateUpdate)
            {
                if(!oneGameOneUpdateBase.ContainsKey(updateOnly.Key))
                {
                    var latestUpdate = updateOnly.Value.MaxBy(x => x.Key);
                    oneBuilder.AppendLine(latestUpdate.Value);
                    oneCount++;
                }
            }
            
            oneBuilder.Append(footer);
            File.WriteAllText(Path.Combine(settings.OutputDirectory, $"Nintendo - Nintendo Switch (Digital) (Standard) (1G1U) ({timeStamp}).xml"), oneBuilder.ToString());
            Log.Information($"{oneCount} entries written to 1G1U DAT successfully!");
            AnsiConsole.Write(new Rule());
        }
        
        return 0;
    }
}

public class XmlEntry : Entry
{
    public string Xml { get; set; } = string.Empty;
    
    public bool IsDlc { get; set; }

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
    public bool IsAlt => DisplayVersion.EndsWith("-ALT");

    private string _description = string.Empty;
    
    public string Description
    {
        get
        {
            if(string.IsNullOrWhiteSpace(_description))
            {
                var internalVersion = InternalVersion == "v0" ? "" : $" ({InternalVersion})";
                var demo = IsDemo ? " (Demo)" : "";
                var alt = IsAlt ? $" (Alt)" : "";
                var outputLanguages = string.IsNullOrWhiteSpace(Languages) ? "" : $" ({Languages})";
                var displayVersion = string.IsNullOrWhiteSpace(DisplayVersion) ? "" : $" (v{DisplayVersion})";

                if (IsAlt)
                {
                    displayVersion = displayVersion.Replace("-ALT", "");
                }
                
                _description = $"{TrimmedTitle} ({Region}){outputLanguages}{displayVersion}{internalVersion}{demo}{DisplayType}{alt}";
            }
            
            return _description;
        }
        
        set => _description = value;
    }

    public override string ToString()
    {
        if (string.IsNullOrWhiteSpace(TitleId))
        {
            throw new Exception("TitleId cannot be empty.");
        }
        
        var regionLanguages = Languages;
        if (string.IsNullOrWhiteSpace(regionLanguages))
        {
            regionLanguages = Region.ToUpperInvariant() switch {
                "KOREA" => "Ko",
                "TAIWAN" => "Zh-Hant",
                "CHINA" => "Zh-Hans",
                "JAPAN" => "Jp",
                "USA" => "En",
                "FRANCE" => "Fr",
                "CANADA" => "Fr-CA",
                "UNITED KINGDOM" => "En-GB",
                "NETHERLANDS" => "Nl",
                "SPAIN" => "Es",
                "ITALY" => "It",
                "GERMANY" => "De",
                "PORTUGAL" => "Pt",
                "BRAZIL" => "Pt-BR",
                "LATIN AMERICA" => "Es-XL",
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

        if (IsDemo)
        {
            category = Type == "UPD" ? "Demo Update" : "Demo";
        }

        var name = Description.Replace("&", "&amp;").Replace("'", "&apos;");
        
        return $"<game name=\"{name}\">\n  <description>{name}</description>\n  <game_id>{TitleId}</game_id>\n  <version1>{InternalVersion}</version1>\n  <version2>{(!string.IsNullOrWhiteSpace(DisplayVersion)?$"v{DisplayVersion.Replace("-ALT",string.Empty)}":"")}</version2>\n  <languages>{regionLanguages}</languages>\n  <isDemo>{IsDemo.ToString().ToLower()}</isDemo>\n  <isAlt>{IsAlt.ToString().ToLower()}</isAlt>\n  <category>{category}</category>\n  <rom name=\"{Name.Replace("&","&amp;").Replace("'","&apos;")}\" size=\"{Size}\" sha256=\"{Sha256}\" sha1=\"{Sha1}\" md5=\"{Md5}\" crc=\"{Crc32}\"/>\n</game>";
    }
}