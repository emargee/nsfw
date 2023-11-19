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
    
    [CommandOption("-o|--outdir <DIR>")]
    [Description("Path to output directory.")]
    [DefaultValue("./cdn")]
    public string OutDirectory { get; set; } = string.Empty;
    
    [CommandOption("-d|--dryrun")]
    [Description("Print files but do not generate NSP.")]
    public bool DryRun { get; set; }
    
    [CommandArgument(0, "<NSP_FILE>")]
    [Description("Path to NSP file.")]
    public string NspFile { get; set; } = string.Empty;

    public override ValidationResult Validate()
    {
        KeysFile = KeysFile.Replace("~", Environment.GetFolderPath(Environment.SpecialFolder.UserProfile));
        NspFile = NspFile.Replace("~", Environment.GetFolderPath(Environment.SpecialFolder.UserProfile));
        CertFile = CertFile.Replace("~", Environment.GetFolderPath(Environment.SpecialFolder.UserProfile));
        OutDirectory = OutDirectory.Replace("~", Environment.GetFolderPath(Environment.SpecialFolder.UserProfile));
        OutDirectory = Path.GetFullPath(OutDirectory);
        
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
        
        if(!Directory.Exists(OutDirectory))
        {
            return ValidationResult.Error($"Output directory '{OutDirectory}' does not exist.");
        }
        
        return base.Validate();
    }
}