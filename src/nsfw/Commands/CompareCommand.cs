using System.ComponentModel;
using Spectre.Console;
using Spectre.Console.Cli;

namespace Nsfw.Commands;

public class CompareCommand : Command<CompareSettings>
{
    public override int Execute(CommandContext context, CompareSettings settings)
    {
        Console.WriteLine(settings.NspFileOne);
        Console.WriteLine(settings.NspFileTwo);
        
        var nspSettings = new ValidateNspSettings
        {
            KeysFile = settings.KeysFile,
            CertFile = settings.CertFile,
            IsQuiet = true,
            VerifyTitle = true,
            TitleDbFile = "./titledb/titledb.db",
            ShortLanguages = true
        };

        var validateNspService = new ValidateNspService(nspSettings);
        var resultOne = validateNspService.Process(settings.NspFileOne, false, true);

        if (resultOne is { returnValue: 0, nspInfo: not null })
        {
            AnsiConsole.Write(new Padder(RenderUtilities.RenderProperties(resultOne.nspInfo, "")).PadLeft(1).PadTop(1).PadBottom(1));
        }
        
        var resultTwo = validateNspService.Process(settings.NspFileTwo, false, true);

        if (resultTwo is { returnValue: 0, nspInfo: not null })
        {
            AnsiConsole.Write(new Padder(RenderUtilities.RenderProperties(resultTwo.nspInfo, "")).PadLeft(1).PadTop(1).PadBottom(1));
        }
        
        return resultOne.returnValue;
    }
}

public class CompareSettings : CommandSettings
{
    [CommandOption("-k|--keys <FILE>")]
    [Description("Path to NSW keys file.")]
    [DefaultValue("~/.switch/prod.keys")]
    public string KeysFile { get; set; } = string.Empty;

    [CommandOption("-c|--cert <FILE>")]
    [Description("Path to 0x700-byte long common certificate chain file.")]
    [DefaultValue("~/.switch/common.cert")]
    public string CertFile { get; set; } = string.Empty;

    [CommandArgument(0, "<NSP_FILE_ONE>")]
    [Description("First file to compare.")]
    public string NspFileOne { get; set; } = string.Empty;
    
    [CommandArgument(1, "<NSP_FILE_TWO>")]
    [Description("Second file to compare.")]
    public string NspFileTwo { get; set; } = string.Empty;

    public override ValidationResult Validate()
    {
        KeysFile = KeysFile.Replace("~", Environment.GetFolderPath(Environment.SpecialFolder.UserProfile));
        CertFile = CertFile.Replace("~", Environment.GetFolderPath(Environment.SpecialFolder.UserProfile));
        
        if (!File.Exists(KeysFile))
        {
            return ValidationResult.Error($"Keys file '{KeysFile}' does not exist.");
        }

        if (!File.Exists(CertFile))
        {
            return ValidationResult.Error($"Certificate file '{CertFile}' does not exist.");
        }
        
        if(!File.Exists(NspFileOne))
        {
            return ValidationResult.Error($"NSP file '{NspFileOne}' does not exist.");
        }
        
        if(!File.Exists(NspFileTwo))
        {
            return ValidationResult.Error($"NSP file '{NspFileTwo}' does not exist.");
        }
        
        return base.Validate();
    }
}