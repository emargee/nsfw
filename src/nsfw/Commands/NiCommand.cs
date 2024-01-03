using System.Collections.Immutable;
using System.Diagnostics;
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
}

public class DatEntry
{
    public string Name { get; set; } = string.Empty;
    public string TitleId { get; set; } = string.Empty;
    public string Version { get; set; } = string.Empty;
    public string Type { get; set; } = string.Empty;
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
        
        Log.Information($"NSP Dat Loaded : [olive]{settings.NspDat}[/]");
        Log.Information($"CDN Dat Loaded : [olive]{settings.CdnDat}[/]");
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

        var files = Directory.EnumerateFiles(settings.ScanDir, "*.nsp", new EnumerationOptions{ MatchCasing = MatchCasing.CaseInsensitive })
            .Select(x => new FileEntry
            {
                TitleId = x.Split('[')[2].TrimEnd(']').Trim(),
                Version = x.Split('[')[3].TrimEnd(']').Trim(),
                FullName = Path.GetFileName(x),
                Name = Path.GetFileName(x).Split('(')[0].Trim(),
                Type = x.Contains("[BASE]") ? "GAME" : x.Contains("[UPD]") ? "UPD" : x.Contains("[DLC]") ? "DLC" : "UNKNOWN"
            })
            .ToImmutableDictionary(x => $"{x.TitleId.ToLowerInvariant()}_{x.Version}", x => x);
        
        //Combine and keep duplicates
        var sortedSet = xml1
            .Descendants("game")
            .Concat(xml2.Descendants("game"))
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
                    };
                return new DatEntry();
            })
            .DistinctBy(x => x.Name)
            .OrderBy(x => x.Name, StringComparer.InvariantCultureIgnoreCase);
        
        if (!settings.Reverse)
        {
            int correctCount = 0;
            int nameErrorCount = 0;
            int missingCount = 0;

            foreach (var game in sortedSet)
            {
                Debug.Assert(game.Name != null, "game.Name != null");

                var version = "v0";

                if (game.Name.Contains("(v"))
                {
                    version = VersionRegex().Match(game.Name).Value;
                }

                var key = $"{game.TitleId.ToLowerInvariant()}_{version.ToLowerInvariant()}";

                if (files.TryGetValue(key, out var file))
                {
                    var gameTrimmed = game.Name.Split('(')[0].Trim();
                    var exactMatch = gameTrimmed.Equals(file.Name, StringComparison.InvariantCultureIgnoreCase);

                    switch (exactMatch)
                    {
                        case false:
                            nameErrorCount++;
                            Log.Warning($"{game.TitleId.ToUpperInvariant()} -> [green]{gameTrimmed.EscapeMarkup()}[/] -> [olive]{file.Name.EscapeMarkup()}[/] ([grey]{file.FullName.EscapeMarkup()}[/])");
                            if (settings.CorrectName)
                            {
                                if (AnsiConsole.Confirm($"Rename [green]{file.Name.EscapeMarkup()}[/] to [green]{gameTrimmed.EscapeMarkup()}[/] ?"))
                                {
                                    var newFileName = file.FullName.Replace(file.Name, gameTrimmed);
                                    File.Move(Path.Combine(settings.ScanDir, file.FullName), Path.Combine(settings.ScanDir, newFileName));
                                    Log.Information($"Renamed [green]{file.FullName.EscapeMarkup()}[/] to [green]{newFileName.EscapeMarkup()}[/]");
                                }
                            }

                            break;
                        case true when settings.ShowCorrect:
                            Log.Information($"{game.TitleId.ToUpperInvariant()} -> [green]{gameTrimmed.EscapeMarkup()}[/] ([grey]{file.FullName.EscapeMarkup()}[/])");
                            break;
                    }

                    correctCount++;
                }
                else
                {
                    if (game.Name.Contains("[b]"))
                    {
                        Log.Fatal($"{game.TitleId.ToUpperInvariant()} -> [maroon]BAD DUMP ->  {game.Name.EscapeMarkup()}[/] <- [maroon]{key}[/] ([grey]{game.Type}[/])");
                    }
                    else
                    {
                        Log.Error($"{game.TitleId.ToUpperInvariant()} -> [red]{game.Name.EscapeMarkup()}[/] <- [red]{key}[/] ([grey]{game.Type}[/])");
                        missingCount++;
                    }
                }

                if (missingCount == 100)
                {
                    Log.Warning("Too many missing files. Stopping.");
                    break;
                }
            }
            
            AnsiConsole.Write(new Rule());
            AnsiConsole.MarkupLine($"Correct    : [green]{correctCount}[/] ([olive]{nameErrorCount}[/]) ");
            AnsiConsole.MarkupLine($"Missing    : [red]{missingCount}[/] ");
            AnsiConsole.MarkupLine($"Total      : {correctCount + missingCount}");
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
        }

        return 0;
    }

    [GeneratedRegex("(v[0-9])\\w+")]
    private static partial Regex VersionRegex();
}

