using System.ComponentModel;
using Spectre.Console;
using Spectre.Console.Cli;

namespace Nsfw.Commands;

public class ValidateNspSettings : CommandSettings
{
    [CommandOption("-k|--keys <FILE>")]
    [Description("Path to NSW keys file.")]
    [DefaultValue("~/.switch/prod.keys")]
    public string KeysFile { get; set; } = string.Empty;
    
    [CommandOption("-c|--cert <FILE>")]
    [Description("Path to 0x700-byte long common certificate chain file.")]
    [DefaultValue("~/.switch/common.cert")]
    public string CertFile { get; set; } = string.Empty;
    
    [CommandOption("-x|--extract")]
    [Description("Extract NSP contents to output directory.")]
    public bool Extract { get; set; }
    
    [CommandOption("-s|--standard")]
    [Description("Convert to Standardised NSP")]
    public bool Convert { get; set; }
    
    [CommandOption("-r|--rename")]
    [Description("Rename NSP to match TitleDB. No other actions performed.")]
    public bool Rename { get; set; }
    
    [CommandOption("--nspdir <DIR>")]
    [Description("Path to standardised NSP output directory.")]
    [DefaultValue("./nsp")]
    public string NspDirectory { get; set; } = string.Empty;
    
    [CommandOption("--cdndir <DIR>")]
    [Description("Path to CDN output directory.")]
    [DefaultValue("./cdn")]
    public string CdnDirectory { get; set; } = string.Empty;
    
    [CommandOption("-d|--dryrun")]
    [Description("Print files but do not generate NSP.")]
    public bool DryRun { get; set; }
    
    [CommandOption("-t|--titledb <FILE>")]
    [Description("Path to titledb.db file.")]
    [DefaultValue("./titledb/titledb.db")]
    public string TitleDbFile { get; set; } = string.Empty;
    
    [CommandOption("--verify-title")]
    [Description("Verify title against TitleDB.")]
    public bool VerifyTitle { get; set; }
    
    [CommandOption("--related-titles")]
    [Description("For DLC, print related titles.")]
    public bool RelatedTitles { get; set; }
    
    [CommandOption("--regional-titles")]
    [Description("Print regional title variations.")]
    public bool RegionalTitles { get; set; }
    
    [CommandOption("--versions")]
    [Description("Print title versions.")]
    public bool Versions { get; set; }
    
    [CommandOption("--nl")]
    [Description("Do not print languages in output filename.")]
    public bool NoLanguages { get; set; }
    
    [CommandOption("--sl")]
    [Description("Use short language codes in output filename.")]
    public bool ShortLanguages { get; set; }
    
    [CommandOption("--skip-validation")]
    [Description("When re-naming files, skip NCA validation.")]
    public bool SkipValidation { get; set; }

    [CommandOption("-q|--quiet")]
    [Description("Disable all non-essential output of the program.")]
    public bool Quiet { get; set; }
    
    [CommandArgument(0, "<NSP_FILE>")]
    [Description("Path to NSP file.")]
    public string NspFile { get; set; } = string.Empty;

    public override ValidationResult Validate()
    {
        if (KeysFile.StartsWith('~'))
        {
            KeysFile = KeysFile.Replace("~", Environment.GetFolderPath(Environment.SpecialFolder.UserProfile));
        }
        
        if(NspFile.StartsWith('~'))
        {
            NspFile = NspFile.Replace("~", Environment.GetFolderPath(Environment.SpecialFolder.UserProfile));
        }
        
        if(CertFile.StartsWith('~'))
        {
            CertFile = CertFile.Replace("~", Environment.GetFolderPath(Environment.SpecialFolder.UserProfile));
        }
        
        if(CdnDirectory.StartsWith('~'))
        {
            CdnDirectory = CdnDirectory.Replace("~", Environment.GetFolderPath(Environment.SpecialFolder.UserProfile));
        }
        
        if(NspDirectory.StartsWith('~'))
        {
            NspDirectory = NspDirectory.Replace("~", Environment.GetFolderPath(Environment.SpecialFolder.UserProfile));
        }
        
        if(TitleDbFile.StartsWith('~'))
        {
            TitleDbFile = TitleDbFile.Replace("~", Environment.GetFolderPath(Environment.SpecialFolder.UserProfile));
        }
        
        CdnDirectory = Path.GetFullPath(CdnDirectory);
        NspDirectory = Path.GetFullPath(NspDirectory);
        
        if (!File.Exists(NspFile))
        {
            return ValidationResult.Error($"NSP file '{NspFile}' does not exist.");
        }
        
        if (!File.Exists(KeysFile))
        {
            return ValidationResult.Error($"Keys file '{KeysFile}' does not exist.");
        }
        
        if (!File.Exists(CertFile))
        {
            return ValidationResult.Error($"Certificate file '{CertFile}' does not exist.");
        }
        
        if(Extract && !Directory.Exists(CdnDirectory))
        {
            return ValidationResult.Error($"CDN Output directory '{CdnDirectory}' does not exist.");
        }
        
        if(Convert && !Directory.Exists(NspDirectory))
        {
            return ValidationResult.Error($"NSP Output directory '{NspDirectory}' does not exist.");
        }
        
        return base.Validate();
    }
}