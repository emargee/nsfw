using System.ComponentModel;
using Spectre.Console;
using Spectre.Console.Cli;

namespace Nsfw.Commands;

public class HashSettings : CommandSettings
{

    [CommandOption("-i|--nspdir <DIR>")]
    [Description("Path to standardised NSP input directory.")]
    [DefaultValue("./nsp")]
    public string NspDirectory { get; set; } = string.Empty;
    
    [CommandOption("-o|--output <OUTPUT_DIRECTORY>")]
    [Description("Output directory to save completed DAT.")]
    public string OutputDirectory { get; set; } = string.Empty;
    
    [CommandOption("-n|--datname")]
    [Description("Name of DAT file otherwise will be named with timestamp.")]
    public string? DatName { get; set; } = string.Empty;
    
    [CommandOption("--overwrite")]
    [Description("Overwrite existing DAT file. Do not append to existing.")]
    public bool Overwrite { get; set; }
    
    [CommandOption("-b|--batch")]
    [Description("Batch mode. Hash a numbered batch and then exit.")]
    public int Batch { get; set; } = 0;
    
    [CommandOption("-d|--dryrun")]
    [Description("Prints actions to be performed but does not execute any of them.")]
    public bool DryRun { get; set; }
    
    [CommandOption("-a|--all")]
    [Description("Show all output. Otherwise only show updated.")]
    public bool ShowAll { get; set; }
    
    [CommandOption("-s|--skip-hash", IsHidden = true)]
    [Description("Skip hashing. Only fix names on existing entries.")]
    public bool SkipHash { get; set; }
    
    [CommandOption("-l|--latest", IsHidden = true)]
    [Description("Dont skip files modified in the last X hours.")]
    public int Latest { get; set; }
    
    [CommandOption("-z|--one-game-one-update", IsHidden = true)]
    [Description("Only hash one game and one update.")]
    public bool OneGameOneUpdate { get; set; }
    
    [CommandOption("--dlc <FILE>")]
    [Description("Path to NI DLC Dat file.")]
    public string DlcDat { get; set; } = string.Empty;
    
    public bool IsBatchMode => Batch > 0;
    
    public override ValidationResult Validate()
    {
        if (!string.IsNullOrWhiteSpace(DlcDat))
        {
            if(!File.Exists(DlcDat))
            {
                return ValidationResult.Error("DLC Dat file does not exist.");
            }
        }
        
        if(NspDirectory.StartsWith('~'))
        {
            NspDirectory = NspDirectory.Replace("~", Environment.GetFolderPath(Environment.SpecialFolder.UserProfile));
        }
        
        NspDirectory = Path.GetFullPath(NspDirectory);
        
        var attr = File.GetAttributes(NspDirectory);
        
        if(!attr.HasFlag(FileAttributes.Directory) || !Directory.Exists(NspDirectory))
        {
            return ValidationResult.Error($"NSP directory '{NspDirectory}' does not exist.");
        }
        
        return base.Validate();
    }
}