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
    
    [CommandArgument(0, "<NSZ_FILE>")]
    [Description("Path to NSZ file.")]
    public string NszFile { get; set; } = string.Empty;
    
    [CommandOption("-o|--outdir <DIR>")]
    [Description("Path to standardised NSP output directory.")]
    [DefaultValue("./out")]
    public string OutDirectory { get; set; } = string.Empty;
    
    [CommandOption("-x|--extract")]
    [Description("Extract contents of NSZ file.")]
    public bool Extract { get; set; }

    public override ValidationResult Validate()
    {
        if (KeysFile.StartsWith('~'))
        {
            KeysFile = KeysFile.Replace("~", Environment.GetFolderPath(Environment.SpecialFolder.UserProfile));
        }
        
        if(NszFile.StartsWith('~'))
        {
            NszFile = NszFile.Replace("~", Environment.GetFolderPath(Environment.SpecialFolder.UserProfile));
        }
        
        if(CertFile.StartsWith('~'))
        {
            CertFile = CertFile.Replace("~", Environment.GetFolderPath(Environment.SpecialFolder.UserProfile));
        }
        
        if (!File.Exists(NszFile))
        {
            return ValidationResult.Error($"NSZ file '{NszFile}' does not exist.");
        }
        
        if(Path.GetExtension(NszFile) != ".nsz")
        {
            return ValidationResult.Error($"NSZ file '{NszFile}' is not a NSZ file.");
        }

        var filename = Path.GetFileName(NszFile).Replace(Path.GetExtension(NszFile), string.Empty);
        OutDirectory = Path.Combine(OutDirectory, filename);
        
        if(!Directory.Exists(OutDirectory))
        {
            Directory.CreateDirectory(OutDirectory);
        }
        
        return base.Validate();
    }
}