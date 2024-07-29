using ZstdSharp;

namespace Nsfw.Commands;

public class BlockReader
{
    private readonly NczBlockHeader _blockHeader;
    private readonly List<long> _compressedBlockOffsetList;
    private readonly int[] _compressedBlockSizeList;
    private readonly Stream _baseFileReader;
    private readonly long _blockSize;
    private long _blockPosition;
    private int _currentBlockId = -1;
    private byte[] _currentBlock = [];

    public BlockReader(NczBlockHeader blockHeader, Stream baseFileReader)
    {
        _blockHeader = blockHeader;
        _baseFileReader = baseFileReader;
        var initialOffset = _baseFileReader.Position;
        
        if (_blockHeader.BlockSizeExponent < 14 || _blockHeader.BlockSizeExponent > 32)
        {
            throw new InvalidDataException("Corrupted NCZBLOCK header: Block size must be between 14 and 32");
        }
        
        _blockSize = (long)Math.Pow(2, _blockHeader.BlockSizeExponent);
        
        _compressedBlockOffsetList = new List<long> { initialOffset };

        for (var i = 0; i < blockHeader.CompressedBlockSizeList.Length - 1; i++)
        {
            _compressedBlockOffsetList.Add(_compressedBlockOffsetList[^1] + _blockHeader.CompressedBlockSizeList[i]);
        }

        _compressedBlockSizeList = blockHeader.CompressedBlockSizeList;
    }

    public int Read(Span<byte> destination)
    {
        var buffer = new List<byte>();
        var blockOffset = _blockPosition % _blockSize;
        var blockId = (int)(_blockPosition / _blockSize);
        
        while (buffer.Count - blockOffset < destination.Length)
        {
            if (blockId >= _compressedBlockOffsetList.Count)
            {
                Console.WriteLine("BlockID exceeds the amounts of compressed blocks in that file!");
                break;
            }

            buffer.AddRange(DecompressBlock(blockId));
            blockId++;
        }
        
        var result = buffer.GetRange((int)blockOffset, destination.Length).ToArray();
        result.CopyTo(destination);

        _blockPosition += destination.Length;
        
        return destination.Length;
    }
    
    private byte[] DecompressBlock(int blockId)
    {
        if (_currentBlockId == blockId)
        {
            //Block already decompressed
            return _currentBlock;
        }
        
        var decompressedBlockSize = _blockSize;
        
        if (blockId >= _compressedBlockSizeList.Length - 1)
        {
            if (blockId >= _compressedBlockSizeList.Length)
            {
                throw new EndOfStreamException("BlockID exceeds the amounts of compressed blocks in that file!");
            }

            decompressedBlockSize = _blockHeader.DecompressedSize % _blockSize;
        }
        
        _baseFileReader.Seek(_compressedBlockOffsetList[blockId], SeekOrigin.Begin);
        _currentBlock = new byte[decompressedBlockSize];
        
        if (_compressedBlockSizeList[blockId] < decompressedBlockSize)
        {
            using var decompressor = new DecompressionStream(_baseFileReader);
            decompressor.ReadExactly(_currentBlock);
        }
        else
        {
            // ReSharper disable once MustUseReturnValue
            _baseFileReader.Read(_currentBlock, 0, (int)decompressedBlockSize);
        }

        _currentBlockId = blockId;
        return _currentBlock;
    }
}