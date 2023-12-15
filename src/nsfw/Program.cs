using System.Diagnostics;
using System.Reflection;
using System.Text;
using Microsoft.Extensions.Logging;
using Nsfw.Commands;
using Serilog;
using Serilog.Sinks.Spectre;
using Spectre.Console;
using Spectre.Console.Cli;

public static class Program
{
    public static int Main(string[] args)
    {
        Console.OutputEncoding = Encoding.UTF8;
        var app = new CommandApp();
        app.Configure(config =>
        {
            var pv = FileVersionInfo.GetVersionInfo(Assembly.GetExecutingAssembly().Location);
            config.SetApplicationVersion($"{pv.FileMajorPart}.{pv.FileMinorPart}.{pv.FileBuildPart}");
            config.SetApplicationName("nsfw");
            config.ValidateExamples();

            config.AddCommand<Cdn2NspCommand>("cdn2nsp")
                .WithDescription("Deterministically recreates NSP files from extracted CDN data following nxdumptool NSP generation guidelines.")
                .IsHidden()
                .WithAlias("c2n");
            
            config.AddCommand<ValidateNspCommand>("validate")
                .WithDescription("Validates NSP.")
                .WithAlias("v");
            
            config.AddCommand<TicketPropertiesCommand>("ticket")
                .WithDescription("Read & print ticket properties from Ticket file.")
                .WithAlias("t");
            
            config.AddCommand<MetaPropertiesCommand>("cnmt")
                .WithDescription("Reads & print properties from CNMT NCA file.")
                .WithAlias("m");
            
            config.AddCommand<BuildTitleDbCommand>("build-titledb")
                .WithDescription("Builds TitleDB from NSP files.")
                .WithAlias("btdb");
        });

        return app.Run(args);
    }
}

public static class SettingsDumper
{
    public static void Dump(object settings)
    {
        var table = new Table();
        table.AddColumn("Property");
        table.AddColumn("Value");

        foreach (var property in settings.GetType().GetProperties())
        {
            table.AddRow(property.Name, property.GetValue(settings)?.ToString() ?? "null");
        }

        AnsiConsole.Write(table);
    }
}