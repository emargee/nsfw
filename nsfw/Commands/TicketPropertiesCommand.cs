using CliWrap;
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
    public override int Execute(CommandContext context, TicketPropertiesSettings settings)
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
            var isTicketSignatureValid = NsfwUtilities.ValidateTicket(ticket, settings.CertFile);
            table.AddRow("Ticket Signature", isTicketSignatureValid ? "[green]Valid[/]" : "[red]Invalid[/]");
        }
        
        table.AddRow("Issuer", ticket.Issuer);
        table.AddRow("Format Version", "0x" + ticket.FormatVersion.ToString("X"));
        table.AddRow("TitleKey Type", ticket.TitleKeyType.ToString());
        table.AddRow("Ticket Id", "0x" +ticket.TicketId.ToString("X"));
        table.AddRow("Ticket Version", "0x" +ticket.TicketVersion.ToString("X"));
        table.AddRow("License Type", ticket.LicenseType.ToString());
        table.AddRow("Crypto Type", "0x" +ticket.CryptoType.ToString("X"));
        table.AddRow("Device Id", "0x" +ticket.DeviceId.ToString("X"));
        table.AddRow("Account Id", "0x" +ticket.AccountId.ToString("X"));
        table.AddRow("Rights Id", ticket.RightsId.ToHexString());
        table.AddRow("Signature Type", ticket.SignatureType.ToString());
        
        var propertyTable = new Table{
            ShowHeaders = false
        };
        propertyTable.AddColumn("Property");
        propertyTable.AddColumn("Value");
        
        var myTikFlags = (MyPropertyFlags)ticket.PropertyMask;
        propertyTable.AddRow("Pre-Install ?", myTikFlags.HasFlag(MyPropertyFlags.PreInstall).ToString());
        propertyTable.AddRow("Allow All Content ?", myTikFlags.HasFlag(MyPropertyFlags.AllowAllContent).ToString());
        propertyTable.AddRow("Shared Title ?", myTikFlags.HasFlag(MyPropertyFlags.SharedTitle).ToString());
        propertyTable.AddRow("DeviceLink Independent ?", myTikFlags.HasFlag(MyPropertyFlags.DeviceLinkIndependent).ToString());
        propertyTable.AddRow("Volatile ?", myTikFlags.HasFlag(MyPropertyFlags.Volatile).ToString());
        propertyTable.AddRow("E-License Required ?", myTikFlags.HasFlag(MyPropertyFlags.ELicenseRequired).ToString());

        table.AddRow(new Text("Properties"), propertyTable);
        
        AnsiConsole.Write(new Padder(table).PadRight(1));

        return 0;
    }
}

[Flags]
public enum MyPropertyFlags
{
    PreInstall = 1 << 0,            // Determines if the title comes pre-installed on the device. Most likely unused -- a remnant from previous ticket formats.
    SharedTitle = 1 << 1,           // Determines if the title holds shared contents only. Most likely unused -- a remnant from previous ticket formats.
    AllowAllContent = 1 << 2,       // Determines if the content index mask shall be bypassed. Most likely unused -- a remnant from previous ticket formats.
    DeviceLinkIndependent = 1 << 3,  // Determines if the console should *not* connect to the Internet to verify if the title's being used by the primary console.
    Volatile = 1 << 4,              // Determines if the ticket copy inside ticket.bin should be encrypted or not.
    ELicenseRequired = 1 << 5,      // Determines if the console should connect to the Internet to perform license verification.
}