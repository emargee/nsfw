using Nsfw.Nsp;
using Serilog;
using Serilog.Events;
using Serilog.Sinks.Spectre;
using Spectre.Console;
using Spectre.Console.Cli;
using SQLite;

namespace Nsfw.Commands;

public class ScanMissingCommand : AsyncCommand<ScanMissingSettings>
{
    private SQLiteAsyncConnection? _dbConnection;

    public override async Task<int> ExecuteAsync(CommandContext context, ScanMissingSettings settings)
    {
        AnsiConsole.Write(new Rule($"[[[blue]N$FW[/]]][[{Program.GetVersion()}]]").LeftJustified());
        
        var logLevel = LogEventLevel.Information;
        
        Log.Logger = new LoggerConfiguration()
            .MinimumLevel.Is(logLevel)
            .WriteTo.Spectre(outputTemplate: "[{Level:u3}] {Message:lj} {NewLine}{Exception}{Elapsed}")
            .CreateLogger();
        
        Log.Information($"Scan Directory : [olive]{settings.ScanDir}[/]");
        AnsiConsole.Write(new Rule());
        
        var titleDbPath = Path.GetFullPath(settings.TitleDbFile);

        if (File.Exists(titleDbPath))
        {
            _dbConnection = new SQLiteAsyncConnection(titleDbPath);
        }
        
        if(_dbConnection == null)
        {
            Log.Error($"TitleDB file not found: [red]{titleDbPath}[/]");
            return 1;
        }

        var files = new Dictionary<string, FileEntry>(comparer: StringComparer.InvariantCultureIgnoreCase);
        
        var fileEntries = Directory.EnumerateFiles(settings.ScanDir, "*.nsp", new EnumerationOptions{ MatchCasing = MatchCasing.CaseInsensitive })
        .Select(x =>
        {
            var parts = x.Split('[');
            var offset = parts.Length > 4 ? 2 : 1;
            var version = parts[offset+1].TrimEnd(']').Trim();
            var displayVersion = parts[offset-1].TrimEnd(']').Trim();
            return new FileEntry
            {
                TitleId = parts[offset].TrimEnd(']').Trim(),
                Version = version,
                FullName = Path.GetFileName(x),
                Name = Path.GetFileName(x).Split('(')[0].Trim(),
                Region = Path.GetFileName(x).Split('(')[1].TrimEnd(')').Trim(),
                Type = x.Contains("[BASE]") ? "GAME" : x.Contains("[UPD]") ? "UPD" : x.Contains("[DLC]") ? "DLC" : x.Contains("[DLCUPD]") ? "DLCUPD" : "UNKNOWN",
                IsDLC = x.Contains("[DLC]") || x.Contains("[DLCUPD]"),
                IsAlt = displayVersion.EndsWith("-ALT")
            };
        });

        bool hasError = false;
        
        foreach (var fileEntry in fileEntries.Where(x => x.Type is "GAME").OrderBy(x => x.Name, StringComparer.InvariantCultureIgnoreCase))
        {
            if (!files.TryAdd(fileEntry.TitleId.ToLowerInvariant(), fileEntry))
            {
                if(fileEntry.FullName.Split("[", 2)[1] == files[fileEntry.TitleId.ToLowerInvariant()].FullName.Split("[", 2)[1])
                {
                    Log.Warning($"Duplicate file found: [red]{fileEntry.FullName.EscapeMarkup()}[/] => [green]{files[fileEntry.TitleId.ToLowerInvariant()].FullName.EscapeMarkup()}[/]");
                    hasError = true;
                }
            }
            
            //Console.WriteLine(fileEntry.Name);
            
            if (fileEntry.Name.EndsWith(" - ") || fileEntry.Name.EndsWith("-"))
            {
                if (fileEntry.FullName.Contains("(Demo)"))
                {
                    Log.Error($"Naming problem: [red]{fileEntry.FullName.EscapeMarkup()}[/]");
                    hasError = true;
                }
            }
            
            //Log.Information($"[[{fileEntry.TitleId}]] {fileEntry.Name.EscapeMarkup()} => {fileEntry.Region}");
        }
        
        if(hasError)
        {
            return 1;
        }

        Log.Information("No file errors found.");

        AnsiConsole.Write(new Rule());

        var query = await _dbConnection.Table<GameInfo>().OrderBy(x => x.Name).ThenBy(x => x.Id).ToArrayAsync();
        
        var missingCount = 0;
        var noIdCount = 0;
        var foundCount = 0;
        
        var missingList = new Dictionary<string, GameInfo>();
        var foundList = new Dictionary<string, GameInfo>();
        
        foreach (var game in query)
        {
            if (game.Id != null && game.Id.EndsWith("000") && files.TryGetValue(game.Id.ToLowerInvariant(), out var entry))
            {
                if(!foundList.ContainsKey(game.Id.ToLowerInvariant()))
                {
                    foundList.Add(game.Id.ToLowerInvariant(), game);
                    Log.Information($"[[{game.Id}]] [green]{game.Name?.ReplaceLineEndings()}[/] => [grey]{entry.FullName.EscapeMarkup()}[/]");
                    foundCount++;
                }
                else
                {
                    Log.Information($"[[----------------]] [green]{game.Name?.ReplaceLineEndings()}[/]");
                }
            }
            else
            {
                if (game.Id != null && game.Id.EndsWith("000"))
                {
                    var name = game.Name?.ReplaceLineEndings(string.Empty);
                    
                    if (!missingList.ContainsKey(game.Id.ToLowerInvariant()))
                    {
                        var cnmtQuery = _dbConnection.Table<CnmtInfo>().Where(x => x.TitleId == game.Id.ToLower()).FirstOrDefaultAsync();
                        
                        if (cnmtQuery.Result != null)
                        {
                            missingList.Add(game.Id.ToLowerInvariant(), game);
                            Log.Warning($"[[{game.Id}]] [red]{name}[/] - NOT FOUND!");
                            missingCount++; 
                        }
                    }
                    else
                    {
                        Log.Warning($"[[----------------]] [red]{name}[/]");
                    }
                }
                else if (game.Id == null)
                {
                    //Log.Fatal($"[[----------------]] [red]{game.Name}[/] - NO ID!");
                    noIdCount++;
                }
            }
        }

        // var withCnmt = 0;
        //
        // foreach (var pair in missingList.OrderBy(x => x.Value.Name, StringComparer.InvariantCultureIgnoreCase))
        // {
        //     var cnmtQuery = _dbConnection.Table<CnmtInfo>().Where(x => x.TitleId == pair.Key).FirstOrDefaultAsync();
        //     
        //     if(cnmtQuery.Result != null)
        //     {
        //         if (pair.Value.Name == null) continue;
        //         
        //         if (pair.Value.Name.ToLowerInvariant().EndsWith("demo") || pair.Value.Name.ToLowerInvariant().EndsWith("trial edition") || pair.Value.Name.ToLowerInvariant().StartsWith("demo:") || pair.Value.Name.ToLowerInvariant().EndsWith("(demo)") || pair.Value.Name.ToLowerInvariant().Contains("trial version"))
        //         {
        //             Log.Information($"[[{pair.Key}]] [[DEMO]] [red]{pair.Value.Name.ReplaceLineEndings(string.Empty)}[/] - NOT FOUND!");
        //         }
        //         else
        //         {
        //             Log.Information($"[[{pair.Key}]] [red]{pair.Value.Name.ReplaceLineEndings(string.Empty)}[/] - NOT FOUND!");
        //             withCnmt++;
        //         }
        //     }
        //     else
        //     {
        //         //Log.Warning($"[[{pair.Key}]] [[NO CMNT]] [red]{pair.Value.Name.ReplaceLineEndings(string.Empty)}[/] - NOT FOUND!");
        //     }
        // }
        
        AnsiConsole.Write(new Rule());
        AnsiConsole.MarkupLine($"Found                   : [green]{foundCount}[/]");
        AnsiConsole.MarkupLine($"Missing                 : [red]{missingCount}[/]");
        AnsiConsole.MarkupLine($"No TITLEID (in TitleTb) : [olive]{noIdCount}[/] ");
        AnsiConsole.Write(new Rule());

        // foreach (var fileEntry in fileEntries)
        // {
        //     var key = $"{fileEntry.TitleId.ToLowerInvariant()}_{fileEntry.Version.ToLowerInvariant()}_{fileEntry.IsDLC}_{fileEntry.IsAlt}";
        //     if(!files.TryAdd(key, fileEntry))
        //     {
        //         Log.Warning($"Duplicate file found: [green]{fileEntry.FullName.EscapeMarkup()}[/]");
        //         return 1;
        //     }
        // }
        //
        // var missingCount = 0;
        // var noUpdateCount = 0;
        // var validCount = 0;
        //
        // foreach (var fileEntry in files.Values.Where(fileEntry => !fileEntry.TitleId.EndsWith("800")).Where(fileEntry => fileEntry.TitleId.Last() == '0'))
        // {
        //     var gameInfos = await NsfwUtilities.GetTitleDbInfo(_dbConnection, fileEntry.TitleId);
        //     
        //     if (gameInfos.Length == 0)
        //     {
        //         AnsiConsole.Write(new Rule() { Style = Style.Parse("red") });
        //         Log.Fatal($"Title ID not found in TitleDB: [olive]{fileEntry.Name.EscapeMarkup()} => {fileEntry.TitleId}[/]");
        //         AnsiConsole.Write(new Rule() { Style = Style.Parse("red") });
        //         continue;
        //     }
        //     
        //     var updates = await NsfwUtilities.LookUpUpdates(_dbConnection, fileEntry.TitleId);
        //
        //     if(updates.Length == 0)
        //     {
        //         if (settings.ShowAll)
        //         {
        //             Log.Information($"{fileEntry.Name.EscapeMarkup()} [[{fileEntry.TitleId}]] => [olive]No updates found.[/]");
        //         }
        //         noUpdateCount++;
        //         continue;
        //     }
        //     
        //     foreach (var update in updates)
        //     {
        //         var key = $"{update.TitleId[..^3] + "800"}_v{update.Version.ToLowerInvariant()}_{fileEntry.IsDLC}";
        //         
        //         if(files.TryGetValue(key, out var file))
        //         {
        //             if (settings.ShowAll)
        //             {
        //                 Log.Information($"{fileEntry.Name.EscapeMarkup()} [[{fileEntry.TitleId}]] => [green]v{update.Version}[/] => [green]{file.FullName.EscapeMarkup()}[/]");
        //             }
        //             validCount++;
        //         }
        //         else
        //         {
        //             Log.Error($"{fileEntry.Name.EscapeMarkup()} [[{fileEntry.TitleId}]] => [red]v{update.Version}[/] Missing.");
        //             missingCount++;
        //         }
        //     }
        // }
        //
        // AnsiConsole.Write(new Rule());
        // AnsiConsole.MarkupLine($"Correct                 : [green]{validCount}[/]");
        // AnsiConsole.MarkupLine($"Missing                 : [red]{missingCount}[/] ");
        // AnsiConsole.MarkupLine($"No Updates (in TitleTb) : [olive]{noUpdateCount}[/] ");
        // AnsiConsole.Write(new Rule());

        return 0;
    }
}