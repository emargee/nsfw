using System.ComponentModel;
using System.Security.Cryptography;
using LibHac.Util;
using Spectre.Console;
using Spectre.Console.Cli;

namespace Nsfw.Commands;

public sealed class Cdn2NspSettings : CommandSettings
{
    [CommandOption("-i|--cdndir <DIR>")]
    [Description("Path to directory with extracted CDN data (will be processed recursively).")]
    [DefaultValue("./cdn")]
    public string CdnDirectory { get; set; } = string.Empty;

    [CommandOption("-k|--keys <FILE>")]
    [Description("Path to NSW keys file.")]
    [DefaultValue("~/.switch/prod.keys")]
    public string KeysFile { get; set; } = string.Empty;

    [CommandOption("-c|--cert <FILE>")]
    [Description("Path to 0x700-byte long common certificate chain file.")]
    [DefaultValue("~/.switch/common.cert")]
    public string CertFile { get; set; } = string.Empty;

    [CommandOption("-o|--outdir <DIR>")]
    [Description("Path to output directory.")]
    [DefaultValue("./out")]
    public string OutDirectory { get; set; } = string.Empty;
    
    [CommandOption("-d|--dryrun")]
    [Description("Process files but do not generate NSP.")]
    public bool DryRun { get; set; }
    
    [CommandOption("-Z|--delete-source")]
    [Description("Delete source files after processing.")]
    public bool DeleteSource { get; set; }

    public override ValidationResult Validate()
    {
        CdnDirectory = CdnDirectory.Replace("~", Environment.GetFolderPath(Environment.SpecialFolder.UserProfile));
        KeysFile = KeysFile.Replace("~", Environment.GetFolderPath(Environment.SpecialFolder.UserProfile));
        CertFile = CertFile.Replace("~", Environment.GetFolderPath(Environment.SpecialFolder.UserProfile));
        OutDirectory = OutDirectory.Replace("~", Environment.GetFolderPath(Environment.SpecialFolder.UserProfile));

        if (!Directory.Exists(CdnDirectory))
        {
            return ValidationResult.Error($"CDN directory '{CdnDirectory}' does not exist.");
        }

        if (!File.Exists(KeysFile))
        {
            return ValidationResult.Error($"Keys file '{KeysFile}' does not exist.");
        }

        if (!File.Exists(CertFile))
        {
            return ValidationResult.Error($"Certificate file '{CertFile}' does not exist.");
        }

        if (!Directory.Exists(OutDirectory))
        {
            Directory.CreateDirectory(OutDirectory);
        }
        
        if(!Nsp.NsfwUtilities.ValidateCommonCert(CertFile))
        {
            return ValidationResult.Error($"Common cert '{CertFile}' is invalid.");
        }

        return base.Validate();
    }
    

}