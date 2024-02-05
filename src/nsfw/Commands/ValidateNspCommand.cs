using System.Diagnostics.CodeAnalysis;
using Nsfw.Nsp;
using Serilog;
using Serilog.Events;
using Serilog.Sinks.Spectre;
using Spectre.Console;
using Spectre.Console.Cli;

namespace Nsfw.Commands;

public class ValidateNspCommand : Command<ValidateNspSettings>
{
    public override int Execute([NotNull] CommandContext context, [NotNull] ValidateNspSettings settings)
    {
        (int, NspInfo?) result = (0, null);
        var logLevel = LogEventLevel.Verbose;

        if (settings.NspCollection.Length != 0)
        {
            settings.IsQuiet = true;
        }
        
        if (settings.LogLevel == LogLevel.Quiet)
        {
            logLevel = LogEventLevel.Information;
        }
        
        Log.Logger = new LoggerConfiguration()
            .MinimumLevel.Is(logLevel)
            .WriteTo.Spectre(outputTemplate: "[{Level:u3}] {Message:lj} {NewLine}{Exception}{Elapsed}")
            .CreateLogger();
        
        if (settings.NspCollection.Length != 0)
        {
            var count = 1;
            var fileList = settings.NspCollection;

            if (settings is { Batch: > 0, Skip: > 0 })
            {
                fileList = fileList.Skip(settings.Skip).Take(settings.Batch).ToArray();
            }
            else if (settings.Batch > 0)
            {
                fileList = fileList.Take(settings.Batch).ToArray();
            }
            else if (settings.Skip > 0)
            {
                fileList = fileList.Skip(settings.Skip).ToArray();
            }
            
            var total = fileList.Length;
            
            DrawLogo();
            AnsiConsole.MarkupLine($"-[[ Processing {total} NSPs ..");
            AnsiConsole.Write(new Rule());
            
            foreach (var nsp in fileList)
            {
                var service = new ValidateNspService(settings);
                result = service.Process(nsp,true);
                AnsiConsole.Write(new Rule($"[[{count}/{total}]]"));
                count++;
            }
        }
        else
        {
            if (settings.LogLevel != LogLevel.Quiet)
            {
                DrawLogo();
            }
            else
            {
                AnsiConsole.Write(new Rule($"[[[blue]N$FW[/]]][[{Program.GetVersion()}]]").LeftJustified());
            }
        
            var service = new ValidateNspService(settings);
            result = service.Process(settings.NspFile, false);
            AnsiConsole.Write(new Rule());
        }

        return result.Item1;
    }

    private static void DrawLogo()
    {
        AnsiConsole.MarkupLine(@"                        _______      ______");
        AnsiConsole.MarkupLine(@"       _______/\  _____/      /_____/     /_  /\______");
        AnsiConsole.MarkupLine(@"    __/   __    \/   __      /   __      / /[grey]\[/]/       /[grey]\[/]");
        AnsiConsole.MarkupLine(@"   /       /    /     /______\    /_____/ /\/       /[grey] /[/]");
        AnsiConsole.MarkupLine(@"[olive]==[/]/       /    /______    \      ____/           __/[grey] /[/][olive]=[/]");
        AnsiConsole.MarkupLine(@"[olive]=[/]/       /    /      /     \    /        /      /[grey]\_\/[/][olive]==[/]");
        AnsiConsole.MarkupLine(@"/       /____/       ______/___/        /\_____/ [grey]/[/][olive]=====[/]  ");
        AnsiConsole.MarkupLine(@"\______/[grey]\____[/]\______/[grey]\_____\___[/]\_______/ [grey]/_____\/[/]");
        AnsiConsole.MarkupLine($@"[grey] \_____\/     \_____\/          \______\/[/]");
        AnsiConsole.Write(new Rule($"[[{Program.GetVersion()}]]").RightJustified());
    }
}
