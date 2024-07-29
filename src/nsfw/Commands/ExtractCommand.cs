using LibHac.Common;
using LibHac.Fs;
using LibHac.Fs.Fsa;
using LibHac.FsSystem;
using LibHac.Tools.FsSystem;
using Spectre.Console.Cli;
using Path = System.IO.Path;

namespace Nsfw.Commands;

public class ExtractCommand : Command<ExtractSettings>
{
    public override int Execute(CommandContext context, ExtractSettings settings)
    {
        var nspFilename = Path.GetFileName(settings.NszFile).Replace(Path.GetExtension(settings.NszFile), ".nsp");
        var outputNsp = Path.Combine(settings.OutDirectory, nspFilename);

        Console.WriteLine("NSZ File        : {0}", settings.NszFile);
        Console.WriteLine("NSP File        : {0}", outputNsp);

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

                if (fileHash.Length != 32)
                {
                    Console.WriteLine("Filename of NCZ is used as verifying hash, but it's not 32 characters long. Cannot validate.");
                    return 1;
                }

                var ncz = new Ncz(file.Release().AsStream(), fileHash.ToUpperInvariant());

                var decompFile = new DecompressNczFile(ncz);
                
                builder.AddFile(fileHash + ".nca", decompFile);
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
            Console.WriteLine(exception.StackTrace);
            File.Delete(outputNsp);
        }

        return 0;
    }
}