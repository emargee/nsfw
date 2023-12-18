using System.ComponentModel;
using Spectre.Console;
using Spectre.Console.Cli;

namespace Nsfw.Commands;

public class QuerySettings : CommandSettings
{
    [CommandOption("--titledb <FILE>")]
    [Description("Path to titledb.db file.")]
    [DefaultValue("./titledb/titledb.db")]
    public string TitleDbFile { get; set; } = string.Empty;
    
    [CommandArgument(0, "<TITLEID>")]
    [Description("Title ID to search for.")]
    public string Query { get; set; } = string.Empty;
    
    public override ValidationResult Validate()
    {
        if (string.IsNullOrWhiteSpace(Query))
        {   
            return ValidationResult.Error("Title ID is required.");
        }
        
        if(Query.Length != 16)
        {
            return ValidationResult.Error("Title ID must be 16 characters long.");
        }
        
        if(TitleDbFile.StartsWith('~'))
        {
            TitleDbFile = TitleDbFile.Replace("~", Environment.GetFolderPath(Environment.SpecialFolder.UserProfile));
        }
        
        return base.Validate();
    }
}