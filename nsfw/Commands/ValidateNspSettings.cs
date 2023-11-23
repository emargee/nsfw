﻿using System.ComponentModel;
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
    [Description("Path to converted titledb file. -> cat US.en.json | jq -r \"[[values[[]]|{id:.id, name:.name,languages:.languages}]]\" > us-en.json")]
    [DefaultValue("./titledb/us-en.json")]
    public string TitleDbFile { get; set; } = string.Empty;
    
    [CommandOption("--verify-title")]
    [Description("Verify title against TitleDB.")]
    public bool VerifyTitle { get; set; }
    
    [CommandOption("--related-titles")]
    [Description("For DLC, print related titles.")]
    public bool RelatedTitles { get; set; }
    
    [CommandArgument(0, "<NSP_FILE>")]
    [Description("Path to NSP file.")]
    public string NspFile { get; set; } = string.Empty;

    public override ValidationResult Validate()
    {
        KeysFile = KeysFile.Replace("~", Environment.GetFolderPath(Environment.SpecialFolder.UserProfile));
        NspFile = NspFile.Replace("~", Environment.GetFolderPath(Environment.SpecialFolder.UserProfile));
        CertFile = CertFile.Replace("~", Environment.GetFolderPath(Environment.SpecialFolder.UserProfile));
        CdnDirectory = CdnDirectory.Replace("~", Environment.GetFolderPath(Environment.SpecialFolder.UserProfile));
        NspDirectory= NspDirectory.Replace("~", Environment.GetFolderPath(Environment.SpecialFolder.UserProfile));
        TitleDbFile= TitleDbFile.Replace("~", Environment.GetFolderPath(Environment.SpecialFolder.UserProfile));
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