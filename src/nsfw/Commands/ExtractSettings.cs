using System.ComponentModel;
using Spectre.Console;
using Spectre.Console.Cli;

namespace Nsfw.Commands;

public class ExtractSettings : CommandSettings
{
    [CommandOption("-c|--cert <FILE>")]
    [Description("Path to 0x700-byte long common certificate chain file.")]
    [DefaultValue("~/.switch/common.cert")]
    public string CertFile { get; set; } = string.Empty;
    
    [CommandOption("-k|--keys <FILE>")]
    [Description("Path to NSW keys file.")]
    [DefaultValue("~/.switch/prod.keys")]
    public string KeysFile { get; set; } = string.Empty;
    
    [CommandArgument(0, "<NSP_FILE>")]
    [Description("Path to NSP file.")]
    public string NspFile { get; set; } = string.Empty;
    
    [CommandOption("-o|--outdir <DIR>")]
    [Description("Path to standardised NSP output directory.")]
    [DefaultValue("./out")]
    public string OutDirectory { get; set; } = string.Empty;

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
        
        if (!File.Exists(NspFile))
        {
            return ValidationResult.Error($"NSP file '{NspFile}' does not exist.");
        }

        var filename = Path.GetFileName(NspFile).Replace(Path.GetExtension(NspFile), string.Empty);
        OutDirectory = Path.Combine(OutDirectory, filename);
        
        if(!Directory.Exists(OutDirectory))
        {
            Directory.CreateDirectory(OutDirectory);
        }
        
        return base.Validate();
    }
}