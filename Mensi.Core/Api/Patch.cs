using System.Text.Json;
using System.Text.Json.Serialization;

namespace Mensi.Core.Api;

/// <summary>
/// Részleges upsert mező: megkülönbözteti a "nem küldött" és a "null-ra állított" esetet.
/// JSON-ban jelen lévő kulcs → IsSet=true (Value lehet null is); hiányzó kulcs → IsSet=false.
/// </summary>
public sealed record Patch<T>
{
    public bool IsSet { get; init; }
    public T? Value { get; init; }

    public static Patch<T> Of(T? value) => new() { IsSet = true, Value = value };
}

public sealed class PatchJsonConverterFactory : JsonConverterFactory
{
    public override bool CanConvert(Type typeToConvert) =>
        typeToConvert.IsGenericType && typeToConvert.GetGenericTypeDefinition() == typeof(Patch<>);

    public override JsonConverter CreateConverter(Type typeToConvert, JsonSerializerOptions options) =>
        (JsonConverter)Activator.CreateInstance(
            typeof(PatchConverter<>).MakeGenericType(typeToConvert.GetGenericArguments()[0]))!;

    private sealed class PatchConverter<T> : JsonConverter<Patch<T>>
    {
        public override bool HandleNull => true;

        public override Patch<T> Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) =>
            Patch<T>.Of(JsonSerializer.Deserialize<T>(ref reader, options));

        public override void Write(Utf8JsonWriter writer, Patch<T> value, JsonSerializerOptions options) =>
            JsonSerializer.Serialize(writer, value.Value, options);
    }
}
