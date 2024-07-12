using System.Runtime.InteropServices;
using LibHac;
using LibHac.Common;
using LibHac.Common.FixedArrays;
using LibHac.Common.Keys;
using LibHac.Crypto.Impl;
using LibHac.Fs;
using LibHac.Fs.Fsa;
using LibHac.FsSystem;
using LibHac.Tools.Fs;
using LibHac.Tools.FsSystem;
using LibHac.Tools.FsSystem.NcaUtils;
using LibHac.Util;
using Spectre.Console.Cli;
using SQLitePCL;
using ZstdSharp;
using Aes = LibHac.Crypto.Aes;
using Path = System.IO.Path;

namespace Nsfw.Commands;

public class ExtractCommand : Command<ExtractSettings>
{
    public override int Execute(CommandContext context, ExtractSettings settings)
    {
        Console.WriteLine("NSP file        : {0}", settings.NspFile);
        Console.WriteLine("Output directory: {0}", settings.OutDirectory);
        
        var keySet = ExternalKeyReader.ReadKeyFile(settings.KeysFile);
        
        var ncaPath = Path.Combine(settings.OutDirectory, "output.nca");
            
        var fileStorage = new LocalStorage(ncaPath, FileAccess.Read);
        var nca = new Nca(keySet, fileStorage);
        
        var storage = new AesCtrStorage(nca.BaseStorage, "BB504B85D024BD9E3441CF4DB14DB2BD".ToBytes(), "00000000000000000000000000000000".ToBytes());
        storage.Write(0x1C000, "12345678".ToBytes());
        storage.WriteAllBytes(Path.Combine(settings.OutDirectory, "output2.nca"));
        // var outBuffer = new byte[8];
        // storage.Read(0x1C000, outBuffer);
        // Console.WriteLine(outBuffer.ToHexString());

        // var rawZero = nca.OpenFileSystem(0, IntegrityCheckLevel.ErrorOnInvalid);
        //
        // var entries = rawZero.EnumerateEntries("*.*", SearchOptions.RecurseSubdirectories).ToArray();
        //
        // foreach (var entry in entries)
        // {
        //     Console.WriteLine(entry.FullPath);
        // }
        //
        // Console.WriteLine("Header: " + nca.Header.TitleId.ToString("X8"));
        //
        // if (nca.CanOpenSection(0))
        // {
        //     Console.WriteLine("Section 0");
        //         
        //     Console.WriteLine(nca.Header.Magic);
        // }
        
        // var localFile = new LocalFile(settings.NspFile, OpenMode.Read);
        // var fileStorage = new FileStorage(localFile);
        // var fileSystem = new PartitionFileSystem();
        // fileSystem.Initialize(fileStorage);
        //
        // foreach (var rawFile in fileSystem.EnumerateEntries("*.*", SearchOptions.RecurseSubdirectories))
        // {
        //     //Console.WriteLine(rawFile.FullPath);
        //     
        //     var ncaPath = Path.Combine(settings.OutDirectory, "output.nca");
        //     
        //     // if(rawFile.Name.EndsWith(".ncz"))
        //     // {
        //     //     Console.WriteLine("NCZ Found!");
        //     //
        //     //     using var nczFileRef = new UniqueRef<IFile>();
        //     //     fileSystem.OpenFile(ref nczFileRef.Ref, rawFile.FullPath.ToU8Span(), OpenMode.Read).ThrowIfFailure();
        //     //
        //     //     var ncz = new Ncz(nczFileRef.Release().AsStream());
        //     //     ncz.Decompress(ncaPath);
        //     // }
        //     
        //     // using var miscFileRef = new UniqueRef<IFile>();
        //     // fileSystem.OpenFile(ref miscFileRef.Ref, rawFile.FullPath.ToU8Span(), OpenMode.Read).ThrowIfFailure();
        //     //
        //     // var outFile = Path.Combine(settings.OutDirectory, rawFile.Name);
        //     // using var outStream = new FileStream(outFile, FileMode.Create, FileAccess.ReadWrite);
        //     // miscFileRef.Get.GetSize(out var fileSize);
        //     // miscFileRef.Get.AsStream().CopyStream(outStream, fileSize);
        // }

        return 0;
    }
}

public class Ncz
{
    private const int UncompressableHeaderSize = 0x4000;
    
    private NczSection[] Sections { get; set; }

    private long _dataSectionStart;

    private byte[] _uncompressableHeader;
    
    private BinaryReader _reader;
    
    public long DecompressedSize => UncompressableHeaderSize + Sections.Sum(x => x.Size);
    
    public Ncz(Stream stream)
    {
        _reader = new BinaryReader(stream);
        _uncompressableHeader = _reader.ReadBytes(UncompressableHeaderSize);
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
            throw new InvalidDataException("NCZ Block not supported.");
        }
        
        Console.WriteLine("Data Section Start: 0x{0:X8}", _dataSectionStart);
        Console.WriteLine($"Decompressed size: {DecompressedSize}/{DecompressedSize:X8}");
        Console.WriteLine("--------------------");
    }
    
    public void Decompress(string path)
    {
        using var fileStream = File.Create(path);
        
        Console.WriteLine("Writing to : " + path);
        
        fileStream.Write(_uncompressableHeader, 0, UncompressableHeaderSize);
        
        _reader.BaseStream.Seek(_dataSectionStart, SeekOrigin.Begin);
        
        using var decompressor = new DecompressionStream(_reader.BaseStream);

        foreach (var nczSection in Sections)
        {
            var currentOffset = nczSection.Offset;
            var sectionEnd = nczSection.Offset + nczSection.Size;
            
            Console.WriteLine($"Start (Offset)      : 0x{nczSection.Offset:X8}");
            Console.WriteLine($"Size                : 0x{nczSection.Size:X8}");
            Console.WriteLine($"Section End         : 0x{sectionEnd:X8}");
            Console.WriteLine($"CryptoType          : 0x{nczSection.CryptoType:X8}");
            Console.WriteLine($"CryptoKey           : {nczSection.CryptoKey.ToHexString()}");
            Console.WriteLine($"CryptoCounter       : {nczSection.CryptoCounter.ToHexString()}");
            Console.WriteLine("BaseStream Position : 0x{0:X8}", _reader.BaseStream.Position);
            
            var useCrypto = nczSection.CryptoType is 3 or 4;
            //var crypto = Aes.CreateCtrEncryptor(nczSection.CryptoKey, nczSection.CryptoCounter, false);
            //var crypto = Aes.CreateCtrEncryptor(nczSection.CryptoKey, "00000000000000000000000000000000".ToBytes(), false);

            long unpackedBytes = 0;

            while (currentOffset < sectionEnd)
            {
                long chunkSize = 0x10000;

                if (sectionEnd - currentOffset < 0x10000)
                {
                    chunkSize = sectionEnd - currentOffset;
                }
                
                var buffer = new byte[chunkSize];
                var bytesRead = decompressor.Read(buffer);
                
                if(bytesRead == 0)
                {
                    break;
                }

                if (useCrypto)
                {
                    //var transformBuffer = new byte[buffer.Length];
                    //var encSize = Aes.EncryptCtr128(buffer, transformBuffer, nczSection.CryptoKey, nczSection.CryptoCounter);
                    
                    //Buffer.BlockCopy(buffer, 0, transformBuffer, 0, buffer.Length);
                    //crypto.Transform(transformBuffer, transformBuffer);
                    
                    //Console.WriteLine(transformBuffer.ToHexString());
                    
                    //return;
                }
                
                //Console.WriteLine($"Chunk : 0x{chunkSize:X8}");
                //Console.WriteLine(buffer.ToHexString());
                
                unpackedBytes += chunkSize;
                currentOffset += chunkSize;
                fileStream.Write(buffer);
            }
            
            Console.WriteLine($"UNPACKED : {unpackedBytes:X8}");
            Console.WriteLine($"ORIGINAL : {nczSection.Size:X8}");
            Console.WriteLine("--------------------");
            
            if(unpackedBytes != nczSection.Size)
            {
                throw new InvalidDataException("Unpacked size does not match expected size.");
            }
            
            /*
             * 		while i < end:
			if useCrypto:
				crypto.seek(i)
			chunkSz = 0x10000 if end - i > 0x10000 else end - i
			inputChunk = decompressor.read(chunkSz)
			if not len(inputChunk):
				break
			if useCrypto:
				inputChunk = crypto.encrypt(inputChunk)
			if f != None:
				f.write(inputChunk)
			hash.update(inputChunk)
			lenInputChunk = len(inputChunk)
			i += lenInputChunk
			decompressedBytes += lenInputChunk
			if statusReportInfo != None:
				statusReport[id] = [statusReport[id][0]+chunkSz, statusReport[id][1], nca_size, currentStep]
			elif decompressedBytes - decompressedBytesOld > 52428800: #Refresh every 50 MB
				decompressedBytesOld = decompressedBytes
				bar.count = decompressedBytes//1048576
				bar.refresh()
             */
            
        }
        
        //
        // foreach (var section in Sections)
        // {
        //     stream.Seek(section.Offset, SeekOrigin.Begin);
        //     stream.CopyStream(stream, section.Size);
        // }
    }
}

public class NczSection
{
    public long Offset { get; set; }
    public long Size { get; set; }
    public long CryptoType { get; set; }
    public byte[] CryptoKey { get; set; } = [];
    public byte[] CryptoCounter { get; set; } = [];
}

// public class AESCTR
// {
//     private byte[] key;
//     private byte[] nonce;
//     private int blockSize = 64;
//     private int counterSize = 8;
//     private long counter;
//     private Aes aes;
//
//     public AESCTR(byte[] key, byte[] nonce, long offset = 0)
//     {
//         this.key = key;
//         this.nonce = nonce;
//         this.aes = aes;
//         Seek(offset);
//     }
//
//     public byte[] Encrypt(byte[] data, long? ctr = null)
//     {
//         if (ctr.HasValue)
//         {
//             counter = ctr.Value;
//         }
//
//         return ProcessData(data);
//     }
//
//     public byte[] Decrypt(byte[] data, long? ctr = null)
//     {
//         // Encryption and decryption are symmetric in CTR mode
//         return Encrypt(data, ctr);
//     }
//
//     public void Seek(long offset)
//     {
//         counter = offset >> 4;
//         InitializeAes();
//     }
//
//     private void InitializeAes()
//     {
//         aes = Aes.Create();
//         aes.Key = key;
//         aes.Mode = CipherMode.ECB; // CTR mode will be implemented manually
//         aes.Padding = PaddingMode.None;
//     }
//
//     private byte[] ProcessData(byte[] data)
//     {
//         int dataLength = data.Length;
//         byte[] output = new byte[dataLength];
//         byte[] counterBlock = new byte[blockSize];
//         byte[] encryptedCounterBlock = new byte[blockSize];
//
//         using (ICryptoTransform encryptor = aes.CreateEncryptor())
//         {
//             for (int i = 0; i < dataLength; i += blockSize)
//             {
//                 Array.Copy(nonce, 0, counterBlock, 0, nonce.Length);
//                 BitConverter.GetBytes(counter).CopyTo(counterBlock, nonce.Length);
//                 encryptor.TransformBlock(counterBlock, 0, blockSize, encryptedCounterBlock, 0);
//
//                 for (int j = 0; j < blockSize && (i + j) < dataLength; j++)
//                 {
//                     output[i + j] = (byte)(data[i + j] ^ encryptedCounterBlock[j]);
//                 }
//
//                 counter++;
//             }
//         }
//
//         return output;
//     }
//
//     public byte[] BktrPrefix(long ctrVal)
//     {
//         byte[] prefix = new byte[nonce.Length + sizeof(int)];
//         Array.Copy(nonce, 0, prefix, 0, 4);
//         BitConverter.GetBytes(ctrVal).CopyTo(prefix, 4);
//         return prefix;
//     }
//
//     public void BktrSeek(long offset, long ctrVal, long virtualOffset = 0)
//     {
//         offset += virtualOffset;
//         counter = offset >> 4;
//         nonce = BktrPrefix(ctrVal);
//         InitializeAes();
//     }
// }
