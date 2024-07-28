using System.Runtime.Intrinsics.Arm;
using System.Security.Cryptography;
using LibHac.Common;
using LibHac.Tools.FsSystem;
using LibHac.Util;
using ZstdSharp;

namespace Nsfw.Commands;

public class Ncz
{
    public byte[] UncompressableHeader { get; private set; }
    public const int UncompressableHeaderSize = 0x4000;
    private NczSectionHeader[] Sections { get; set; }
    private readonly SHA256 _sha256;
    private readonly string _fileHash;
    private readonly DecompressionStream _decompressor;
    private NczBlockHeader? Block;
    private BlockReader? _blockReader;
    
    public NczCompressionType CompressionType => Block != null ? NczCompressionType.Block : NczCompressionType.Solid;
    
    public long DecompressedSize
    {
        get { return UncompressableHeaderSize + Sections.Sum(x => x.Size); }
    }

    public Ncz(Stream stream, string fileHash)
    {
        _fileHash = fileHash;
        var reader = new BinaryReader(stream);
        UncompressableHeader = reader.ReadBytes(UncompressableHeaderSize);
        var sectionMagic = reader.ReadAscii(0x8);

        if (sectionMagic != "NCZSECTN")
        {
            throw new InvalidDataException("NCZ magic is invalid.");
        }

        var sectionCount = reader.ReadInt64();
        Sections = new NczSectionHeader[sectionCount];

        for (var i = 0; i < sectionCount; i++)
        {
            var section = new NczSectionHeader();

            section.Offset = reader.ReadInt64();
            section.Size = reader.ReadInt64();
            section.CryptoType = reader.ReadInt64();
            reader.ReadInt64(); // padding
            section.CryptoKey = reader.ReadBytes(16);
            section.CryptoCounter = reader.ReadBytes(16);
            Sections[i] = section;
        }

        var dataSectionStart = stream.Position;

        if (Sections[0].Offset - UncompressableHeaderSize > 0)
        {
            Console.WriteLine("Fake Section time ?");
        }

        var blockMagic = reader.ReadAscii(0x8);

        if (blockMagic == "NCZBLOCK")
        {
            Block = new NczBlockHeader();
            Block.Version = reader.ReadSByte();
            Block.Type = reader.ReadSByte();
            reader.ReadSByte(); // Unused
            Block.BlockSizeExponent = reader.ReadSByte();
            Block.NumberOfBlocks = reader.ReadInt32();
            Block.DecompressedSize = reader.ReadInt64();
            Block.CompressedBlockSizeList = new int[Block.NumberOfBlocks];
            for (int i = 0; i < Block.NumberOfBlocks; i++)
            {
                Block.CompressedBlockSizeList[i] = reader.ReadInt32();
            }

            dataSectionStart = stream.Position;
            
            _blockReader = new BlockReader(Block, stream);
        }
        
        _sha256 = SHA256.Create();
        _sha256.Initialize();
        _sha256.TransformBlock(UncompressableHeader, 0, UncompressableHeaderSize, null, 0);
        reader.BaseStream.Seek(dataSectionStart, SeekOrigin.Begin);
        _decompressor = new DecompressionStream(reader.BaseStream);
    }
    
    public bool IsValid()
    {
        _sha256.TransformFinalBlock(Array.Empty<byte>(), 0, 0);
        return _fileHash.Equals(_sha256.Hash.ToHexString()[..^32]);
    }
    
    public int DecompressChunk(long offset, Span<byte> destination)
    {
        // Find section with current offset 

        var section = GetSection(offset);
        var sectionEnd = section.Offset + section.Size;
        
        var hasExtraChunk = false;
        
        Span<byte> destinationChunk;
        
        if(destination.Length > sectionEnd - offset)
        {
            destinationChunk = destination.Slice(0, (int)(sectionEnd - offset));
            hasExtraChunk = true;
        }
        else
        {
            destinationChunk = destination;
        }

        DecompressFromSection(section, offset, destinationChunk);
        
        if (!hasExtraChunk)
        {
            _sha256.TransformBlock(destination.ToArray(), 0, destination.Length, null, 0);
            return destinationChunk.Length;
        }
        
        offset += destinationChunk.Length;
        
        // Find next section ..
        var nextSection = GetSection(offset);
        
        DecompressFromSection(nextSection, offset, destination.Slice(destinationChunk.Length, destination.Length - destinationChunk.Length));
        
        _sha256.TransformBlock(destination.ToArray(), 0, destination.Length, null, 0);
        return destination.Length;
    }
    
    private NczSectionHeader GetSection(long offset)
    {
        foreach (var nczSection in Sections)
        {
            if (offset >= nczSection.Offset && offset < (nczSection.Offset + nczSection.Size))
            {
                return nczSection;
            }
        }

        throw new InvalidOperationException("Unable to match section. Offset outside of all ranges");
    }

    private void DecompressFromSection(NczSectionHeader section,long currentOffset, Span<byte> destinationChunk)
    {
        // ERR = 0
        // NONE = 1
        // XTS = 2
        // CTR = 3
        // BKTR = 4
        // NCA0 = 0x3041434E
        
        var useCrypto = section.CryptoType is 3 or 4;
        var encryptor = new Aes128CtrTransform(section.CryptoKey, section.CryptoCounter);
        var cryptoCounter = encryptor.Counter;
        
        if(CompressionType == NczCompressionType.Block)
        {
            _blockReader?.Read(destinationChunk);
        }
        else
        {
            _decompressor.ReadExactly(destinationChunk);
        }

        if (!useCrypto) return;
        
        UpdateCounter(ref cryptoCounter, currentOffset);
        encryptor.TransformBlock(destinationChunk);
    }

    private void UpdateCounter(ref byte[] counter, long offset)
    {
        ulong off = (ulong)offset >> 4;

        for (uint j = 0; j < 0x7; j++)
        {
            counter[0x10 - j - 1] = (byte)(off & 0xFF);
            off >>= 8;
        }

        // Because the value stored in the counter is offset >> 4, the top 4 bits 
        // of byte 8 need to have their original value preserved
        counter[8] = (byte)((counter[8] & 0xF0) | (int)(off & 0x0F));
    }
}

public enum NczCompressionType
{
    Solid,
    Block
}