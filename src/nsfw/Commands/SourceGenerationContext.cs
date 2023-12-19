using System.Text.Json.Serialization;

namespace Nsfw.Commands;

[JsonSourceGenerationOptions(WriteIndented = true, PropertyNameCaseInsensitive = true, AllowTrailingCommas = true)]
[JsonSerializable(typeof(Dictionary<string,GameInfo>))]
[JsonSerializable(typeof(Dictionary<string,Dictionary<string, string>>))]
[JsonSerializable(typeof(Dictionary<string, Dictionary<string, DtoCnmtInfo>>))]
internal partial class SourceGenerationContext : JsonSerializerContext
{
}