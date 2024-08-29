using LibHac;
using LibHac.Fs;
using LibHac.Fs.Fsa;
using LibHac.Util;
using Serilog;

namespace Nsfw.Commands;

public class DecompressNczFile(Ncz ncz) : IFile
{
    private long _readOffset;
    
    protected override Result DoRead(out long bytesRead, long offset, Span<byte> destination, in ReadOption option)
    {
        var slice = destination[..destination.Length]; // Full slice 
        
        if (_readOffset < Ncz.UncompressableHeaderSize)
        {
            ncz.UncompressableHeader.AsSpan(0, Ncz.UncompressableHeaderSize).CopyTo(destination.Slice(0, Ncz.UncompressableHeaderSize));
            _readOffset += Ncz.UncompressableHeaderSize;
            bytesRead = Ncz.UncompressableHeaderSize;
            slice = destination.Slice(Ncz.UncompressableHeaderSize, destination.Length - Ncz.UncompressableHeaderSize); // Full slice
        }
        
        bytesRead = ncz.DecompressChunk(_readOffset, slice);
        
        _readOffset += bytesRead;
        
        // Check hash validity on last block..
        if (_readOffset == ncz.DecompressedSize)
        {
            if(!ncz.IsValid())
            {
                throw new InvalidDataException($"Current hash ({ncz.CurrentHash}) does not match original hash ({ncz.TargetHash.ToUpper()}). File is corrupted.");
            }
        }
        
        return Result.Success;
    }

    protected override Result DoWrite(long offset, ReadOnlySpan<byte> source, in WriteOption option)
    {
        Console.WriteLine("Write");
        return Result.Success;
    }

    protected override Result DoFlush()
    {
        Console.WriteLine("Flush");
        return Result.Success;
    }

    protected override Result DoSetSize(long size)
    {
        Console.WriteLine("Set Size");
        return Result.Success;
    }

    protected override Result DoGetSize(out long size)
    {
        size = ncz.DecompressedSize;
        return Result.Success;
    }

    protected override Result DoOperateRange(Span<byte> outBuffer, OperationId operationId, long offset, long size, ReadOnlySpan<byte> inBuffer)
    {
        Console.WriteLine("Operate Range");
        return Result.Success;
    }
}