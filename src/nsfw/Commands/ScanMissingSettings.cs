using System.ComponentModel;
using Spectre.Console;
using Spectre.Console.Cli;

namespace Nsfw.Commands;

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