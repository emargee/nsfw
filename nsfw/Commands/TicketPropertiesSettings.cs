using System.ComponentModel;
using Spectre.Console;
using Spectre.Console.Cli;

namespace Nsfw.Commands;

public class TicketPropertiesSettings : CommandSettings
{
    [CommandOption("-c|--cert <FILE>")]
    [Description("Path to 0x700-byte long common certificate chain file.")]
    [DefaultValue("~/.switch/common.cert")]
    public string CertFile { get; set; } = string.Empty;
    
    [CommandArgument(0, "<TIK_FILE>")]
    [Description("Path to tik file.")]
    public string TicketFile { get; set; } = string.Empty;

    public override ValidationResult Validate()
    {
        if(CertFile.StartsWith('~'))
        {
            CertFile = CertFile.Replace("~", Environment.GetFolderPath(Environment.SpecialFolder.UserProfile));
        }
        
        if (!File.Exists(CertFile))
        {
            return ValidationResult.Error($"Certificate file '{CertFile}' does not exist.");
        }
        
        if(!File.Exists(TicketFile))
        {
            return ValidationResult.Error($"Ticket file '{TicketFile}' does not exist.");
        }
        
        if(!NsfwUtilities.ValidateCommonCert(CertFile))
        {
            return ValidationResult.Error($"Common cert '{CertFile}' is invalid.");
        }
        
        return base.Validate();
    }
    
}