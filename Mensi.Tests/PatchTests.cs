using System.Text.Json;
using System.Text.Json.Serialization;
using Mensi.Core.Api;

namespace Mensi.Tests;

public class PatchTests
{
    private sealed record Body
    {
        public Patch<decimal?> Bbt { get; init; } = new();
        public Patch<string?> Note { get; init; } = new();
    }

    private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web)
    {
        Converters = { new PatchJsonConverterFactory(), new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) }
    };

    [Fact]
    public void Missing_key_is_not_set()
    {
        var body = JsonSerializer.Deserialize<Body>("""{}""", Options)!;
        Assert.False(body.Bbt.IsSet);
    }

    [Fact]
    public void Null_value_is_set_with_null()
    {
        var body = JsonSerializer.Deserialize<Body>("""{"bbt":null}""", Options)!;
        Assert.True(body.Bbt.IsSet);
        Assert.Null(body.Bbt.Value);
    }

    [Fact]
    public void Value_is_set_with_value()
    {
        var body = JsonSerializer.Deserialize<Body>("""{"bbt":36.42}""", Options)!;
        Assert.True(body.Bbt.IsSet);
        Assert.Equal(36.42m, body.Bbt.Value);
    }
}
