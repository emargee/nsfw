namespace Nsfw.Commands;

[Flags]
public enum FixedPropertyFlags
{
    PreInstall = 1 << 0,            // Determines if the title comes pre-installed on the device. Most likely unused -- a remnant from previous ticket formats.
    SharedTitle = 1 << 1,           // Determines if the title holds shared contents only. Most likely unused -- a remnant from previous ticket formats.
    AllowAllContent = 1 << 2,       // Determines if the content index mask shall be bypassed. Most likely unused -- a remnant from previous ticket formats.
    DeviceLinkIndependent = 1 << 3,  // Determines if the console should *not* connect to the Internet to verify if the title's being used by the primary console.
    Volatile = 1 << 4,              // Determines if the ticket copy inside ticket.bin should be encrypted or not.
    ELicenseRequired = 1 << 5,      // Determines if the console should connect to the Internet to perform license verification.
}