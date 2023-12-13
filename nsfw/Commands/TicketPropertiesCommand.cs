using System.Diagnostics.CodeAnalysis;
using LibHac.Fs;
using LibHac.FsSystem;
using LibHac.Tools.Es;
using LibHac.Tools.FsSystem;
using LibHac.Util;
using Spectre.Console;
using Spectre.Console.Cli;

namespace Nsfw.Commands;

public class TicketPropertiesCommand : Command<TicketPropertiesSettings>
{
    public override int Execute([NotNull] CommandContext context, [NotNull] TicketPropertiesSettings settings)
    {
        var ticket = new Ticket(new LocalFile(settings.TicketFile, OpenMode.Read).AsStream());
        var fixedSignature = Enumerable.Repeat((byte)0xFF, 0x100).ToArray();
        
        var table = new Table
        {
            ShowHeaders = false
        };
        
        table.AddColumn("Property");
        table.AddColumn("Value");
        
        if (fixedSignature.ToHexString() == ticket.Signature.ToHexString())
        {
            table.AddRow("Ticket Signature", "[olive]Normalised[/]");
        }
        else
        {
            var isTicketSignatureValid = Nsp.NsfwUtilities.ValidateTicket(ticket, settings.CertFile);
            table.AddRow("Ticket Signature", isTicketSignatureValid ? "[green]Valid[/]" : "[red]Invalid[/]");
        }
        
        Nsp.NsfwUtilities.RenderTicket(table, ticket);
        
        AnsiConsole.Write(new Padder(table).PadLeft(1).PadRight(0).PadBottom(0).PadTop(1));

        return 0;
    }
}