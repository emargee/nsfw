using System.Diagnostics.CodeAnalysis;
using Spectre.Console;
using Spectre.Console.Cli;

namespace Nsfw.Commands;

public class ValidateNspCommand : Command<ValidateNspSettings>
{
    public override int Execute([NotNull] CommandContext context, [NotNull] ValidateNspSettings settings)
    {
        if (!settings.Quiet)
        {
            AnsiConsole.MarkupLine(@"                        _______      ______");
            AnsiConsole.MarkupLine(@"       _______/\  _____/      /_____/     /_  /\______");
            AnsiConsole.MarkupLine(@"    __/   __    \/   __      /   __      / /[grey]\[/]/       /[grey]\[/]");
            AnsiConsole.MarkupLine(@"   /       /    /     /______\    /_____/ /\/       /[grey] /[/]");
            AnsiConsole.MarkupLine(@"[olive]==[/]/       /    /______    \      ____/           __/[grey] /[/][olive]=[/]");
            AnsiConsole.MarkupLine(@"[olive]=[/]/       /    /      /     \    /        /      /[grey]\_\/[/][olive]==[/]");
            AnsiConsole.MarkupLine(@"/       /____/       ______/___/        /\_____/ [grey]/[/][olive]=====[/]  ");
            AnsiConsole.MarkupLine(@"\______/[grey]\____[/]\______/[grey]\_____\___[/]\_______/ [grey]/[[mRg]]\/[/]");
            AnsiConsole.MarkupLine(@"[grey] \_____\/     \_____\/          \______\/[/]");
            AnsiConsole.MarkupLine("----------------------------------------");
        }

        var service = new ValidateNspService(settings);
        var result = service.Process(settings.NspFile);
        AnsiConsole.WriteLine("----------------------------------------");
        return result;
    }
}
