using Nsfw.Nsp;
using Spectre.Console;
using Spectre.Console.Cli;

namespace Nsfw.Commands;

public class QueryCommand : AsyncCommand<QuerySettings>
{
    public override async Task<int> ExecuteAsync(CommandContext context, QuerySettings settings)
    {
        var query = settings.Query;
        
        if (query.EndsWith("800"))
        {
            Console.WriteLine("This is an update title ID. Searching parent title ID..");
            query = query[..^3] + "000";
        }
        
        var results = await NsfwUtilities.GetTitleDbInfo(settings.TitleDbFile, query);
        
        if(results.Length == 0)
        {
            Console.WriteLine("No results found.");
            return 0;
        }
        
        var table = new Table
        {
            ShowHeaders = false
        };
        
        table.AddColumn("Property");
        table.AddColumn("Value");
        
        table.AddRow("Name",$"[olive]{results[0].Name}[/]");
        table.AddRow("Publisher",results[0].Publisher ?? "Unknown");
        table.AddRow("Description",results[0].Description ?? "Unknown");
        
        var titleResults = Nsp.NsfwUtilities.GetTitleDbInfo(settings.TitleDbFile, settings.Query).Result;
        
        if (titleResults.Length > 0)
        {
            var regionTable = new Table() { ShowHeaders = false };
            regionTable.AddColumns("Region", "Title");
            foreach (var titleResult in titleResults.DistinctBy(x => x.RegionLanguage))
            {
                regionTable.AddRow(new Markup($"{titleResult.Name!.ReplaceLineEndings(string.Empty).EscapeMarkup()}"), new Markup($"{titleResult.RegionLanguage.ToUpper()}"));
            }
                
            table.AddRow(new Text("Regional Titles"), regionTable);
        }
        
        var variationTable = new Table{
            ShowHeaders = false
        };
        variationTable.AddColumn("Property");
        variationTable.AddColumn("Value");
        
        foreach (var result in results.DistinctBy(x => x.NsuId))
        {
            if (result == null) continue;
            
            variationTable.AddRow("NSU ID", result.NsuId.ToString());
                
            var regions = await NsfwUtilities.LookUpRegions(settings.TitleDbFile, result.NsuId);
            
            var regionString = string.Join(",", regions);
            
            variationTable.AddRow("CDN Regions", regionString);

            var region = "UNKNOWN";
            regionString = regionString.ToUpperInvariant();

            string[] americas = ["US.", "CA.", "MX."];
            string[] europe = ["GB.","DE.","FR.","ES.", "IT.", "PT.", "CH.", "HU.", "LT.", "BE.", "BG.", "EE.", "LU.", "CH.", "HR.", "SI.", "AT.", "GR.", "LU.", "NO.", "DK.", "CZ.", "RO.", "ZA.", "NZ.", "BE.", "CH.", "LV.", "SK.", "SE.", "FI.", "IE.", "AU.", "MT.", "CY."];
            string[] asia = ["HK.","KR.","JP"];
            
            if (americas.Any(regionString.Contains))
            {
                region = "Americas";
            }
            
            if (europe.Any(regionString.Contains))
            {
                region = "Europe";
            }
            
            if(asia.Any(regionString.Contains))
            {
                region = "Asia";
            }
            
            if(regionString.Equals("KR.KO"))
            {
                region = "Korea";
            }

            if (regionString.Equals("JP.JA"))
            {
                region = "Japan";
            }
            
            if(regionString.Equals("HK.ZH"))
            {
                region = "China";
            }
            
            variationTable.AddRow("Region", region);
        }
        
        table.AddRow(new Text("eShop Variations"), variationTable);
        
        var updates = await NsfwUtilities.LookUpUpdates(settings.TitleDbFile, query);

        if (updates.Length > 0)
        {
            var updateTable = new Table
            {
                ShowHeaders = false
            };
            updateTable.AddColumn("Property");
            updateTable.AddColumn("Value");

            foreach (var update in updates)
            {
                updateTable.AddRow("v" + update.Version, update.ReleaseDate);
            }

            table.AddRow(new Text("Updates"), updateTable);
        }
        
        var relatedResults = NsfwUtilities.LookUpRelatedTitles(settings.TitleDbFile, query).Result.Distinct().ToArray();
        
        if (relatedResults.Length > 1)
        {
            var relatedTable = new Table() { ShowHeaders = false };
            relatedTable.AddColumn("Title");
            foreach (var relatedResult in relatedResults)
            {
                relatedTable.AddRow(new Markup($"{relatedResult.ReplaceLineEndings(string.Empty).EscapeMarkup()}"));
            }
        
            table.AddRow(new Text("Relates Titles"), relatedTable);
        }

        AnsiConsole.Write(new Padder(table).PadLeft(1).PadRight(0).PadBottom(0).PadTop(1));
        return 0;
    }
}