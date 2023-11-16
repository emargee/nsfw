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
    
    [CommandOption("-s|--check-shas")]
    [Description("Check SHA256 of all files in CDN directory and compare with CNMT hashes.")]
    public bool CheckShas { get; set; }
    
    [CommandOption("-v|--verbose")]
    [Description("Enable verbose output.")]
    public bool Verbose { get; set; }
    
    [CommandOption("-d|--dryrun")]
    [Description("Process files but do not generate NSP.")]
    public bool DryRun { get; set; }

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
            return ValidationResult.Error($"Output directory '{OutDirectory}' does not exist.");
        }
        
        if(!ValidateCommonCert(CertFile))
        {
            return ValidationResult.Error($"Common cert '{CertFile}' is invalid.");
        }

        return base.Validate();
    }
    
    bool ValidateCommonCert(string certPath)
    {
        var commonCertSize = 0x700;
        var commonCertSha256 = "3c4f20dca231655e90c75b3e9689e4dd38135401029ab1f2ea32d1c2573f1dfe";

        var fileBytes = File.ReadAllBytes(certPath);
        
        if(fileBytes.Length != commonCertSize)
        {
            AnsiConsole.WriteLine("Common cert is invalid size");
            return false;
        }
        
        var certSha256 = SHA256.HashData(fileBytes).ToHexString();

        return certSha256 == commonCertSha256.ToUpperInvariant();
    }
}