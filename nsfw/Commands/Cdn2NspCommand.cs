using System.Diagnostics.CodeAnalysis;
using Spectre.Console;
using Spectre.Console.Cli;

namespace Nsfw.Commands;

public sealed class Cdn2NspCommand : Command<Cdn2NspSettings>
{
    public override int Execute([NotNull] CommandContext context, [NotNull] Cdn2NspSettings settings)
    {
        //SettingsDumper.Dump(settings);
        
        var metaNcaList = Directory.GetFiles(settings.CdnDirectory, "*.cnmt.nca", SearchOption.AllDirectories);

        if (!metaNcaList.Any())
        {
            AnsiConsole.MarkupLine("[red]Cannot find any CNMT NCA files[/]");
            return 1;
        }

        foreach (var metaNcaFilePath in metaNcaList)
        {
            var metaNcaFileFullPath = Path.GetFullPath(metaNcaFilePath);
            var workingDirectory = Path.GetDirectoryName(metaNcaFileFullPath) ?? string.Empty;
            
            if (!Directory.Exists(workingDirectory) && !File.Exists(metaNcaFileFullPath))
            {
                AnsiConsole.MarkupLine("[red]Cannot find CDN directory or file[/]");
                return 1;
            }
            
            var cdn2NspService = new Cdn2NspService(settings);
            var result = cdn2NspService.Process(workingDirectory, Path.GetFileName(metaNcaFileFullPath));
            
            if (result != 0)
            {
                return result;
            }
        }

        return 0;
    }
}