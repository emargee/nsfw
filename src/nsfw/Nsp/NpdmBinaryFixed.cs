using System.Buffers.Binary;
using LibHac.Common;
using LibHac.Common.Keys;
using LibHac.Tools.Npdm;

namespace Nsfw.Nsp;

public class NpdmBinaryFixed
{
    public string Magic;
    public bool Is64Bits { get; }
    public int AddressSpaceWidth { get; }
    public byte MainThreadPriority { get; }
    public byte DefaultCpuId { get; }
    public int SystemResourceSize { get; }
    public int ProcessCategory { get; }
    public int MainEntrypointStackSize { get; }
    public string TitleName { get; }
    public byte[] ProductCode { get; }

    public Aci0Fixed Aci0 { get; }
    public Acid AciD { get; }

    public NpdmBinaryFixed(Stream stream) : this(stream, null) { }

    public NpdmBinaryFixed(Stream stream, KeySet? keySet)
    {
        var reader = new BinaryReader(stream);

        Magic = reader.ReadAscii(0x4);

        if (Magic != "META")
        {
            throw new Exception("NPDM Stream doesn't contain NPDM file!");
        }

        reader.ReadInt64();

        //MmuFlags, bit0: 64-bit instructions, bits1-3: address space width (1=64-bit, 2=32-bit). Needs to be <= 0xF.
        byte mmuflags = reader.ReadByte();

        Is64Bits = (mmuflags & 1) != 0;
        AddressSpaceWidth = (mmuflags >> 1) & 7;

        reader.ReadByte();

        MainThreadPriority = reader.ReadByte(); //(0-63).
        DefaultCpuId = reader.ReadByte();
        
        if(MainThreadPriority > 0x3F)
        {
            throw new Exception("MainThreadPriority is out of range!");
        }

        reader.ReadInt32();

        //System resource size (max size as of 5.x: 534773760).
        SystemResourceSize = BinaryPrimitives.ReverseEndianness(reader.ReadInt32());

        //ProcessCategory (0: regular title, 1: kernel built-in). Should be 0 here.
        ProcessCategory = BinaryPrimitives.ReverseEndianness(reader.ReadInt32());

        //Main entrypoint stack size.
        MainEntrypointStackSize = reader.ReadInt32();

        TitleName = reader.ReadUtf8(0x10).Trim('\0');

        ProductCode = reader.ReadBytes(0x10);

        stream.Seek(0x30, SeekOrigin.Current);

        int aci0Offset = reader.ReadInt32();
        int aci0Size = reader.ReadInt32();
        int acidOffset = reader.ReadInt32();
        int acidSize = reader.ReadInt32();
        
        Aci0 = new Aci0Fixed(stream, aci0Offset);
        AciD = new Acid(stream, acidOffset, keySet);
    }
}

public class Aci0Fixed
{
    public string Magic;
    public long TitleId { get; }
    public int FsVersion { get; }
    public NpdmFsAccessControlFlags FsPermissionsBitmask { get; }
    public ServiceAccessControl? ServiceAccess { get; }
    public KernelAccessControl? KernelAccess { get; }

    public Aci0Fixed(Stream stream, int offset)
    {
        stream.Seek(offset, SeekOrigin.Begin);

        var reader = new BinaryReader(stream);

        Magic = reader.ReadAscii(0x4);

        if (Magic != "ACI0")
        {
            throw new Exception("ACI0 Stream doesn't contain ACI0 section!");
        }

        stream.Seek(0xc, SeekOrigin.Current);

        TitleId = reader.ReadInt64();

        //Reserved.
        stream.Seek(8, SeekOrigin.Current);

        int fsAccessHeaderOffset = reader.ReadInt32();
        int fsAccessHeaderSize = reader.ReadInt32();
        int serviceAccessControlOffset = reader.ReadInt32();
        int serviceAccessControlSize = reader.ReadInt32();
        int kernelAccessControlOffset = reader.ReadInt32();
        int kernelAccessControlSize = reader.ReadInt32();

        stream.Seek(8, SeekOrigin.Current);
        
        if (fsAccessHeaderSize > 0)
        {
            //Console.WriteLine("fsAccessHeaderSize: " + fsAccessHeaderSize.ToString("X8"));
            //Console.WriteLine("fsAccessHeaderOffset: " + fsAccessHeaderOffset.ToString("X8"));
            
            var accessHeader = new FsAccessHeaderFixed(stream, offset + fsAccessHeaderOffset);

            FsVersion = accessHeader.Version;
            FsPermissionsBitmask = accessHeader.PermissionFlags;
        }

        if (serviceAccessControlSize > 0)
        {
            ServiceAccess = new ServiceAccessControl(stream, offset + serviceAccessControlOffset, serviceAccessControlSize);
        }

        if (kernelAccessControlSize > 0)
        {
            KernelAccess = new KernelAccessControl(stream, offset + kernelAccessControlOffset, kernelAccessControlSize);
        }
    }
}

public class FsAccessHeaderFixed
{
    public int Version { get; }
    public NpdmFsAccessControlFlags PermissionFlags { get; }

    public FsAccessHeaderFixed(Stream stream, int offset)
    {
        stream.Seek(offset, SeekOrigin.Begin);

        var reader = new BinaryReader(stream);

        Version = reader.ReadByte();
        reader.ReadBytes(3); //Reserved.
        PermissionFlags = (NpdmFsAccessControlFlags)reader.ReadUInt64();
        
        var contentOwnerInfoOffset = reader.ReadUInt32();
        var contentOwnerInfoSize = reader.ReadUInt32();
        var saveDataOwnerInfoOffset = reader.ReadUInt32();
        var saveDataOwnerInfoSize = reader.ReadUInt32();
        
        if (contentOwnerInfoOffset != 0x1c)
        {
            throw new Exception("FsAccessHeader is corrupted!");
        }
        
        //Console.WriteLine("ContentOwnerInfoOffset: " + contentOwnerInfoOffset.ToString("X8"));
        //Console.WriteLine("SaveDataOwnerInfoOffset: " + saveDataOwnerInfoOffset.ToString("X8"));

        if (contentOwnerInfoSize > 0)
        {
            //Console.WriteLine("ContentOwnerInfoSize: " + contentOwnerInfoSize.ToString("X8"));
            var contentOwnerIdCount = reader.ReadUInt32();
            var contentOwnerIds = reader.ReadUInt64();
            //Console.WriteLine("ContentOwnerIdCount :" + contentOwnerIdCount);
            //Console.WriteLine("ContentOwnerIds     :" + contentOwnerIds.ToString("X16"));
        }

        if (saveDataOwnerInfoSize > 0)
        {
            //Console.WriteLine("SaveDataOwnerInfoSize: " + saveDataOwnerInfoSize.ToString("X8"));
            var saveDataOwnerIdCount = reader.ReadUInt32();
            //Console.WriteLine("SaveDataOwnerIdCount :" + saveDataOwnerIdCount);
        }
    }
}

[Flags]
public enum NpdmAccessibility: uint
{
    None = 0,
    Read = 1U << 0,
    Write = 1U << 1,
    ReadWrite = Read | Write,
}

[Flags]
public enum NpdmFsAccessControlFlags : ulong
{
    None = 0,
    ApplicationInfo = 1UL << 0,
    BootModeControl = 1UL << 1,
    Calibration = 1UL << 2,
    SystemSaveData = 1UL << 3,
    GameCard = 1UL << 4,
    SaveDataBackUp = 1UL << 5,
    SaveDataManagement = 1UL << 6,
    BisAllRaw = 1UL << 7,
    GameCardRaw = 1UL << 8,
    GameCardPrivate = 1UL << 9,
    SetTime = 1UL << 10,
    ContentManager = 1UL << 11,
    ImageManager = 1UL << 12,
    CreateSaveData = 1UL << 13,
    SystemSaveDataManagement = 1UL << 14,
    BisFileSystem = 1UL << 15,
    SystemUpdate = 1UL << 16,
    SaveDataMeta = 1UL << 17,
    DeviceSaveData = 1UL << 18,
    SettingsControl = 1UL << 19,
    SystemData = 1UL << 20,
    SdCard = 1UL << 21,
    Host = 1UL << 22,
    FillBis = 1UL << 23,
    CorruptSaveData = 1UL << 24,
    SaveDataForDebug = 1UL << 25,
    FormatSdCard = 1UL << 26,
    GetRightsId = 1UL << 27,
    RegisterExternalKey = 1UL << 28,
    RegisterUpdatePartition = 1UL << 29,
    SaveDataTransfer = 1UL << 30,
    DeviceDetection = 1UL << 31,
    AccessFailureResolution = 1UL << 32,
    SaveDataTransferVersion2 = 1UL << 33,
    RegisterProgramIndexMapInfo = 1UL << 34,
    CreateOwnSaveData = 1UL << 35,
    MoveCacheStorage = 1UL << 36,
    DeviceTreeBlob = 1UL << 37,
    NotifyErrorContextServiceReady = 1UL << 38,
    Debug = 1UL << 62,
    FullPermission = 1UL << 63
}