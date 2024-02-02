using System.ComponentModel;
using System.Xml.Linq;
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
                Log.Warning($"Duplicate file found: [green]{fileEntry.FullName.EscapeMarkup()}[/]");
                return 1;
            }
        }

        var missingCount = 0;
        var noUpdateCount = 0;
        var validCount = 0;

        foreach (var fileEntry in files.Values.Where(fileEntry => !fileEntry.TitleId.EndsWith("800")).Where(fileEntry => fileEntry.TitleId.Last() == '0'))
        {
            var gameInfos = await NsfwUtilities.GetTitleDbInfo(_dbConnection, fileEntry.TitleId);
            
            if (gameInfos.Length == 0)
            {
                AnsiConsole.Write(new Rule() { Style = Style.Parse("red") });
                Log.Fatal($"Title ID not found in TitleDB: [olive]{fileEntry.Name.EscapeMarkup()} => {fileEntry.TitleId}[/]");
                AnsiConsole.Write(new Rule() { Style = Style.Parse("red") });
                continue;
            }
            
            var updates = await NsfwUtilities.LookUpUpdates(_dbConnection, fileEntry.TitleId);

            if(updates.Length == 0)
            {
                if (settings.ShowAll)
                {
                    Log.Information($"{fileEntry.Name.EscapeMarkup()} [[{fileEntry.TitleId}]] => [olive]No updates found.[/]");
                }
                noUpdateCount++;
                continue;
            }
            
            foreach (var update in updates)
            {
                var key = $"{update.TitleId[..^3] + "800"}_v{update.Version.ToLowerInvariant()}_{fileEntry.IsDLC}";
                
                if(files.TryGetValue(key, out var file))
                {
                    if (settings.ShowAll)
                    {
                        Log.Information($"{fileEntry.Name.EscapeMarkup()} [[{fileEntry.TitleId}]] => [green]v{update.Version}[/] => [green]{file.FullName.EscapeMarkup()}[/]");
                    }
                    validCount++;
                }
                else
                {
                    Log.Error($"{fileEntry.Name.EscapeMarkup()} [[{fileEntry.TitleId}]] => [red]v{update.Version}[/] Missing.");
                    missingCount++;
                }
            }
        }
        
        AnsiConsole.Write(new Rule());
        AnsiConsole.MarkupLine($"Correct                 : [green]{validCount}[/]");
        AnsiConsole.MarkupLine($"Missing                 : [red]{missingCount}[/] ");
        AnsiConsole.MarkupLine($"No Updates (in TitleTb) : [olive]{noUpdateCount}[/] ");
        AnsiConsole.Write(new Rule());

        return 0;
    }
}

public class ScanMissingSettings : CommandSettings
{
    [CommandOption("--titledb <FILE>")]
    [Description("Path to titledb.db file.")]
    [DefaultValue("./titledb/titledb.db")]
    public string TitleDbFile { get; set; } = string.Empty;
    
    [CommandOption("-a|--all")]
    [Description("Show all (matched + no updates)")]
    public bool ShowAll { get; set; }
    
    [CommandArgument(0, "<NSP_DIR>")]
    [Description("Path to NSP directory to scan.")]
    public string ScanDir { get; set; } = string.Empty;

    public override ValidationResult Validate()
    {
        if(ScanDir.StartsWith('~'))
        {
            ScanDir = ScanDir.Replace("~", Environment.GetFolderPath(Environment.SpecialFolder.UserProfile));
        }
        
        if(TitleDbFile.StartsWith('~'))
        {
            TitleDbFile = TitleDbFile.Replace("~", Environment.GetFolderPath(Environment.SpecialFolder.UserProfile));
        }
        
        if (string.IsNullOrWhiteSpace(ScanDir))
        {
            return ValidationResult.Error("Scan directory is required.");
        }

        return base.Validate();
    }
}