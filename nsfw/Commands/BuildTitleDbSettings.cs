using System.ComponentModel;
using Spectre.Console;
using Spectre.Console.Cli;

namespace Nsfw.Commands;

public class BuildTitleDbSettings : CommandSettings
{
    [CommandOption("--titledbdir <DIR>")]
    [Description("Path to TitleDB jsons directory.")]
    [DefaultValue("./titledb")]
    public string TitleDbDirectory { get; set; } = string.Empty;
    
    [CommandOption("-c|--clean")]
    [Description("Clean database before rebuilding.")]
    public bool CleanDatabase { get; set; }

    public override ValidationResult Validate()
    {
        TitleDbDirectory = TitleDbDirectory.Replace("~", Environment.GetFolderPath(Environment.SpecialFolder.UserProfile));
        TitleDbDirectory = Path.GetFullPath(TitleDbDirectory);

        if (!Directory.Exists(TitleDbDirectory))
        {
            return ValidationResult.Error($"TitleDB directory {TitleDbDirectory} does not exist.");
        }
        
        if(!File.Exists(Path.Combine(TitleDbDirectory, "languages.json")))
        {
            return ValidationResult.Error($"TitleDB directory {TitleDbDirectory} does not contain languages.json.");
        }
        
        return base.Validate();
    }
}