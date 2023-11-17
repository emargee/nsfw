using System.Diagnostics.CodeAnalysis;
using Spectre.Console;
using Spectre.Console.Cli;

namespace Nsfw.Commands;

public class ValidateNspCommand : Command<ValidateNspSettings>
{
    public override int Execute([NotNull] CommandContext context, [NotNull] ValidateNspSettings settings)
    {
        AnsiConsole.WriteLine("----------------------------------------");
        var service = new ValidateNspService(settings);
        var result = service.Process(settings.NspFile);
        AnsiConsole.WriteLine("----------------------------------------");
        return result;
    }
}
