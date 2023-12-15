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
    
    [CommandOption("--titledb <FILE>")]
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
    
    [CommandOption("--updates")]
    [Description("Print title update versions.")]
    public bool Updates { get; set; }
    
    [CommandOption("--nl")]
    [Description("Do not print languages in output filename.")]
    public bool NoLanguages { get; set; }
    
    [CommandOption("--sl")]
    [Description("Use short language codes in output filename.")]
    public bool ShortLanguages { get; set; }
    
    [CommandOption("--skip-hash")]
    [Description("When re-naming files, skip NCA hash validation.")]
    public bool SkipHash { get; set; }
    
    [CommandOption("--quiet")]
    [Description("Set output level to 'quiet'. Minimal display for details.")]
    public bool IsQuiet { get; set; }
    
    [CommandOption("--full")]
    [Description("Set output level to 'full'. Full break-down on NSP structure.")]
    public bool IsFull { get; set; }
    
    [CommandOption("-t|--ticketinfo")]
    [Description("Print ticket info.")]
    public bool TicketInfo { get; set; }
    
    [CommandOption("--force-hash")]
    [Description("Force hash verification of bad NCA files.")]
    public bool ForceHash { get; set; }
    
    [CommandOption("--extract-all")]
    [Description("Extract all files from NSP.")]
    public bool ExtractAll { get; set; }
    
    [CommandArgument(0, "<NSP_FILE>")]
    [Description("Path to NSP file.")]
    public string NspFile { get; set; } = string.Empty;

    public LogLevel LogLevel
    {
        get
        {
            if (IsQuiet)
            {
                return LogLevel.Quiet;
            }

            if (IsFull)
            {
                return LogLevel.Full;
            }

            return LogLevel.Compact;
        }
    }
    
    public string[] NspCollection { get; set; } = [];

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
        
        var attr = File.GetAttributes(NspFile);
        
        if(attr.HasFlag(FileAttributes.Directory))
        {
            NspCollection = Directory.EnumerateFiles(NspFile, "*.nsp").ToArray();
        }
        else
        {
            if (!File.Exists(NspFile))
            {
                return ValidationResult.Error($"NSP file '{NspFile}' does not exist.");
            }
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
            Directory.CreateDirectory(CdnDirectory);
        }
        
        if(Convert && !Directory.Exists(NspDirectory))
        {
            Directory.CreateDirectory(NspDirectory);
        }
        
        return base.Validate();
    }
}