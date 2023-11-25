using System.Diagnostics;
using System.Runtime.InteropServices.ComTypes;
using System.Text.Json;
using System.Text.Json.Nodes;
using CliWrap;
using CliWrap.Buffered;
using Spectre.Console;
using Spectre.Console.Cli;
using SQLite;

namespace Nsfw.Commands;

public class BuildTitleDbCommand : AsyncCommand<BuildTitleDbSettings>
{
    public const string TitleDbName = "titledb.db"; 
    
    public override async Task<int> ExecuteAsync(CommandContext context, BuildTitleDbSettings settings)
    {
        if(settings.Refresh)
        {
            AnsiConsole.MarkupLine($"[red]WARN[/] Refreshing TitleDB files from source.");
            var result = await RefreshTitleDbFiles(settings);
            
            if(result != 0)
            {
                return result;
            }
        }

        var dbPath = Path.Combine(settings.TitleDbDirectory, TitleDbName);
        
        if(File.Exists(dbPath) && settings.CleanDatabase)
        {
            AnsiConsole.MarkupLine($"[[[green]DONE[/]]] Clean old DB.");
            File.Delete(dbPath);
        }
        
        var db = new SQLiteAsyncConnection(dbPath);
        await db.EnableWriteAheadLoggingAsync();
        await db.CreateTableAsync<GameInfo>();
        
        var entries = new HashSet<string>();
        entries.Add(Path.Combine(settings.TitleDbDirectory, "converted.US.en.json"));
        entries.Add(Path.Combine(settings.TitleDbDirectory, "converted.US.es.json"));
        entries.Add(Path.Combine(settings.TitleDbDirectory, "converted.JP.ja.json"));
        entries.UnionWith(Directory.EnumerateFiles(settings.TitleDbDirectory, "converted.*.json", SearchOption.TopDirectoryOnly));
        
        foreach (var entry in entries)
        {
            Console.Write("Ingesting: " + entry + "...");
            
            await using var fs = File.OpenRead(entry);

            var options = new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true,
                AllowTrailingCommas = true
            };

            var gameEntries = JsonSerializer.DeserializeAsyncEnumerable<GameInfo>(fs, options);

            await foreach (var game in gameEntries)
            {
                if (game == null)
                {
                    //Console.WriteLine("NULL");
                    continue;
                }
                
                game.RegionLanguage = game.Region.Split(".")[1];
                
                if(db.Table<GameInfo>().Where(x => 
                       x.NsuId == game.NsuId && x.Name == game.Name).CountAsync().Result != 0)
                {
                    //Console.WriteLine("DUPLICATE");
                    continue;
                }
                
                await db.InsertAsync(game);
            }
            
            AnsiConsole.MarkupLine("[[[green]DONE[/]]]");
        }
        
        return 0;
    }

    private async Task<int> RefreshTitleDbFiles(BuildTitleDbSettings settings)
    {
        await using var openStream = File.OpenRead(Path.Combine(settings.TitleDbDirectory,"languages.json")); 
        var regions = await JsonSerializer.DeserializeAsync<Dictionary<string, string[]>>(openStream);

        if (regions == null)
        {
            AnsiConsole.MarkupLine("[red]Error:[/] Failed to parse languages.json.");
            return 1;
        }

        var version = string.Empty;
            
        try
        {
            var cli = Cli.Wrap("jq").WithArguments("--version").ExecuteBufferedAsync();
            version = cli.Task.Result.StandardOutput.ReplaceLineEndings(string.Empty).Trim();
        }
        catch (Exception)
        {
            AnsiConsole.MarkupLine($"[red]Error:[/] jq not found in PATH.");
            return 1;
        }
            
        AnsiConsole.MarkupLine($"Found: [olive]jq[/] ({version})");
            
        // var fileList = string.Join(' ',regions.Select(x => x.Value.Select(y => Path.Combine(settings.TitleDbDirectory, $"{x.Key}.{y}.json")).ToArray()).SelectMany(x => x).ToArray());
        // var outputFile = Path.Combine(settings.TitleDbDirectory, $"combined.json");
        // await using var output = File.Create(outputFile, 4096, FileOptions.Asynchronous);
        //
        // Console.Write("Combining TitleDB files...");
        //
        // await (Cli.Wrap("jq").WithArguments($"-n -r \"[inputs|values[]]\" {fileList}") | output).ExecuteAsync();
        //
        // Console.WriteLine("[DONE]");
            
        foreach (var region in regions)
        {
            foreach (var language in region.Value)
            {
                var fullLanguage = $"{region.Key}.{language}";
                    
                var path = Path.Combine(settings.TitleDbDirectory, $"{fullLanguage}.json");
                    
                if (!File.Exists(path))
                {
                    AnsiConsole.MarkupLine($"[red]Error:[/] {path} does not exist.");
                }
                    
                Console.Write($"Converting [{fullLanguage}]...");
            
                var inputFile = Path.Combine(settings.TitleDbDirectory, $"{fullLanguage}.json");
                var outputFile = Path.Combine(settings.TitleDbDirectory, $"converted.{fullLanguage}.json");
                File.Delete(outputFile);
                    
                await using var input = File.OpenRead(inputFile);
                await using var output = File.Create(outputFile, 4096, FileOptions.Asynchronous);
            
                await (input | Cli.Wrap("jq").WithArguments($"-r \"[values[]|. + {{\\\"region\\\":\\\"{fullLanguage}\\\"}}]\"") | output).ExecuteAsync();
                    
                Console.WriteLine("[DONE]");
            }
        }

        return 0;
    }
}