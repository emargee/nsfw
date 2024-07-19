namespace Nsfw.Commands;

public class NczSection
{
    public long Offset { get; set; }
    public long Size { get; set; }
    public long CryptoType { get; set; }
    public byte[] CryptoKey { get; set; } = [];
    public byte[] CryptoCounter { get; set; } = [];
}