using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using LibHac;
using LibHac.Common;
using LibHac.Common.Keys;
using LibHac.Fs;
using LibHac.Fs.Fsa;
using LibHac.FsSystem;
using LibHac.Spl;
using LibHac.Tools.Es;
using LibHac.Tools.FsSystem;
using LibHac.Tools.FsSystem.NcaUtils;
using LibHac.Util;
using Spectre.Console.Cli;
using SQLite;
using ZstdSharp;
using Path = System.IO.Path;

namespace Nsfw.Commands;

public class ExtractCommand : Command<ExtractSettings>
{
    public override int Execute(CommandContext context, ExtractSettings settings)
    {
        Console.WriteLine("NSZ file        : {0}", settings.NszFile);
        Console.WriteLine("Output directory: {0}", settings.OutDirectory);
        
        var nspFilename = Path.GetFileName(settings.NszFile).Replace(Path.GetExtension(settings.NszFile), ".nsp");
        Console.WriteLine(nspFilename);
        
        var outputNsp = Path.Combine(settings.OutDirectory, nspFilename);
        
        var localFile = new LocalFile(settings.NszFile, OpenMode.All);
        var fileStorage = new FileStorage(localFile);
        var fileSystem = new PartitionFileSystem();
        fileSystem.Initialize(fileStorage);
        
        var builder = new PartitionFileSystemBuilder();
        
        using var file = new UniqueRef<IFile>();
        
        foreach (var rawFile in fileSystem.EnumerateEntries("*.*", SearchOptions.RecurseSubdirectories))
        {
            fileSystem.OpenFile(ref file.Ref, rawFile.FullPath.ToU8Span(), OpenMode.Read).ThrowIfFailure();
            
            if (rawFile.Name.EndsWith(".ncz"))
            {
                Console.WriteLine("NCZ Found!");
            
                var fileHash = rawFile.Name.Replace(".ncz", "");
                
                if(fileHash.Length != 32)
                {
                    Console.WriteLine("Filename of NCZ is used as verifying hash, but it's not 32 characters long. Cannot validate.");
                    return 1;
                }
                
                var ncz = new Ncz(file.Release().AsStream(), fileHash.ToUpperInvariant());
                
                //var decompFile = new DecompressNczFile(ncz);
                
                //builder.AddFile(fileHash + ".nca", decompFile);
                
                Console.WriteLine("Decompressing..");
                
                var ncaPath = Path.Combine(settings.OutDirectory, fileHash + ".nca");
                
                ncz.Decompress(ncaPath);
                
                if (!ncz.IsVerified)
                {
                    Console.WriteLine("Decompressed file hash does not match original hash");
                    return 1;
                }
                
                Console.WriteLine("Success - Decompressed file hash matches original hash");
                
                builder.AddFile(fileHash + ".nca", new LocalFile(ncaPath, OpenMode.Read));
                continue;
            }
            
            builder.AddFile(rawFile.FullPath.TrimStart('/'), file.Release());
        }
        
        try
        {
            using var outStream = new FileStream(outputNsp, FileMode.Create, FileAccess.ReadWrite);
            var builtPfs = builder.Build(PartitionFileSystemType.Standard);
            builtPfs.GetSize(out var pfsSize).ThrowIfFailure();
            builtPfs.CopyToStream(outStream, pfsSize);
        }
        catch (Exception exception)
        {
            Console.WriteLine($"Failed to convert file. {exception.Message}");
        }

        return 0;
    }
}

// public class DecompressNczFile : IFile
// {
//     public readonly Ncz NczFile;
//
//     public long counterOffset = 0;
//     
//     public DecompressNczFile(Ncz nczFile)
//     {
//         NczFile = nczFile;
//     }
//     
//     protected override Result DoRead(out long bytesRead, long offset, Span<byte> destination, in ReadOption option)
//     {
//         Console.WriteLine($"READ => Offset: {offset:X8} / Dest Size : {destination.Length:X8}");
//         
//         if (destination.IsEmpty)
//         {
//             bytesRead = 0;
//             return Result.Success;
//         }
//
//         var chunkSize = destination.Length;
//
//         var sliceStart = 0;
//         
//         if(counterOffset < Ncz.UncompressableHeaderSize)
//         {
//             var toRead = (int)Math.Min(destination.Length, Ncz.UncompressableHeaderSize - counterOffset);
//             NczFile.UncompressableHeader.AsSpan(0, toRead).CopyTo(destination.Slice(0, toRead));
//             Console.WriteLine($"Adding header .. {toRead:X8}");
//             sliceStart += toRead;
//             counterOffset += toRead;
//             chunkSize -= toRead;
//         }
//
//         bytesRead = NczFile.DecompressBlock(counterOffset, chunkSize, destination.Slice(sliceStart, destination.Length - sliceStart));
//         
//         counterOffset += chunkSize;
//
//         return Result.Success;
//     }
//
//     protected override Result DoWrite(long offset, ReadOnlySpan<byte> source, in WriteOption option)
//     {
//         Console.WriteLine("DO WRITE!");
//
//         return Result.Success;
//     }
//
//     protected override Result DoFlush()
//     {
//         Console.WriteLine("DO FLUSH!");
//
//         return Result.Success;
//     }
//
//     protected override Result DoSetSize(long size)
//     {
//         Console.WriteLine("DO SETSIZE!");
//
//         return Result.Success;
//     }
//
//     protected override Result DoGetSize(out long size)
//     {
//         UnsafeHelpers.SkipParamInit(out size);
//         
//         size = NczFile.DecompressedSize;
//
//         return Result.Success;
//     }
//
//     protected override Result DoOperateRange(Span<byte> outBuffer, OperationId operationId, long offset, long size, ReadOnlySpan<byte> inBuffer)
//     {
//         Console.WriteLine("DO OPERATERANGE!");
//
//         return Result.Success;
//     }
// }

public class Ncz
{
    public bool IsVerified => _fileHash.Equals(_sha256.Hash.ToHexString()[..^32]);
    
    public const int UncompressableHeaderSize = 0x4000;
    private NczSection[] Sections { get; set; }

    private long _dataSectionStart;

    public byte[] UncompressableHeader { get; private set; }

    private BinaryReader _reader;
    
    private byte[] Counter;
    
    private readonly object _locker = new object();
    
    private readonly SHA256 _sha256;
    private readonly string _fileHash;

    public long DecompressedSize => UncompressableHeaderSize + Sections.Sum(x => x.Size);
    
    private NczBlock Block { get; set; }

    public Ncz(Stream stream, string fileHash)
    {
        _fileHash = fileHash;
        _sha256 = SHA256.Create();
        _reader = new BinaryReader(stream);
        UncompressableHeader = _reader.ReadBytes(UncompressableHeaderSize);
        var sectionMagic = _reader.ReadAscii(0x8);

        if (sectionMagic != "NCZSECTN")
        {
            throw new InvalidDataException("NCZ magic is invalid.");
        }

        var sectionCount = _reader.ReadInt64();
        Sections = new NczSection[sectionCount];

        for (var i = 0; i < sectionCount; i++)
        {
            var section = new NczSection();

            section.Offset = _reader.ReadInt64();
            section.Size = _reader.ReadInt64();
            section.CryptoType = _reader.ReadInt64();
            _reader.ReadInt64(); // padding
            section.CryptoKey = _reader.ReadBytes(16);
            section.CryptoCounter = _reader.ReadBytes(16);
            Sections[i] = section;
        }

        _dataSectionStart = stream.Position;

        if (Sections[0].Offset - UncompressableHeaderSize > 0)
        {
            Console.WriteLine("Fake Section time ?");
        }

        var blockMagic = _reader.ReadAscii(0x8);

        if (blockMagic == "NCZBLOCK")
        {
            Block = new NczBlock();
            Block.Version = _reader.ReadSByte();
            Block.Type = _reader.ReadSByte();
            _reader.ReadSByte(); // Unused
            Block.BlockSizeExponent = _reader.ReadSByte();
            Block.NumberOfBlocks = _reader.ReadInt32();
            Block.DecompressedSize = _reader.ReadInt64();
            Block.CompressedBlockSizeList = new int[Block.NumberOfBlocks];
            for (int i = 0; i < Block.NumberOfBlocks; i++)
            {
                Block.CompressedBlockSizeList[i] = _reader.ReadInt32();
            }
            
            throw new InvalidOperationException("Block decompression not implemented.");
        }
    }

    // public int DecompressBlock(long offset, long chunkSize, Span<byte> destination)
    // {
    //     Console.WriteLine($"Offset: {offset:X8} / Chunk Size: {chunkSize:X8} / Dest Size: {destination.Length:X8}");
    //
    //     Console.ReadLine();
    //     
    //     _reader.BaseStream.Seek(offset, SeekOrigin.Begin);
    //     using var decompressor = new DecompressionStream(_reader.BaseStream);
    //
    //     var nczSection = GetSection(offset); // Work out which section corresponds to offset
    //     var nczSectionEnd = nczSection.Offset + nczSection.Size;
    //     
    //     var useCrypto = nczSection.CryptoType is 3 or 4;
    //     
    //     long cryptoCounterOffset = 0;
    //         
    //     for (int i = 0; i < 8; i++)
    //     {
    //         cryptoCounterOffset |= (long)nczSection.CryptoCounter[0xF - i] << (4 + i * 8);
    //     }
    //         
    //     var encryptor = new Aes128CtrTransform(nczSection.CryptoKey, nczSection.CryptoCounter);
    //     var cryptoCounter = encryptor.Counter;
    //     
    //     // Does section end before end of chunk ?
    //     if (nczSectionEnd - offset < chunkSize)
    //     {
    //         //Need to load next section !
    //     }
    //         
    //     var buffer = new byte[chunkSize];
    //     var bytesRead = decompressor.ReadAtLeast(buffer, (int)chunkSize);
    //             
    //     if(bytesRead == 0)
    //     {
    //         break;
    //     }
    //     
    //     
    //     //_reader.BaseStream.Seek(offset, SeekOrigin.Begin);
    //
    //     return 0;
    // }

    public void Decompress(string outputPath)
    {
        Console.WriteLine("Writing to : " + outputPath);

        using IStorage outFile = new LocalStorage(outputPath, FileAccess.ReadWrite, FileMode.Create);
        
        _sha256.TransformBlock(UncompressableHeader, 0, UncompressableHeader.Length, null, 0);
        
        outFile.Write(0, UncompressableHeader);
        
        _reader.BaseStream.Seek(_dataSectionStart, SeekOrigin.Begin);
        
        using var decompressor = new DecompressionStream(_reader.BaseStream);
        
        foreach (var nczSection in Sections)
        {
            var currentOffset = nczSection.Offset;
            var sectionEnd = nczSection.Offset + nczSection.Size;
            
            // Console.WriteLine($"Start (Offset)      : 0x{nczSection.Offset:X8}");
            // Console.WriteLine($"Size                : 0x{nczSection.Size:X8}");
            // Console.WriteLine($"Section End         : 0x{sectionEnd:X8}");
            // Console.WriteLine($"CryptoType          : 0x{nczSection.CryptoType:X8}");
            // Console.WriteLine($"CryptoKey           : {nczSection.CryptoKey.ToHexString()}");
            // Console.WriteLine($"CryptoCounter       : {nczSection.CryptoCounter.ToHexString()}");
            // Console.WriteLine("BaseStream Position  : 0x{0:X8}", _reader.BaseStream.Position);
            
            // ERR = 0
            // NONE = 1
            // XTS = 2
            // CTR = 3
            // BKTR = 4
            // NCA0 = 0x3041434E
            
            var useCrypto = nczSection.CryptoType is 3 or 4;
            
            Stream outputFileStream;

            outputFileStream = outFile.AsStream();
            outputFileStream.Seek(nczSection.Offset, SeekOrigin.Current);
            
            long counterOffset = 0;
            
            for (int i = 0; i < 8; i++)
            {
                counterOffset |= (long)nczSection.CryptoCounter[0xF - i] << (4 + i * 8);
            }
            
            var decryptor = new Aes128CtrTransform(nczSection.CryptoKey, nczSection.CryptoCounter);
            Counter = decryptor.Counter;
            
            long unpackedBytes = 0;
            long totalBytesRead = 0;
            
            while (currentOffset < sectionEnd)
            {
                long chunkSize = 0x10000;
            
                if (sectionEnd - currentOffset < 0x10000)
                {
                    chunkSize = sectionEnd - currentOffset;
                }
            
                var buffer = new byte[chunkSize];
                var bytesRead = decompressor.ReadAtLeast(buffer, (int)chunkSize);
                
                if(bytesRead == 0)
                {
                    break;
                }
                
                // Console.WriteLine($"Decompressing: {currentOffset}/{sectionEnd}");
                // Console.WriteLine($"Chunk size   : {chunkSize}");
                // Console.WriteLine($"Input chunk  : {bytesRead}");
                
                if (useCrypto)
                {
                    UpdateCounter(currentOffset);
                    decryptor.TransformBlock(buffer);
                    outputFileStream.Write(buffer);

                }
                else
                {
                    outputFileStream.Write(buffer);
                }
                
                _sha256.TransformBlock(buffer, 0, bytesRead,null, 0);
                
                totalBytesRead += bytesRead;
                currentOffset += chunkSize;
            }
        }
        
        _sha256.TransformFinalBlock(Array.Empty<byte>(), 0, 0);
    }
    
    private void UpdateCounter(long offset)
    {
        ulong off = (ulong)offset >> 4;

        for (uint j = 0; j < 0x7; j++)
        {
            Counter[0x10 - j - 1] = (byte)(off & 0xFF);
            off >>= 8;
        }

        // Because the value stored in the counter is offset >> 4, the top 4 bits 
        // of byte 8 need to have their original value preserved
        Counter[8] = (byte)((Counter[8] & 0xF0) | (int)(off & 0x0F));
    }
}