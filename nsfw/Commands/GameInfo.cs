using System.Text.Json;
using System.Text.Json.Serialization;
using SQLite;

namespace Nsfw.Commands;

public class GameInfo
{
    [PrimaryKey]
    [AutoIncrement]
    public long GameId { get; set; }
    [Indexed] 
    public long NsuId { get; set; }
    public string? BannerUrl { get; set; }
    [JsonConverter(typeof(ArrayToStringConverter))]
    public string Category { get; set; } = string.Empty;
    public string? Description { get; set; }
    public string? Developer { get; set; }
    public string? FrontBoxArt { get; set; }
    public string? IconUrl { get; set; }
    [Indexed]
    public string? Id { get; set; }
    public string? Intro { get; set; }
    public bool IsDemo { get; set; }
    public string? Key { get; set; }
    [JsonConverter(typeof(ArrayToStringConverter))]
    public string? Languages { get; set; }
    [Indexed]
    public string? Name { get; set; }
    public int? NumberOfPlayers { get; set; }
    public string? Publisher { get; set; }
    public int? Rating { get; set; }
    [JsonConverter(typeof(ArrayToStringConverter))]
    public string? RatingContent { get; set; }
    public string? Region { get; set; }
    public int? ReleaseDate { get; set; }
    public string? RightsId { get; set; }
    [JsonConverter(typeof(ArrayToStringConverter))]
    public string? Screenshots { get; set; }
    public long? Size { get; set; }
    public string? Version { get; set; }
    public string RegionLanguage { get; set; } = "Unknown";
}

public class LongToStringConverter : JsonConverter<string>
{
    public override string Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType != JsonTokenType.Number)
        {
            throw new JsonException();
        }

        return reader.GetInt64().ToString();
    }

    public override void Write(Utf8JsonWriter writer, string value, JsonSerializerOptions options)
    {
        throw new NotImplementedException();
    }
}

public class ArrayToStringConverter : JsonConverter<string>
{
    public override string Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType != JsonTokenType.StartArray)
        {
            throw new JsonException();
        }
        
        var list = new List<string>();
        
        reader.Read();
        
        while (reader.TokenType != JsonTokenType.EndArray)
        {
            list.Add(reader.GetString());

            reader.Read();
        }

        return string.Join(",", list);
    }

    public override void Write(Utf8JsonWriter writer, string value, JsonSerializerOptions options)
    {
        throw new NotImplementedException();
    }
}