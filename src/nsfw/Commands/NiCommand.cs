using System.Diagnostics;
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using Serilog;
using Serilog.Events;
using Serilog.Sinks.Spectre;
using Spectre.Console;
using Spectre.Console.Cli;

namespace Nsfw.Commands;

public class FileEntry
{
    public string TitleId { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Version { get; set; } = string.Empty;
    public string FullName { get; set; } = string.Empty;
    public string Type { get; set; } = string.Empty;
    // ReSharper disable once InconsistentNaming
    public bool IsDLC { get; set; }
}

public class DatEntry
{
    public string Name { get; set; } = string.Empty;
    public string TitleId { get; set; } = string.Empty;
    public string Version { get; set; } = string.Empty;
    public string Type { get; set; } = string.Empty;
    public string Xml { get; set; } = string.Empty;
    public bool CdnFixable => Xml.Contains(".tik");
    public string? Id { get; set; }
    public string? Sha1 { get; set; }
    // ReSharper disable once InconsistentNaming
    public bool IsDLC { get; set; }
    public bool IsMia { get; set; }
}

public partial class NiCommand : Command<NiSettings>
{
    public override int Execute(CommandContext context, NiSettings settings)
    {
        AnsiConsole.Write(new Rule($"[[[blue]N$FW[/]]][[{Program.GetVersion()}]]").LeftJustified());
        
        var logLevel = LogEventLevel.Information;
        
        Log.Logger = new LoggerConfiguration()
            .MinimumLevel.Is(logLevel)
            .WriteTo.Spectre(outputTemplate: "[{Level:u3}] {Message:lj} {NewLine}{Exception}{Elapsed}")
            .CreateLogger();
        
        var xml1 = XDocument.Load(settings.NspDat);
        var xml2 = XDocument.Load(settings.CdnDat);
        var xml3 = XDocument.Load(settings.DlcDat);
        
        Log.Information($"NSP Dat Loaded : [olive]{settings.NspDat}[/]");
        Log.Information($"CDN Dat Loaded : [olive]{settings.CdnDat}[/]");
        Log.Information($"DLC Dat Loaded : [olive]{settings.DlcDat}[/]");
        Log.Information($"Scan Directory : [olive]{settings.ScanDir}[/]");
        if (settings.Reverse)
        {
            Log.Information($"Scan Mode      : [olive]Reverse[/] (Files on disk but not in DATs)");
        }
        else
        {
            Log.Information($"Scan Mode      : [olive]Normal[/] (Files in DATs but not on disk)");
        }
        AnsiConsole.Write(new Rule());

        var files = new Dictionary<string, FileEntry>(comparer: StringComparer.InvariantCultureIgnoreCase);
        
        var fileEntries = Directory.EnumerateFiles(settings.ScanDir, "*.nsp", new EnumerationOptions{ MatchCasing = MatchCasing.CaseInsensitive })
        .Select(x =>
        {
            var parts = x.Split('[');
            var offset = parts.Length > 4 ? 2 : 1;
            var filename = Path.GetFileName(x);
            
            if(!filename.Contains('[') && !filename.Contains('('))
            {
                Log.Error("Unable to process filename for comparison: [red]{filename}[/]");
                throw new InvalidOperationException($"Unable to process filename ({filename}) for comparison");
            }
            
            return new FileEntry
            {
                TitleId = parts[offset].TrimEnd(']').Trim(),
                Version = parts[offset+1].TrimEnd(']').Trim(),
                FullName = Path.GetFileName(x),
                Name = filename.Contains('(') ? filename.Split('(')[0].Trim() : filename.Split('[')[0].Trim(),
                Type = x.Contains("[BASE]") ? "GAME" : x.Contains("[UPD]") ? "UPD" : x.Contains("[DLC]") ? "DLC" : x.Contains("[DLCUPD]") ? "DLCUPD" : "UNKNOWN",
                IsDLC = x.Contains("[DLC]") || x.Contains("[DLCUPD]")
            };
        });

        var versionMap = new Dictionary<string, string[]>(); // TitleId -> [Version]
        var hasDuplicates = false;
        
        foreach (var fileEntry in fileEntries)
        {
            var key = $"{fileEntry.TitleId.ToLowerInvariant()}_{fileEntry.Version.ToLowerInvariant()}_{fileEntry.IsDLC}";
            if(!files.TryAdd(key, fileEntry))
            {
                Log.Warning($"Duplicate file found: [green]{fileEntry.FullName.EscapeMarkup()}[/] => {files[key].FullName.EscapeMarkup()}");
                hasDuplicates = true;
            }

            var title = fileEntry.TitleId.ToLowerInvariant();
            var internalVersion = fileEntry.Version;
            
            if (versionMap.TryGetValue(title, out var versions))
            {
                if (!versions.Contains(internalVersion))
                {
                    versionMap[title] = versions.Append(internalVersion).ToArray();
                }
            }
            else
            {
                versionMap.Add(title, new[] { internalVersion });
            }
        }

        if (hasDuplicates)
        {
            return 1;
        }

        // Find games without game_id
        // var test = xml1
        //     .Descendants("game")
        //     .Where(x => !x.Descendants("game_id").Any());
        //
        // foreach (var element in test)
        // {
        //     var name = element.Attribute("name").Value;
        //     Console.WriteLine(name);
        // }
        
        //Combine and keep duplicates
        var sortedSet = xml1
            .Descendants("game")
            .Concat(xml2.Descendants("game"))
            .Concat(xml3.Descendants("game"))
            .Descendants("game_id")
            .Select(x =>
            {
                Debug.Assert(x.Parent != null, "x.Parent != null");
                
                var name = x.Parent.Attribute("name")?.Value;
                var romName = x.Parent?.Descendants("rom").First().Attribute("name")?.Value;
                var isNsp = romName != null && romName.EndsWith(".nsp");
                
                if (name != null)
                {
                    return new DatEntry
                    {
                        Name = name,
                        TitleId = x.Value,
                        Version = VersionRegex().IsMatch(name) ? VersionRegex().Match(name).Value : "v0",
                        Type = isNsp ? "NSP" : "CDN",
                        Xml = x.Parent?.ToString() ?? string.Empty,
                        Id = x.Parent?.Attribute("id")?.Value,
                        Sha1 = isNsp ? x.Parent?.Descendants("rom").First().Attribute("sha1")?.Value : null,
                        IsDLC = name.Contains("(DLC") || name.Contains("DLC)") || name.Contains("Update, DLC"),
                        IsMia = isNsp && x.Parent?.Descendants("rom").First().Attribute("mia")?.Value == "yes"
                    };
                }

                return new DatEntry();
            })
            .DistinctBy(x => x.Name.ToUpperInvariant())
            .OrderBy(x => x.Name, StringComparer.InvariantCultureIgnoreCase);
        
        if (!settings.Reverse)
        {
            int correctCount = 0;
            int nameErrorCount = 0;
            HashSet<DatEntry> missing = [];
            int missingBuffer = 0;
            int miaCount = 0;
            int noTikCount = 0;
            int badCount = 0;
            
            var searchList = sortedSet.AsEnumerable();
            
            var duplicates = searchList.Duplicates();
            if (settings.ShowDuplicates)
            {
                foreach (var duplicate in duplicates)
                {
                    if (duplicate.Value.Sha1 != null && duplicate.Value.Sha1.Equals(duplicate.Key.Sha1, StringComparison.InvariantCultureIgnoreCase))
                    {
                        Log.Warning($"[[[green]DUPE EXACT[/]]]  {duplicate.Key.TitleId.ToUpperInvariant()} -> [green]{duplicate.Value.Name.EscapeMarkup()} -> {duplicate.Key.Name}[/]");
                    }
                    else
                    {
                        Log.Warning($"[[[red]DUPE[/]]] {duplicate.Key.TitleId.ToUpperInvariant()} -> [red]{duplicate.Value.Name.EscapeMarkup()} -> {duplicate.Key.Name}[/]");
                    }
                }
                AnsiConsole.Write(new Rule());
            }

            var duplicateList = new Dictionary<string, string>(StringComparer.InvariantCulture);

            foreach (var duplicate in duplicates)
            {
                duplicateList.Add(duplicate.Value.Name, duplicate.Key.Name); // Key = first instance, Value = second instance (duplicate) - go with first
            }

            if(settings.ByLetter != null)
            {
                searchList = searchList.Where(x => x.Name.StartsWith(settings.ByLetter, StringComparison.InvariantCultureIgnoreCase));
            }

            foreach (var game in searchList)
            {
                if (game.IsDLC && settings.ExcludeDlc)
                {
                    continue;
                }
                
                Debug.Assert(game.Name != null, "game.Name != null");

                if (game.Name.Contains("(Homebrew)"))
                {
                    // Skip homebrew
                    continue;
                }

                if (game.Type == "CDN" && game.Name.Contains("(Alt)"))
                {
                    // Skip alts
                    continue;
                }
                
                var version = "v0";

                if (game.Name.Contains("(v"))
                {
                    version = VersionRegex().Match(game.Name).Value;
                }

                if (version.Contains('.'))
                {
                    AnsiConsole.Write(new Rule());
                    Log.Fatal($"[red]!!ENTRY ERROR: Missing valid version -> {game.TitleId} -> {game.Name} -> {version}[/]");
                    AnsiConsole.Write(new Rule());
                    continue;
                }

                if (game.TitleId.Length != 16)
                {
                    AnsiConsole.Write(new Rule());
                    Log.Fatal($"[red]!!DAT ERROR: Invalid Title ID Length -> {game.TitleId} -> {game.Name}[/]");
                    AnsiConsole.Write(new Rule());
                    continue;
                }
                
                if (!long.TryParse(game.TitleId, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out _))
                {
                    AnsiConsole.Write(new Rule());
                    Log.Fatal($"[red]!!DAT ERROR: Invalid Title ID -> {game.TitleId} -> {game.Name}[/]");
                    AnsiConsole.Write(new Rule());
                    continue;
                }

                var key = $"{game.TitleId.ToLowerInvariant()}_{version.ToLowerInvariant()}_{game.IsDLC}";

                if (files.TryGetValue(key, out var file))
                {
                    var gameTrimmed = game.Name.Split('(')[0].Trim().Replace("[", " ").Replace("]", " ").Trim();
                    var exactMatch = gameTrimmed.Equals(file.Name, StringComparison.InvariantCulture);
                    var newFileName = file.FullName.Replace(file.Name, gameTrimmed);

                    switch (exactMatch)
                    {
                        case false:
                            
                            if (duplicateList.ContainsKey(game.Name))
                            {
                                Log.Fatal($"{game.TitleId.ToUpperInvariant()} -> [[[grey]D[/]]] [grey]{game.Name.EscapeMarkup()} -> {duplicateList[game.Name].EscapeMarkup()}[/] ([grey]{game.Type}[/])");
                                break;
                            }

                            if (game.Id != null && game.Id.ToLowerInvariant().StartsWith('z'))
                            {
                                Log.Fatal($"{game.TitleId.ToUpperInvariant()} -> [[[grey]D[/]]] [grey]{game.Name.EscapeMarkup()} -> CDN/NSP -> {game.Id}[/] ([grey]{game.Type}[/])");
                                break;
                            }

                            if (game.Name.StartsWith("zzzUNK"))
                            {
                                Log.Information($"{game.TitleId.ToUpperInvariant()} -> [[[grey]SKIP[/]]] [grey]{game.Name.EscapeMarkup()}[/] ([grey]{game.Type}[/])");
                                break;
                            }

                            nameErrorCount++;

                            Log.Warning($"{game.TitleId.ToUpperInvariant()} -> [[[olive]R[/]]] [green]{file.Name.EscapeMarkup()}[/] -> [olive]{gameTrimmed.EscapeMarkup()}[/] ({game.Id}) ({game.Type}) ([grey]{file.FullName.EscapeMarkup()}[/])");
                            if (settings.CorrectName)
                            {
                                if (AnsiConsole.Confirm($"Rename [green]{file.Name.EscapeMarkup()}[/] to [green]{gameTrimmed.EscapeMarkup()}[/] ?"))
                                {
                                    if (file.FullName.Equals(newFileName, StringComparison.InvariantCultureIgnoreCase))
                                    {
                                        File.Move(Path.Combine(settings.ScanDir, file.FullName), Path.Combine(settings.ScanDir, "_tmp.nsp"));
                                        File.Move(Path.Combine(settings.ScanDir, "_tmp.nsp"), Path.Combine(settings.ScanDir, newFileName));
                                    }
                                    else
                                    {
                                        if (File.Exists(Path.Combine(settings.ScanDir, newFileName)))
                                        {
                                            Log.Error($"File already exists: [red]{newFileName.EscapeMarkup()}[/]");
                                            break;
                                        }

                                        File.Move(Path.Combine(settings.ScanDir, file.FullName), Path.Combine(settings.ScanDir, newFileName));

                                    }

                                    Log.Information($"Renamed [green]{file.FullName.EscapeMarkup()}[/] to [green]{newFileName.EscapeMarkup()}[/]");
                                    nameErrorCount--;
                                }
                            }

                            break;
                        case true when settings.ShowCorrect:

                            var foundMessage = $"{game.TitleId.ToUpperInvariant()} -> [[[green]V[/]]] [green]{gameTrimmed.EscapeMarkup()}[/] ([grey]{file.FullName.EscapeMarkup()}[/])";
                            
                            if (game is { Type: "CDN", CdnFixable: false })
                            {
                                Log.Information($"{foundMessage} <- [[[olive]FOUND TIK![/]]]");
                                break;
                            }
                            
                            if (game.IsMia)
                            {
                                Log.Information($"{foundMessage} <- [[[olive]FOUND MIA![/]]]");
                                break;
                            }
                            
                            Log.Information(foundMessage);
                            break;
                    }

                    correctCount++;
                    missingBuffer = 0;
                }
                else
                {
                    if (game is { Type: "CDN", CdnFixable: false } && !game.Name.Contains("[b]"))
                    {
                        if (settings.ShowTikMissing)
                        {
                            Log.Fatal(BuildErrorMessage("X", "red", game.TitleId, game.Name, key, game.Type, game.Id ?? string.Empty, " <- [[[red]NO TIK[/]]]"));
                            missing.Add(game);

                            if (versionMap.ContainsKey(game.TitleId.ToLowerInvariant()))
                            {
                                foreach (var internalVersion in versionMap[game.TitleId.ToLowerInvariant()])
                                {
                                    var versionKey = $"{game.TitleId.ToLowerInvariant()}_{internalVersion}_{game.IsDLC}";

                                    if (files.TryGetValue(versionKey, out var matchedFile))
                                    {
                                        Log.Information($"------------------> [[[green]![/]]] Possible reconstruction match: [olive]{matchedFile.FullName.EscapeMarkup()}[/]");
                                    }
                                }
                            }
                        }

                        noTikCount++;
                        continue;
                    }
                    
                    if (game.Name.Contains("[b]"))
                    {
                        Log.Fatal(BuildErrorMessage("B", "grey", game.TitleId, game.Name, key, game.Type, game.Id ?? string.Empty, string.Empty));
                        badCount++;
                    }
                    else
                    {
                        if (game.IsMia)
                        {
                            Log.Error(BuildErrorMessage("M", "grey", game.TitleId, game.Name, key, game.Type, game.Id ?? string.Empty, string.Empty));
                            miaCount++;
                        }
                        else
                        {
                            Log.Error(BuildErrorMessage("X", "red", game.TitleId, game.Name, key, game.Type, game.Id ?? string.Empty, string.Empty));
                            missing.Add(game);
                            missingBuffer++;
                        }
                    }
                }

                if (missingBuffer == 100)
                {
                    Log.Warning("Too many missing files. Stopping.");
                    break;
                }
            }
            
            AnsiConsole.Write(new Rule());
            AnsiConsole.MarkupLine($"Correct        : [green]{correctCount}[/] ([olive]{nameErrorCount}[/]) ");
            AnsiConsole.MarkupLine($"Missing        : [red]{missing.Count}[/] ");
            AnsiConsole.MarkupLine($"MIA            : [red]{miaCount}[/] ");
            AnsiConsole.MarkupLine($"Bad            : [red]{badCount}[/] ");
            AnsiConsole.MarkupLine($"No TIK         : [red]{noTikCount}[/] ");
            AnsiConsole.MarkupLine($"CDN Fixable    : [yellow]{missing.Count(x => x is { CdnFixable: true, Type: "CDN" })}[/] ");
            AnsiConsole.MarkupLine($"Total          : {correctCount + missing.Count}");
            AnsiConsole.MarkupLine($"DAT Duplicates : {duplicateList.Count}");
            
            if (settings.SaveDatDirectory != null)
            {
                Console.WriteLine(missing.Count);
                File.WriteAllText(Path.Combine(settings.SaveDatDirectory, "nsp_std_missing.dat"), CreateXml(missing));
            }

            if (settings.ShowMissing) 
            {
                AnsiConsole.Write(new Rule("Latest 20 Missing"));

                foreach (var missingEntry in missing.Where(x => x.Id != null && !x.Id.StartsWith('z') && !x.Id.StartsWith('x')).OrderBy(x => x.Id).TakeLast(20))
                {
                    Console.WriteLine(missingEntry.Id + " - " + missingEntry.TitleId.ToUpperInvariant() + " - " + missingEntry.Name);
                }
            }
            
            AnsiConsole.Write(new Rule());
        }
        else
        {
            var finalSet = new Dictionary<string, DatEntry>();

            foreach (var item in sortedSet)
            {
                var key = $"{item.TitleId.ToUpperInvariant()}_{(string.IsNullOrWhiteSpace(item.Version) ? "v0" : item.Version)}";
                finalSet.TryAdd(key, item);
            }
            
            foreach (var file in files.Values)
            {
                var key = $"{file.TitleId.ToUpperInvariant()}_{file.Version}";
                
                if (!finalSet.ContainsKey(key) && !finalSet.ContainsKey($"{file.TitleId}_v0"))
                {
                    Log.Information($"[olive]{file.FullName.EscapeMarkup()}[/]");
                }
            }
            
            AnsiConsole.Write(new Rule());
        }

        return 0;
    }

    [GeneratedRegex("(v[0-9.]+)")]
    private static partial Regex VersionRegex();

    public static string CreateXml(HashSet<DatEntry> missingEntries)
    {
        var header = $"""<?xml version="1.0"?><datafile xmlns:xsi="http://www.w3.org/2001/XMLSchema-instance" xsi:schemaLocation="https://datomatic.no-intro.org/stuff https://datomatic.no-intro.org/stuff/schema_nointro_datfile_v3.xsd"><header><name>Nintendo - Nintendo Switch (Digital) (Standard)</name><description>Nintendo - Nintendo Switch (Digital) (Standard)</description><version>{DateTime.Now.ToString()}</version><author>[mRg]</author></header>""";
        var footer = "</datafile>";
        
        var builder = new StringBuilder();

        builder.Append(header);
        builder.AppendJoin('\n', missingEntries.Select(x => x.Xml));
        builder.Append(footer);

        return builder.ToString();
    }

    public static string BuildErrorMessage(string status, string colour, string titleId, string gameName, string lookupKey, string gameType, string niId, string extra)
    {
        return $"{titleId.ToUpperInvariant()} -> [[[{colour}]{status}[/]]] [{colour}]{gameName.EscapeMarkup()}[/] <- [{colour}]{lookupKey.Replace("_True", string.Empty).Replace("_False", string.Empty)}[/] ([grey]{gameType}[/])([grey]{niId}[/]){extra}";
    }
}

public static class Extensions
{
    public static IEnumerable<KeyValuePair<DatEntry,DatEntry>> Duplicates(this IEnumerable<DatEntry> e)
    {
        var set = new Dictionary<string, DatEntry>();

        foreach (var item in e)
        {
            var key = item.TitleId.ToUpperInvariant() + "_" + item.Version + "_" + item.Type;
            if (!set.TryAdd(key, item))
            {
                yield return new KeyValuePair<DatEntry, DatEntry>(set[key], item);
            }
        }
    }
}

