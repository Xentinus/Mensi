using Mensi.Core.Domain;
using Mensi.Core.Import;

namespace Mensi.Tests.Import;

public class PdfTextLinesTests
{
    [Fact]
    public void Fixture_pdf_extracts_and_parses_end_to_end()
    {
        var bytes = File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "Fixtures", "pc-report-fixture.pdf"));
        var data = PcReportParser.Parse(PdfTextLines.Extract(bytes));

        Assert.Equal(5, data.Cycles.Count);
        Assert.Equal(new DateOnly(2025, 3, 2), data.Cycles[0].StartDate);
        Assert.Equal(new DateOnly(2026, 4, 2), data.Cycles[^1].StartDate);
        Assert.Equal(3, data.LhTests.Count);
        Assert.Contains(data.LhTests, t => t.Date == new DateOnly(2026, 2, 4) && t.Result == LhTest.Peak);
        Assert.Empty(data.Warnings);
    }
}
