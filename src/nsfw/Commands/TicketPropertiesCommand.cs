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

        var isNormalised = fixedSignature.ToHexString() == ticket.Signature.ToHexString();
        var isTicketSignatureValid = false;
        
        if (isNormalised)
        {
            isTicketSignatureValid = Nsp.NsfwUtilities.ValidateTicket(ticket, settings.CertFile);
        }
        
        AnsiConsole.Write(new Padder(RenderUtilities.RenderTicket(ticket, isNormalised, isTicketSignatureValid)).PadLeft(1).PadRight(0).PadBottom(0).PadTop(1));

        return 0;
    }
}