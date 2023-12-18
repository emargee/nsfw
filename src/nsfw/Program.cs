using System.Reflection;
using System.Text;
using Nsfw.Commands;
using Spectre.Console.Cli;

// ReSharper disable once CheckNamespace
public static class Program
{
    public static string GetVersion()
    {
        var pv = Assembly.GetEntryAssembly()?.GetName().Version;
        return pv != null ? $"v{pv.Major}.{pv.Minor}.{pv.Build}" : "0.0.0";
    }
    
    public static int Main(string[] args)
    {
        Console.OutputEncoding = Encoding.UTF8;
        var app = new CommandApp();
        app.Configure(config =>
        {
            config.SetApplicationVersion(GetVersion());
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
            
            config.AddCommand<QueryCommand>("query")
                .WithDescription("Query TitleDB for Title ID.")
                .WithAlias("q");
            
            config.AddCommand<BuildTitleDbCommand>("build-titledb")
                .WithDescription("Builds TitleDB from NSP files.")
                .WithAlias("btdb");
        });

        return app.Run(args);
    }
}