namespace Nsfw.Commands;

public enum FixedContentMetaType : byte
{
    SystemProgram = 1,
    SystemData = 2,
    SystemUpdate = 3,
    BootImagePackage = 4,
    BootImagePackageSafe = 5,
    Application = 0x80,
    Patch = 0x81,
    AddOnContent = 0x82,
    Delta = 0x83,
    DataPatch = 0x84
}