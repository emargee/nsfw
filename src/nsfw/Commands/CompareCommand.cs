using System.ComponentModel;
using Serilog;
using Serilog.Events;
using Serilog.Sinks.Spectre;
using Spectre.Console;
using Spectre.Console.Cli;

namespace Nsfw.Commands;

public class CompareCommand : Command<CompareSettings>
{
    public override int Execute(CommandContext context, CompareSettings settings)
    {
        // var logLevel = LogEventLevel.Verbose;
        //
        // Log.Logger = new LoggerConfiguration()
        //     .MinimumLevel.Is(logLevel)
        //     .WriteTo.Spectre(outputTemplate: "[{Level:u3}] {Message:lj} {NewLine}{Exception}{Elapsed}")
        //     .CreateLogger();
        
        AnsiConsole.Write(new Rule($"[[[blue]N$FW[/]]][[{Program.GetVersion()}]]").LeftJustified());
        AnsiConsole.MarkupLine($"File One: [olive]{settings.NspFileOne.EscapeMarkup()}[/]");
        AnsiConsole.MarkupLine($"File Two: [olive]{settings.NspFileTwo.EscapeMarkup()}[/]");
        AnsiConsole.Write(new Rule());
        
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
        AnsiConsole.MarkupLine("[[[olive]INF[/]]] Loading NSP files...");
        var resultOne = validateNspService.Process(settings.NspFileOne, false, true);
        var resultTwo = validateNspService.Process(settings.NspFileTwo, false, true);
        
        if(resultOne.returnValue != 0 || resultTwo.returnValue != 0 || resultOne.nspInfo == null || resultTwo.nspInfo == null)
        {
            AnsiConsole.MarkupLine("[[[red]ERR[/]]] One or more NSPs failed validation.");
            return 1;
        }
        
        var nspOne = resultOne.nspInfo;
        var nspTwo = resultTwo.nspInfo;
        
         // Check both are standard NSPs

        if(!nspOne.IsStandardNsp || !nspTwo.IsStandardNsp)
        {
            AnsiConsole.MarkupLine("[[[red]ERR[/]]] One or more NSPs are not standard NSPs.");
            AnsiConsole.Write(RenderUtilities.RenderResultTable("Standard NSP", nspOne.IsStandardNsp.ToString(), nspTwo.IsStandardNsp.ToString()));
            return 1;
        }

        if (nspOne.NcaFiles.Count != nspTwo.NcaFiles.Count)
        {
            AnsiConsole.MarkupLine("[[[red]ERR[/]]] Files have a different number of NCAs.");
            AnsiConsole.Write(RenderUtilities.RenderResultTable("NCA Count", nspOne.NcaFiles.Count.ToString(), nspTwo.NcaFiles.Count.ToString()));
            return 1;
        }
        
        var inFirstOnly = nspOne.RawFileEntries.Keys.Except(nspTwo.RawFileEntries.Keys).ToArray();
        var inSecondOnly = nspTwo.RawFileEntries.Keys.Except(nspOne.RawFileEntries.Keys).ToArray();
        var allInBoth = inFirstOnly.Length == 0 && inSecondOnly.Length == 0;

        if (!allInBoth)
        {
            AnsiConsole.MarkupLine("[[[red]ERR[/]]] Files are mis-matched ...");
            AnsiConsole.Write(RenderUtilities.RenderResultTable("Missing File", RenderUtilities.RenderRawFilesTree(nspOne.RawFileEntries.Values, inFirstOnly), RenderUtilities.RenderRawFilesTree(nspTwo.RawFileEntries.Values, inSecondOnly)));

            if(inFirstOnly.Length == 1 && inSecondOnly.Length  == 1)
            {
                if(inFirstOnly[0].EndsWith(".cnmt.nca") && inSecondOnly[0].EndsWith(".cnmt.nca"))
                {
                    var metaOne = nspOne.NcaFiles.Values.FirstOrDefault(x => x.FileName == inFirstOnly[0]);
                    var metaTwo = nspTwo.NcaFiles.Values.FirstOrDefault(x => x.FileName == inSecondOnly[0]);
                    
                    if(metaOne != null && metaTwo != null)
                    {
                        AnsiConsole.MarkupLine("[[[red]ERR[/]]] Possible mis-match with encryption keys ...");
                        
                        var ncaTreeOne = new Tree(metaOne.FileName)
                        {
                            Expanded = true,
                        };
                        ncaTreeOne.AddNode(RenderUtilities.RenderNcaKeys(metaOne, metaTwo));
                        
                        var ncaTreeTwo = new Tree(metaTwo.FileName)
                        {
                            Expanded = true,
                        };
                        ncaTreeTwo.AddNode(RenderUtilities.RenderNcaKeys(metaTwo, metaOne));
                        
                        AnsiConsole.Write(RenderUtilities.RenderResultTable("NCA Keys", ncaTreeOne, ncaTreeTwo));
                    }
                }
            }
        }
        
        foreach (var rawEntry in nspOne.RawFileEntries)
        {
            if (nspTwo.RawFileEntries.TryGetValue(rawEntry.Key, out var rawEntryTwo))
            {
                if (rawEntry.Value.Size != rawEntryTwo.Size)
                {
                    Log.Error($"File [[{nspTwo.FileName.EscapeMarkup()}]] has a different size for file [[{rawEntry.Key.EscapeMarkup()}]].");
                    AnsiConsole.Write(RenderUtilities.RenderResultTable("File Size", rawEntry.Value.Size.ToString(), rawEntryTwo.Size.ToString()));
                }
            }
        }
        
        
        

        // if (resultOne is { returnValue: 0, nspInfo: not null })
        // {
        //     AnsiConsole.Write(new Padder(RenderUtilities.RenderProperties(resultOne.nspInfo, "")).PadLeft(1).PadTop(1).PadBottom(1));
        // }
        //
        //
        //
        // if (resultTwo is { returnValue: 0, nspInfo: not null })
        // {
        //     AnsiConsole.Write(new Padder(RenderUtilities.RenderProperties(resultTwo.nspInfo, "")).PadLeft(1).PadTop(1).PadBottom(1));
        // }
        
        return 0;
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