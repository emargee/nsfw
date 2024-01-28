using System.Diagnostics.CodeAnalysis;
using Serilog;
using Spectre.Console;
using Spectre.Console.Cli;

namespace Nsfw.Commands;

public sealed class Cdn2NspCommand : Command<Cdn2NspSettings>
{
    public override int Execute([NotNull] CommandContext context, [NotNull] Cdn2NspSettings settings)
    {
        foreach (var directory in Directory.EnumerateDirectories(settings.CdnDirectory))
        {
            AnsiConsole.WriteLine("----------------------------------------");            
            var files = Directory.EnumerateFiles(directory, "*.cnmt.nca", SearchOption.TopDirectoryOnly).ToArray();
            
            if (files.Length == 0)
            {
                Console.Write(directory);
                AnsiConsole.MarkupLine(" -> [red]Cannot find any CNMT NCA files. Skipping.[/]");
                continue;
            }
            
            if (files.Length > 1)
            {
                Console.Write(directory);
                AnsiConsole.MarkupLine(" -> [red]Multiple CNMTs found in CDN directory. Skipping.[/]");
                continue;
            }
            
            var metaNcaFileFullPath = Path.GetFullPath(files.First());
            var workingDirectory = Path.GetDirectoryName(metaNcaFileFullPath) ?? string.Empty;
            
            if (!Directory.Exists(workingDirectory) && !File.Exists(metaNcaFileFullPath))
            {
                Console.Write(directory);
                AnsiConsole.MarkupLine(" -> [red]Cannot find CDN directory or CNMT file. Skipping.[/]");
                continue;
            }
            
            var cdn2NspService = new Cdn2NspService(settings);
            var result = cdn2NspService.Process(workingDirectory, Path.GetFileName(metaNcaFileFullPath));
            
            if (result == 0 && settings.DeleteSource)
            {
                Log.Logger.Information($"Cleaning up : {workingDirectory}");
                Directory.Delete(workingDirectory, true);
            }
            
        }
        
        AnsiConsole.WriteLine("----------------------------------------");

        return 0;
    }
}