using System.Security.Cryptography;
using LibHac.Common;
using LibHac.Tools.FsSystem;
using LibHac.Util;
using Serilog;
using ZstdSharp;

namespace Nsfw.Commands;

public class Ncz
{
    public static int UncompressableHeaderSize => 0x4000;
    public byte[] UncompressableHeader { get; init; }
    public NczSectionHeader[] Sections { get; init; }
    public  NczBlockHeader? Block { get; init; }

    public string TargetHash { get; init; }
    public string CurrentHash { get; private set; } = string.Empty;
    
    private readonly SHA256 _sha256;
    private readonly DecompressionStream _decompressor;
    private readonly BlockReader? _blockReader;
    
    public NczCompressionType CompressionType => Block != null ? NczCompressionType.Block : NczCompressionType.Solid;
    
    public long DecompressedSize
    {
        get { return UncompressableHeaderSize + Sections.Sum(x => x.Size); }
    }

    public Ncz(Stream stream, string targetHash)
    {
        TargetHash = targetHash;
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
            section.Index = i+1;
            Sections[i] = section;
        }

        var dataSectionStart = stream.Position;

        if (Sections[0].Offset - UncompressableHeaderSize > 0)
        {
            Log.Error("Fake Section time ?");
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
            for (var i = 0; i < Block.NumberOfBlocks; i++)
            {
                Block.CompressedBlockSizeList[i] = reader.ReadInt32();
            }

            dataSectionStart = stream.Position;
            
            _blockReader = new BlockReader(Block, stream);
        }
        
        _sha256 = SHA256.Create();
        _sha256.Initialize();
        reader.BaseStream.Seek(dataSectionStart, SeekOrigin.Begin);
        _decompressor = new DecompressionStream(reader.BaseStream);
    }
    
    public void HashChunk(byte[] chunk)
    {
        _sha256.TransformBlock(chunk, 0, chunk.Length, null, 0);
    }
    
    public bool IsValid()
    {
        _sha256.TransformFinalBlock(Array.Empty<byte>(), 0, 0);
        CurrentHash = _sha256.Hash.ToHexString()[..^32];
        return TargetHash.ToUpper().Equals(CurrentHash.ToUpper());
    }
    
    public int DecompressChunk(long offset, Span<byte> destination)
    {
        // Find section with current offset 
        var destinationPosition = 0;
        while (destinationPosition < destination.Length)
        {
            var section = GetSection(offset);
            var sectionRemaining = (section.Offset + section.Size) - offset;
            
            var chunkSize = destination.Length - destinationPosition > sectionRemaining ? (int)sectionRemaining : destination.Length - destinationPosition;
            var destinationChunk = destination.Slice(destinationPosition, chunkSize);
            DecompressFromSection(section, offset, destinationChunk);
            destinationPosition += chunkSize;
            offset += chunkSize;
        }

        HashChunk(destination.ToArray());
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