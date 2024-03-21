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
                Type = x.Contains("[BASE]") ? "GAME" : x.Contains("[UPD]") ? "UPD" : x.Contains("[DLC]") ? "DLC" : x.Contains("[DLCUPD]") ? "DLCUPD" : "UNKNOWN",
                IsDLC = x.Contains("[DLC]") || x.Contains("[DLCUPD]"),
                IsAlt = displayVersion.EndsWith("-ALT")
            };
        });

        foreach (var fileEntry in fileEntries.Where(x => x.Type is "GAME"))
        {
            if (!files.TryAdd(fileEntry.TitleId.ToLowerInvariant(), fileEntry))
            {
                Log.Warning($"Duplicate file found: [green]{fileEntry.FullName.EscapeMarkup()}[/]");
            }
        }
        
        // var query = await _dbConnection.Table<CnmtInfo>().ToArrayAsync();
        //
        // var missingList = new Dictionary<string, CnmtInfo>();
        // var foundCount = 0;
        //
        // foreach (var cnmtInfo in query.DistinctBy(x => x.TitleId.ToLowerInvariant()))
        // {
        //     if(cnmtInfo.TitleId.ToLowerInvariant().EndsWith("000") && files.TryGetValue(cnmtInfo.TitleId.ToLowerInvariant(), out var entry))
        //     {
        //         //Log.Information($"[[{cnmtInfo.TitleId}]] => [grey]{entry.FullName.EscapeMarkup()}[/]");
        //         foundCount++;
        //     }
        //     else
        //     {
        //         if(cnmtInfo.TitleId.ToLowerInvariant().EndsWith("000"))
        //         {
        //             missingList.Add(cnmtInfo.TitleId.ToLowerInvariant(), cnmtInfo);
        //         }
        //     }
        // }
        //
        // foreach (var pair in missingList)
        // {
        //     var nameQuery = await _dbConnection.Table<GameInfo>().Where(x => x.Id != null && x.Id.ToLower() == pair.Key).FirstOrDefaultAsync();
        //     var name = nameQuery != null ? nameQuery.Name : "Unknown";
        //     Log.Warning($"[[{pair.Key}]] => {name} => [red]NOT FOUND![/]");
        // }
        //
        // AnsiConsole.Write(new Rule());
        // AnsiConsole.MarkupLine($"Found                   : [green]{foundCount}[/]");
        // AnsiConsole.MarkupLine($"Missing                 : [red]{missingList.Count}[/] ");
        // AnsiConsole.Write(new Rule());

        var query = await _dbConnection.Table<GameInfo>().ToArrayAsync();
        
        var missingCount = 0;
        var noIdCount = 0;
        var foundCount = 0;
        
        var missingList = new Dictionary<string, GameInfo>();
        
        foreach (var game in query)
        {
            if (game.Id != null && game.Id.EndsWith("000") && files.TryGetValue(game.Id.ToLowerInvariant(), out var entry))
            {
                //Log.Information($"[[{game.Id}]] [green]{game.Name}[/] => [grey]{entry.FullName.EscapeMarkup()}[/]");
                foundCount++;
            }
            else
            {
                if (game.Id != null && game.Id.EndsWith("000"))
                {
                    if (missingList.ContainsKey(game.Id.ToLowerInvariant()))
                    {
                        //Log.Information($"Already found: [grey]{game.Name}[/]");
                    }
                    else
                    {
                        missingList.Add(game.Id.ToLowerInvariant(), game);
                        //Log.Warning($"[[{game.Id}]] [red]{game.Name.ReplaceLineEndings(string.Empty)}[/] - NOT FOUND!");
                        missingCount++;
                    }
                }
                else if (game.Id == null)
                {
                    //Log.Fatal($"[[----------------]] [red]{game.Name}[/] - NO ID!");
                    noIdCount++;
                }
            }
        }

        var withCnmt = 0;
        
        foreach (var pair in missingList.OrderBy(x => x.Value.Name, StringComparer.InvariantCultureIgnoreCase))
        {
            var cnmtQuery = _dbConnection.Table<CnmtInfo>().Where(x => x.TitleId == pair.Key).FirstOrDefaultAsync();
            
            if(cnmtQuery.Result != null)
            {
                if (pair.Value.Name.ToLowerInvariant().EndsWith("demo") || pair.Value.Name.ToLowerInvariant().EndsWith("trial edition") || pair.Value.Name.ToLowerInvariant().StartsWith("demo:") || pair.Value.Name.ToLowerInvariant().EndsWith("(demo)") || pair.Value.Name.ToLowerInvariant().Contains("trial version"))
                {
                    Log.Information($"[[{pair.Key}]] [[DEMO]] [red]{pair.Value.Name.ReplaceLineEndings(string.Empty)}[/] - NOT FOUND!");
                }
                else
                {
                    Log.Information($"[[{pair.Key}]] [red]{pair.Value.Name.ReplaceLineEndings(string.Empty)}[/] - NOT FOUND!");
                    withCnmt++;
                }
            }
            else
            {
                //Log.Warning($"[[{pair.Key}]] [[NO CMNT]] [red]{pair.Value.Name.ReplaceLineEndings(string.Empty)}[/] - NOT FOUND!");
            }
        }
        
        AnsiConsole.Write(new Rule());
        AnsiConsole.MarkupLine($"Found                   : [green]{foundCount}[/]");
        AnsiConsole.MarkupLine($"Missing                 : [red]{missingCount}[/] ({withCnmt} with CNMT)");
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