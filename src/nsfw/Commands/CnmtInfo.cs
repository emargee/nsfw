using SQLite;

namespace Nsfw.Commands;

[Table("CnmtData")]
public class CnmtInfo
{
    [PrimaryKey]
    [AutoIncrement]
    public long Id { get; set; }
    [Indexed]
    public string TitleId { get; set; } = string.Empty;
    public string Version { get; set; } = string.Empty;
    public string NcaId { get; set; } = string.Empty;
    public int NcaType { get; set; }
}

public class DtoCnmtInfo
{
    public DtoCnmtContentEntry[] ContentEntries { get; set; } = Array.Empty<DtoCnmtContentEntry>();
    public string OtherApplicationId { get; set; } = string.Empty;
}

public class DtoCnmtContentEntry
{
    public string NcaId { get; set; } = string.Empty;
    public int Type { get; set; }
}