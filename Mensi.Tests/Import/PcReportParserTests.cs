using Mensi.Core.Domain;
using Mensi.Core.Import;

namespace Mensi.Tests.Import;

public class PcReportParserTests
{
    /// <summary>Anonimizált sorok a Period Tracker riport szerkezetével: legújabb ciklus
    /// elöl ("Today" végű, ciklushossz nélkül), évszám csak ott, ahol a riport is írja,
    /// év-átfordulás december–január között.</summary>
    private static readonly string[] Sample =
    [
        "Name: X",
        "Cycle Summary",
        "Mar 2,2025 - Apr 18 (5 cycles)",
        "Cycle History",
        "Mar 2,2025 - Apr 18",
        "START - END DATE PERIOD CYCLE(DAY)",
        "Apr 2 - Today 5 Days",
        "Feb 28 - Apr 1 4 Days 33 Days",
        "Jan 24 - Feb 27 6 Days 35 Days",
        "Dec 20,2025 - Jan 23 4 Days 34 Days",
        "Mar 2,2025 - Dec 19,2025 3 Days 293 Days",
        "Ovulation test",
        "CYCLE DAY DATE&TIME RESULT PICTURE",
        "CD 12 Feb 4 Peak",
        "CD 10 Feb 2 High",
        "CD 8 Jan 31 Negative",
        "Period Tracker Page 1/10",
    ];

    [Fact]
    public void Cycles_are_chronological_with_inferred_years()
    {
        var data = PcReportParser.Parse(Sample);
        Assert.Equal(
            new[]
            {
                (new DateOnly(2025, 3, 2), 3),
                (new DateOnly(2025, 12, 20), 4),
                (new DateOnly(2026, 1, 24), 6),   // Dec→Jan átfordulás: év +1
                (new DateOnly(2026, 2, 28), 4),
                (new DateOnly(2026, 4, 2), 5),    // nyitott ("Today") ciklus is bekerül
            },
            data.Cycles.Select(c => (c.StartDate, c.PeriodDays)).ToArray());
        Assert.Empty(data.Warnings);
    }

    [Fact]
    public void Lh_tests_get_their_year_from_the_cycle_day_match()
    {
        var data = PcReportParser.Parse(Sample);
        // Jan 24-i ciklus: CD 8 = jan. 31., CD 10 = febr. 2., CD 12 = febr. 4.
        Assert.Equal(
            new[]
            {
                (new DateOnly(2026, 2, 4), LhTest.Peak),
                (new DateOnly(2026, 2, 2), LhTest.Positive),
                (new DateOnly(2026, 1, 31), LhTest.Negative),
            },
            data.LhTests.Select(t => (t.Date, t.Result)).ToArray());
    }

    [Fact]
    public void Unmatchable_lh_test_is_skipped_with_warning()
    {
        var lines = Sample.Append("CD 40 Sep 9 Peak").ToArray();
        var data = PcReportParser.Parse(lines);
        Assert.Equal(3, data.LhTests.Count);
        Assert.Contains(data.Warnings, w => w.Contains("CD 40"));
    }

    [Fact]
    public void Missing_year_anchor_yields_warning_and_no_cycles()
    {
        var data = PcReportParser.Parse(["Apr 2 - Today 5 Days"]);
        Assert.Empty(data.Cycles);
        Assert.Contains(data.Warnings, w => w.Contains("évszám"));
    }

    [Fact]
    public void Implausible_period_length_is_skipped_with_warning()
    {
        var lines = new[]
        {
            "Mar 2,2025 - Dec 19,2025 3 Days 293 Days",
            "Dec 20,2025 - Jan 23 22 Days 34 Days",
        };
        var data = PcReportParser.Parse(lines);
        Assert.Single(data.Cycles);
        Assert.Contains(data.Warnings, w => w.Contains("22 nap"));
    }
}
