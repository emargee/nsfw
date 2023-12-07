using System.Diagnostics.CodeAnalysis;
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
        var result = 0;
        var logLevel = LogEventLevel.Verbose;

        if (settings.NspCollection.Length != 0)
        {
            settings.LogLevel = LogLevel.Quiet;
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
            DrawLogo();
            AnsiConsole.MarkupLine($"-[[ Processing {settings.NspCollection.Length:000} NSPs ]]----------------");
            AnsiConsole.MarkupLine("----------------------------------------");
            foreach (var nsp in settings.NspCollection)
            {
                var service = new ValidateNspService(settings);
                result = service.Process(nsp);
                AnsiConsole.MarkupLine("----------------------------------------");
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
                AnsiConsole.MarkupLine("---------------------------------[[[blue]N$FW[/]]]--");
            }
        
            var service = new ValidateNspService(settings);
            result = service.Process(settings.NspFile);
        }

        return result;
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
        AnsiConsole.MarkupLine(@"[grey] \_____\/     \_____\/          \______\/[/]");
        AnsiConsole.MarkupLine("----------------------------------------");
    }
}
