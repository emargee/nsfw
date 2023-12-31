using LibHac.Common;
using LibHac.Fs;
using LibHac.Fs.Fsa;
using LibHac.Ns;
using LibHac.Tools.Fs;
using LibHac.Tools.FsSystem;
using LibHac.Tools.FsSystem.NcaUtils;
using LibHac.Tools.Ncm;
using LibHac.Util;
using ContentType = LibHac.Ncm.ContentType;

namespace Nsfw.Commands;

public class NspStructure
{
    public Cnmt? Metadata { get; set; }
    public Dictionary<string, SwitchFsNca> NcaCollection { get; } = new (StringComparer.OrdinalIgnoreCase);
    public Dictionary<string, TitleStructure> Titles { get; }  = new (StringComparer.OrdinalIgnoreCase);
    public SwitchFsNca? MetaNca { get; set; }

    public void Build()
    {
        if (Metadata?.ContentEntries == null) return;
        
        foreach (var contentEntry in Metadata.ContentEntries)
        {
            if (!NcaCollection.ContainsKey(contentEntry.NcaId.ToHexString())) continue;

            var nca = NcaCollection[contentEntry.NcaId.ToHexString()];
            var titleId = nca.Nca.Header.TitleId.ToString("X16");

            if (!Titles.TryGetValue(titleId, out var titleStructure))
            {
                titleStructure = new TitleStructure();
            }

            var contentType = contentEntry.Type;

            if (contentType == ContentType.Control)
            {
                var romFs = nca.OpenFileSystem(NcaSectionType.Data, IntegrityCheckLevel.ErrorOnInvalid);

                using var control = new UniqueRef<IFile>();
                romFs.OpenFile(ref control.Ref, "/control.nacp"u8, OpenMode.Read).ThrowIfFailure();
                control.Get.Read(out _, 0, titleStructure.Control.ByteSpan).ThrowIfFailure();
            }

            if (contentType is ContentType.Program or ContentType.Data)
            {
                titleStructure.MainNca = nca;
            }

            if (!Titles.TryAdd(titleId, titleStructure))
            {
                Titles[titleId] = titleStructure;
            }
        }
    }

    public class TitleStructure
    {
        // ReSharper disable once NullableWarningSuppressionIsUsed
        public SwitchFsNca MainNca { get; set; } = null!;
        public BlitStruct<ApplicationControlProperty> Control { get; } = new(1);
        public string DisplayVersion => Control.Value.DisplayVersionString.ToString();
    }
}