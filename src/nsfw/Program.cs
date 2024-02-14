using System.Diagnostics.CodeAnalysis;
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
    
    [DynamicDependency(DynamicallyAccessedMemberTypes.PublicConstructors | DynamicallyAccessedMemberTypes.PublicNestedTypes | DynamicallyAccessedMemberTypes.PublicProperties, typeof(ValidateNspCommand))]
    [DynamicDependency(DynamicallyAccessedMemberTypes.PublicConstructors | DynamicallyAccessedMemberTypes.PublicNestedTypes | DynamicallyAccessedMemberTypes.PublicProperties, typeof(ValidateNspSettings))]
    [DynamicDependency(DynamicallyAccessedMemberTypes.PublicConstructors | DynamicallyAccessedMemberTypes.PublicNestedTypes | DynamicallyAccessedMemberTypes.PublicProperties, typeof(BuildTitleDbCommand))]
    [DynamicDependency(DynamicallyAccessedMemberTypes.PublicConstructors | DynamicallyAccessedMemberTypes.PublicNestedTypes | DynamicallyAccessedMemberTypes.PublicProperties, typeof(BuildTitleDbSettings))]
    [DynamicDependency(DynamicallyAccessedMemberTypes.PublicConstructors | DynamicallyAccessedMemberTypes.PublicNestedTypes | DynamicallyAccessedMemberTypes.PublicProperties, typeof(Cdn2NspCommand))]
    [DynamicDependency(DynamicallyAccessedMemberTypes.PublicConstructors | DynamicallyAccessedMemberTypes.PublicNestedTypes | DynamicallyAccessedMemberTypes.PublicProperties, typeof(Cdn2NspSettings))]
    [DynamicDependency(DynamicallyAccessedMemberTypes.PublicConstructors | DynamicallyAccessedMemberTypes.PublicNestedTypes | DynamicallyAccessedMemberTypes.PublicProperties, typeof(MetaPropertiesCommand))]
    [DynamicDependency(DynamicallyAccessedMemberTypes.PublicConstructors | DynamicallyAccessedMemberTypes.PublicNestedTypes | DynamicallyAccessedMemberTypes.PublicProperties, typeof(MetaPropertiesSettings))]
    [DynamicDependency(DynamicallyAccessedMemberTypes.PublicConstructors | DynamicallyAccessedMemberTypes.PublicNestedTypes | DynamicallyAccessedMemberTypes.PublicProperties, typeof(QueryCommand))]
    [DynamicDependency(DynamicallyAccessedMemberTypes.PublicConstructors | DynamicallyAccessedMemberTypes.PublicNestedTypes | DynamicallyAccessedMemberTypes.PublicProperties, typeof(QuerySettings))]
    [DynamicDependency(DynamicallyAccessedMemberTypes.PublicConstructors | DynamicallyAccessedMemberTypes.PublicNestedTypes | DynamicallyAccessedMemberTypes.PublicProperties, typeof(TicketPropertiesCommand))]
    [DynamicDependency(DynamicallyAccessedMemberTypes.PublicConstructors | DynamicallyAccessedMemberTypes.PublicNestedTypes | DynamicallyAccessedMemberTypes.PublicProperties, typeof(TicketPropertiesSettings))]
    [DynamicDependency(DynamicallyAccessedMemberTypes.PublicConstructors | DynamicallyAccessedMemberTypes.PublicNestedTypes | DynamicallyAccessedMemberTypes.PublicProperties, typeof(NiSettings))]
    [DynamicDependency(DynamicallyAccessedMemberTypes.PublicConstructors | DynamicallyAccessedMemberTypes.PublicNestedTypes | DynamicallyAccessedMemberTypes.PublicProperties, typeof(NiCommand))]
    [DynamicDependency(DynamicallyAccessedMemberTypes.PublicConstructors | DynamicallyAccessedMemberTypes.PublicNestedTypes | DynamicallyAccessedMemberTypes.PublicProperties, typeof(HashCommand))]
    [DynamicDependency(DynamicallyAccessedMemberTypes.PublicConstructors | DynamicallyAccessedMemberTypes.PublicNestedTypes | DynamicallyAccessedMemberTypes.PublicProperties, typeof(HashSettings))]
    [DynamicDependency(DynamicallyAccessedMemberTypes.PublicConstructors | DynamicallyAccessedMemberTypes.PublicNestedTypes | DynamicallyAccessedMemberTypes.PublicProperties, typeof(CompareCommand))]
    [DynamicDependency(DynamicallyAccessedMemberTypes.PublicConstructors | DynamicallyAccessedMemberTypes.PublicNestedTypes | DynamicallyAccessedMemberTypes.PublicProperties, typeof(CompareSettings))]
    [DynamicDependency(DynamicallyAccessedMemberTypes.PublicConstructors | DynamicallyAccessedMemberTypes.PublicNestedTypes | DynamicallyAccessedMemberTypes.PublicProperties, typeof(ScanMissingCommand))]
    [DynamicDependency(DynamicallyAccessedMemberTypes.PublicConstructors | DynamicallyAccessedMemberTypes.PublicNestedTypes | DynamicallyAccessedMemberTypes.PublicProperties, typeof(ScanMissingSettings))]
    [DynamicDependency(DynamicallyAccessedMemberTypes.PublicConstructors | DynamicallyAccessedMemberTypes.PublicNestedTypes | DynamicallyAccessedMemberTypes.PublicProperties, "Spectre.Console.Cli.VersionCommand", "Spectre.Console.Cli")]
    [DynamicDependency(DynamicallyAccessedMemberTypes.PublicConstructors | DynamicallyAccessedMemberTypes.PublicNestedTypes | DynamicallyAccessedMemberTypes.PublicProperties, "Spectre.Console.Cli.XmlDocCommand", "Spectre.Console.Cli")]
    [DynamicDependency(DynamicallyAccessedMemberTypes.PublicConstructors | DynamicallyAccessedMemberTypes.PublicNestedTypes | DynamicallyAccessedMemberTypes.PublicProperties, "Spectre.Console.Cli.ExplainCommand", "Spectre.Console.Cli")]
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
                .WithDescription("Builds TitleDB from TitleDB repo files.")
                .WithAlias("btdb");

            config.AddCommand<NiCommand>("ni")
                .IsHidden();

            config.AddCommand<HashCommand>("hash")
                .WithDescription("Hash files for submission to DAT-o-MATIC.")
                .WithAlias("h");
            
            config.AddCommand<CompareCommand>("cmp")
                .WithDescription("Compare two NSP files.")
                .WithAlias("c")
                .IsHidden();

            config.AddCommand<ScanMissingCommand>("scan")
                .WithDescription("Scan directory for games and missing updates.")
                .WithAlias("s")
                .IsHidden();
        });

        return app.Run(args);
    }
}