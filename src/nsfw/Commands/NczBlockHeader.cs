namespace Nsfw.Commands;

public class NczBlockHeader
{
    public int Version { get; set; }
    
    public int Type { get; set; }
    
    public int BlockSizeExponent { get; set; }
    
    public int NumberOfBlocks { get; set; }
    
    public long DecompressedSize { get; set; }

    public int[] CompressedBlockSizeList { get; set; } = [];
}