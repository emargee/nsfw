using System.Diagnostics;
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
    public bool IsDLC { get; set; }
}

public class DatEntry
{
    public string Name { get; set; } = string.Empty;
    public string TitleId { get; set; } = string.Empty;
    public string Version { get; set; } = string.Empty;
    public string Type { get; set; } = string.Empty;
    public string Xml { get; set; } = string.Empty;
    public bool Fixable => Xml.Contains(".tik");
    public string? Id { get; set; }
    public string? Sha1 { get; set; }
    public bool IsDLC { get; set; }
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
            return new FileEntry
            {
                TitleId = parts[offset].TrimEnd(']').Trim(),
                Version = parts[offset+1].TrimEnd(']').Trim(),
                FullName = Path.GetFileName(x),
                Name = Path.GetFileName(x).Split('(')[0].Trim(),
                Type = x.Contains("[BASE]") ? "GAME" : x.Contains("[UPD]") ? "UPD" : x.Contains("[DLC]") ? "DLC" : x.Contains("[DLCUPD]") ? "DLCUPD" : "UNKNOWN",
                IsDLC = x.Contains("[DLC]") || x.Contains("[DLCUPD]")
            };
        });

        foreach (var fileEntry in fileEntries)
        {
            var key = $"{fileEntry.TitleId.ToLowerInvariant()}_{fileEntry.Version.ToLowerInvariant()}_{fileEntry.IsDLC}";
            if(!files.TryAdd(key, fileEntry))
            {
                Console.WriteLine(files[key].FullName);
                Console.WriteLine(key);
                Log.Warning($"Duplicate file found: [green]{fileEntry.FullName.EscapeMarkup()}[/]");
                return 1;
            }
        }
        
        //Combine and keep duplicates
        var sortedSet = xml1
            .Descendants("game")
            .Concat(xml2.Descendants("game"))
            .Concat(xml3.Descendants("game"))
            .Descendants("game_id")
            .Select(x =>
            {
                var name = x.Parent?.Attribute("name")?.Value;
                if (name != null)
                    return new DatEntry
                    {
                        Name = name,
                        TitleId = x.Value,
                        Version = VersionRegex().IsMatch(name) ? VersionRegex().Match(name).Value : "v0",
                        Type = x.Parent?.Descendants().Count() <= 4 ? "NSP" : "CDN",
                        Xml = x.Parent?.ToString() ?? string.Empty,
                        Id = x.Parent?.Attribute("id")?.Value,
                        Sha1 = x.Parent?.Descendants().Count() <= 4 ? x.Parent.Descendants("rom").First().Attribute("sha1")?.Value : null,
                        IsDLC = name.Contains("(DLC)")
                    };
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

            var duplicateList = new HashSet<string>(StringComparer.InvariantCulture);

            foreach (var duplicate in duplicates)
            {
                duplicateList.Add(duplicate.Value.Name); // Key = first instance, Value = second instance (duplicate) - go with first
            }

            if(settings.ByLetter != null)
            {
                searchList = searchList.Where(x => x.Name.StartsWith(settings.ByLetter, StringComparison.InvariantCultureIgnoreCase));
            }

            foreach (var game in searchList)
            {
                Debug.Assert(game.Name != null, "game.Name != null");

                var version = "v0";

                if (game.Name.Contains("(v"))
                {
                    version = VersionRegex().Match(game.Name).Value;
                }

                if (game.TitleId.Length != 16)
                {
                    AnsiConsole.Write(new Rule());
                    Log.Fatal($"[red]!!DAT ERROR: Invalid Title ID Length -> {game.TitleId} -> {game.Name}[/]");
                    AnsiConsole.Write(new Rule());
                    continue;
                }

                var key = $"{game.TitleId.ToLowerInvariant()}_{version.ToLowerInvariant()}_{game.IsDLC}";

                if (files.TryGetValue(key, out var file))
                {
                    var gameTrimmed = game.Name.Split('(')[0].Trim();
                    var exactMatch = gameTrimmed.Equals(file.Name, StringComparison.InvariantCulture);
                    var newFileName = file.FullName.Replace(file.Name, gameTrimmed);
                    
                    switch (exactMatch)
                    {
                        case false:
                            nameErrorCount++;
                            
                            if (duplicateList.Contains(game.Name))
                            {
                                Log.Fatal($"{game.TitleId.ToUpperInvariant()} -> [grey][[D]] {game.Name.EscapeMarkup()}[/] ([grey]{game.Type}[/])");
                                break;
                            }
                            Log.Warning($"{game.TitleId.ToUpperInvariant()} -> [[[olive]R[/]]] [green]{gameTrimmed.EscapeMarkup()}[/] -> [olive]{file.Name.EscapeMarkup()}[/] ({game.Type}) ([grey]{file.FullName.EscapeMarkup()}[/])");
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
                                }
                            }

                            break;
                        case true when settings.ShowCorrect:
                            Log.Information($"{game.TitleId.ToUpperInvariant()} -> [[[green]V[/]]] [green]{gameTrimmed.EscapeMarkup()}[/] ([grey]{file.FullName.EscapeMarkup()}[/])");
                            break;
                    }

                    correctCount++;
                    missingBuffer = 0;
                }
                else
                {
                    if (game.Name.Contains("[b]"))
                    {
                        Log.Fatal($"{game.TitleId.ToUpperInvariant()} -> [grey][[B]] {game.Name.EscapeMarkup()}[/] <- [grey]{key.Replace("_True",string.Empty).Replace("_False", string.Empty)}[/] ([grey]{game.Type}[/])");
                    }
                    else
                    {
                        Log.Error($"{game.TitleId.ToUpperInvariant()} -> [[[red]X[/]]] [red]{game.Name.EscapeMarkup()}[/] <- [red]{key.Replace("_True",string.Empty).Replace("_False", string.Empty)}[/] ([grey]{game.Type}[/])");
                        missing.Add(game);
                        missingBuffer++;
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
            AnsiConsole.MarkupLine($"CDN Fixable    : [yellow]{missing.Count(x => x.Fixable)}[/] ");
            AnsiConsole.MarkupLine($"Total          : {correctCount + missing.Count}");
            AnsiConsole.MarkupLine($"DAT Duplicates : {duplicateList.Count / 2}");
            AnsiConsole.Write(new Rule());
            
            if (settings.SaveDatDirectory != null)
            {
                File.WriteAllText(Path.Combine(settings.SaveDatDirectory, "nsp_std_missing.dat"), CreateXml(missing));
            }
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

    [GeneratedRegex("(v[0-9])\\w+")]
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

