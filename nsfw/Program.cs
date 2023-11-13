﻿using Nsfw.Commands;
using Spectre.Console;
using Spectre.Console.Cli;

public static class Program
{
    public static int Main(string[] args)
    {
        var app = new CommandApp();
        app.Configure(config =>
        {
            config.SetApplicationName("nsfw");
            config.ValidateExamples();

            config.AddCommand<Cdn2NspCommand>("cdn2nsp")
                .WithDescription("Deterministically recreates NSP files from extracted CDN data following nxdumptool NSP generation guidelines.")
                .WithAlias("c2n");
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