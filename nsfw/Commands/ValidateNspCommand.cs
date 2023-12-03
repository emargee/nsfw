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
            AnsiConsole.WriteLine(@"                        _______      ______");
            AnsiConsole.WriteLine(@"       _______/\  _____/      /_____/     /_  /\______");
            AnsiConsole.WriteLine(@"    __/   __    \/   __      /    _      / /\/       /\");
            AnsiConsole.WriteLine(@"   /       /    /     /_____/     /_____/ /\/       / /");
            AnsiConsole.WriteLine(@"-=/       /    /______    \      ____/           __/ /=");
            AnsiConsole.WriteLine(@"=/       /    /      /    /     /        /      /\_\/=-");
            AnsiConsole.WriteLine(@"/       /____/       ____/_____/        /\_____/ /  ");
            AnsiConsole.WriteLine(@"\______/\____\______/\___\_____\_______/ [nsfw]\/");
            AnsiConsole.WriteLine(@" \_____\/     \_____\/          \______\/");
            AnsiConsole.WriteLine("----------------------------------------");
        }

        var service = new ValidateNspService(settings);
        var result = service.Process(settings.NspFile);
        AnsiConsole.WriteLine("----------------------------------------");
        return result;
    }
}
