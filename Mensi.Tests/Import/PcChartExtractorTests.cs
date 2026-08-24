using Mensi.Core.Domain;
using Mensi.Core.Import;

namespace Mensi.Tests.Import;

public class PcChartExtractorTests
{
    private static (IReadOnlyList<PcDailyData> Daily, IReadOnlyList<string> Warnings) ExtractFixture()
    {
        var bytes = File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "Fixtures", "pc-report-fixture.pdf"));
        var cycles = PcReportParser.Parse(PdfTextLines.Extract(bytes)).Cycles;
        return PcChartExtractor.Extract(bytes, cycles);
    }

    private static readonly DateOnly C = new(2026, 2, 28); // a grafikon-oldal ciklusa

    [Fact]
    public void Chart_dots_map_to_days_and_fields()
    {
        var (daily, warnings) = ExtractFixture();
        Assert.Empty(warnings);
        var byDate = daily.ToDictionary(d => d.Date);

        Assert.Equal(FlowIntensity.Light, byDate[C].Flow);                        // 1. nap
        Assert.Equal(FlowIntensity.Heavy, byDate[C.AddDays(1)].Flow);             // 2. nap
        Assert.True(byDate[C.AddDays(1)].Cramps);
        Assert.Equal([Mood.Sad], byDate[C.AddDays(1)].Moods);
        // A grafikonból visszamért hőmérséklet pontossága ±0,01–0,02 °C.
        Assert.InRange(byDate[C.AddDays(9)].Bbt!.Value, 36.58m, 36.62m);          // 10. nap ~36,60
        Assert.InRange(byDate[C.AddDays(10)].Bbt!.Value, 36.38m, 36.42m);         // 11. nap ~36,40
        Assert.Equal(LhTest.Negative, byDate[C.AddDays(9)].Lh);                   // Low
        Assert.Equal(1, byDate[C.AddDays(7)].ProtectedSex);                       // 8. nap
        Assert.Equal(1, byDate[C.AddDays(11)].UnprotectedSex);                    // 12. nap
        Assert.Equal(CervicalMucus.Creamy, byDate[C.AddDays(10)].Mucus);          // Watery → nedves
        Assert.Equal(CervicalMucus.EggWhite, byDate[C.AddDays(12)].Mucus);        // 13. nap
        Assert.Equal([Mood.Longing], byDate[C.AddDays(12)].Moods);                // Horny
        Assert.Equal(LhTest.Peak, byDate[C.AddDays(13)].Lh);                      // 14. nap
        Assert.Equal([Mood.Cheerful], byDate[C.AddDays(13)].Moods);               // Happy
        Assert.Equal(1, byDate[C.AddDays(13)].UnprotectedSex);
        Assert.True(byDate[C.AddDays(19)].Spotting);                              // 20. nap
        Assert.False(byDate.ContainsKey(C.AddDays(2)));                           // Ill → nincs megfelelő
    }

    [Fact]
    public void Chart_extraction_is_limited_to_known_rows()
    {
        var (daily, _) = ExtractFixture();
        // pontosan a lerakott, leképezhető jelek napjai jelennek meg
        Assert.Equal(
            new[] { 1, 2, 8, 10, 11, 12, 13, 14, 20 },
            daily.Select(d => d.Date.DayNumber - C.DayNumber + 1).ToArray());
    }
}
