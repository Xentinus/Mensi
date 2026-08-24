using Mensi.Core.Services;

namespace Mensi.Tests;

public class AuditWriterTests
{
    [Fact]
    public void Changes_json_is_camel_case_old_new_pairs()
    {
        var json = AuditWriter.BuildChangesJson(new Dictionary<string, (object?, object?)>
        {
            ["bbtCelsius"] = (36.40m, 36.42m),
            ["cervicalMucus"] = (null, Mensi.Core.Domain.CervicalMucus.EggWhite),
        });
        Assert.Equal(
            """{"bbtCelsius":{"old":36.40,"new":36.42},"cervicalMucus":{"old":null,"new":"eggWhite"}}""",
            json);
    }
}
