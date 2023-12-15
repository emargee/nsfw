using System.ComponentModel;
using Spectre.Console;
using Spectre.Console.Cli;

namespace Nsfw.Commands;

public class MetaPropertiesSettings : CommandSettings
{
    [CommandArgument(0, "<META_NCA_FILE>")]
    [Description("Path to CNMT NCA file.")]
    public string CnmtFile { get; set; } = string.Empty;
    
    [CommandOption("-k|--keys <FILE>")]
    [Description("Path to NSW keys file.")]
    [DefaultValue("~/.switch/prod.keys")]
    public string KeysFile { get; set; } = string.Empty;
    
    public override ValidationResult Validate()
    {
        if(CnmtFile.StartsWith('~'))
        {
            CnmtFile = CnmtFile.Replace("~", Environment.GetFolderPath(Environment.SpecialFolder.UserProfile));
        }
        
        if (KeysFile.StartsWith('~'))
        {
            KeysFile = KeysFile.Replace("~", Environment.GetFolderPath(Environment.SpecialFolder.UserProfile));
        }
        
        if (!File.Exists(CnmtFile))
        {
            return ValidationResult.Error($"CNMT NCA File '{CnmtFile}' does not exist.");
        }
        
        if (!File.Exists(KeysFile))
        {
            return ValidationResult.Error($"Keys file '{KeysFile}' does not exist.");
        }
        
        return base.Validate();
    }
}