using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.InteropServices.ComTypes;
using System.Text.Json;
using System.Text.Json.Serialization.Metadata;
using Spectre.Console;
using Spectre.Console.Cli;
using SQLite;

namespace Nsfw.Commands;

public class BuildTitleDbCommand : AsyncCommand<BuildTitleDbSettings>
{
    private const string TitleDbName = "titledb.db";
    
    public override async Task<int> ExecuteAsync(CommandContext context, BuildTitleDbSettings settings)
    {
        var dbPath = Path.Combine(settings.TitleDbDirectory, TitleDbName);
        
        if(File.Exists(dbPath) && settings.CleanDatabase)
        {
            AnsiConsole.MarkupLine($"[[[green]DONE[/]]] Clean old DB.");
            File.Delete(dbPath);
        }
        
        var db = new SQLiteAsyncConnection(dbPath);
        await db.EnableWriteAheadLoggingAsync();
        await db.CreateTableAsync<GameInfo>();
        await db.CreateTableAsync<TitleRegion>();
        await db.CreateTableAsync<TitleVersions>();
        await db.CreateTableAsync<CnmtInfo>();
        
        AnsiConsole.Markup("Ingesting: [olive]CNMT[/]...");
        
        var cnmtFilePath = Path.Combine(settings.TitleDbDirectory, "cnmts.json");
        await using var cnmtFs = File.OpenRead(cnmtFilePath);
        
        var cnmtEntries = JsonSerializer.Deserialize(cnmtFs, SourceGenerationContext.Default.DictionaryStringDictionaryStringDtoCnmtInfo);

        if (cnmtEntries != null)
        {
            foreach (var dbEntry in cnmtEntries.SelectMany(cnmtEntry => 
                         from versions in cnmtEntry.Value 
                         from contentEntry in versions.Value.ContentEntries 
                         select new CnmtInfo
                         {
                             TitleId = cnmtEntry.Key,
                             Version = versions.Key,
                             NcaId = contentEntry.NcaId,
                             NcaType = contentEntry.Type
                         }))
            {
                await db.InsertAsync(dbEntry);
            }
        }
        else
        {
            AnsiConsole.MarkupLine("[[red]Error[/]] Cannot parse CNMTS file.");
            return 1;
        }

        AnsiConsole.MarkupLine($"[[[green]DONE[/]]]");
        
        AnsiConsole.Markup("Ingesting: [olive]VERSIONS[/]...");
        
        var versionFilePath = Path.Combine(settings.TitleDbDirectory, "versions.json");
        await using var versionFs = File.OpenRead(versionFilePath);
        
        var versionEntries = JsonSerializer.Deserialize(versionFs, SourceGenerationContext.Default.DictionaryStringDictionaryStringString);

        if (versionEntries != null)
        {
            foreach (var versionInfo in versionEntries
                         .SelectMany(version => version.Value.Select(titleVersion => 
                             new TitleVersions
                             {
                                 TitleId = version.Key,
                                 Version = titleVersion.Key,
                                 ReleaseDate = titleVersion.Value
                             })))
            {
                await db.InsertAsync(versionInfo);
            }
        }
        else
        {
            AnsiConsole.MarkupLine("[[red]Error[/]] Cannot parse VERSIONS file.");
            return 1;
        }

        AnsiConsole.MarkupLine($"[[[green]DONE[/]]]");
        
        var entries = new HashSet<string>();
        entries.Add(Path.Combine(settings.TitleDbDirectory, "US.en.json"));
        entries.Add(Path.Combine(settings.TitleDbDirectory, "JP.ja.json"));
        entries.Add(Path.Combine(settings.TitleDbDirectory, "DE.de.json"));
        entries.Add(Path.Combine(settings.TitleDbDirectory, "FR.fr.json"));
        entries.Add(Path.Combine(settings.TitleDbDirectory, "ES.es.json"));
        entries.Add(Path.Combine(settings.TitleDbDirectory, "IT.it.json"));
        entries.Add(Path.Combine(settings.TitleDbDirectory, "NL.nl.json"));
        entries.Add(Path.Combine(settings.TitleDbDirectory, "PT.pt.json"));
        entries.Add(Path.Combine(settings.TitleDbDirectory, "KR.ko.json"));
        entries.Add(Path.Combine(settings.TitleDbDirectory, "RU.ru.json"));
        
        entries.UnionWith(Directory.EnumerateFiles(settings.TitleDbDirectory, "??.??.json", SearchOption.TopDirectoryOnly));
        
        var timer = new Stopwatch();
        
        foreach (var entry in entries)
        {
            AnsiConsole.Markup("Ingesting: [olive]" + entry + "[/]...");
            timer.Start();
            
            await using var fs = File.OpenRead(entry);
            
            var gameEntries = JsonSerializer.Deserialize(fs, SourceGenerationContext.Default.DictionaryStringGameInfo);
        
            if (gameEntries == null)
            {
                Console.WriteLine("Cannot parse file.");
                return 1;
            }
            
            var count = 0;
            
            foreach (var gameEntry in gameEntries)
            {
                var game = gameEntry.Value;
        
                game.Region = Path.GetFileName(entry).Replace(".json",string.Empty);
                game.RegionLanguage = game.Region.Split(".")[1];
                
                var titleRegion = new TitleRegion{ NsuId = game.NsuId, Region = game.Region };
                await db.InsertAsync(titleRegion);
                
                if(db.Table<GameInfo>().Where(x => x.NsuId == game.NsuId && x.Name == game.Name).CountAsync().Result != 0)
                {
                    continue;
                }
                
                await db.InsertAsync(game);
                count++;
            }
            
            AnsiConsole.MarkupLine($"[[[green]DONE[/]]] - Added {count} - ({timer.Elapsed.TotalSeconds:0.00}s)");
            timer.Reset();
        }
        
        AnsiConsole.MarkupLine($"[[[green]COMPLETE![/]]]");
        await db.CloseAsync();
        
        return 0;
    }
    
}