using Mensi.Core.Domain;
using Mensi.Core.Prediction;

namespace Mensi.Tests.Prediction;

public class PredictionEngineTests
{
    private static readonly DateOnly Start = new(2026, 8, 10);

    private static ClosedCycleStat Closed(int offsetDays, int len, int? luteal) =>
        new(Start.AddDays(offsetDays), len, luteal, false, null);

    /// <summary>3 lezárt ciklus (28/27/29, luteális 13/14/13) + nyitott ciklus a 14. napon,
    /// LH-csúccsal a 13. napon és egy védekezés nélküli együttléttel a 12. napon.</summary>
    private static EngineInput Input(DateOnly? today = null, bool lhPeak = true)
    {
        var logs = new List<DailyLogSnapshot>();
        for (var d = 1; d <= 14; d++)
        {
            logs.Add(new DailyLogSnapshot(
                Start.AddDays(d - 1),
                Bbt: 36.30m + (d % 3) * 0.03m,
                Mucus: d >= 11 ? CervicalMucus.EggWhite : null,
                Lh: lhPeak && d == 13 ? LhTest.Peak : null,
                CrampType: null, CrampSeverity: null,
                Flow: d <= 5 ? FlowIntensity.Medium : null,
                PeriodStart: d == 1,
                IntercourseCount: d == 12 ? 1 : 0,
                UnprotectedCount: d == 12 ? 1 : 0));
        }
        return new EngineInput(
            [Closed(-84, 28, 13), Closed(-56, 27, 14), Closed(-29, 29, 13)],
            Start, logs, today ?? Start.AddDays(13));
    }

    [Fact]
    public void No_closed_cycles_yields_null() =>
        Assert.Null(PredictionEngine.Evaluate(new EngineInput([], Start, [], Start)));

    [Fact]
    public void Windows_are_ordered_and_lh_peak_centers_ovulation()
    {
        var p = PredictionEngine.Evaluate(Input())!;
        Assert.True(p.OvulationFrom <= p.OvulationP50);
        Assert.True(p.OvulationP50 <= p.OvulationTo);
        Assert.True(p.PeriodFrom <= p.PeriodTo);
        Assert.True(p.OvulationTo < p.PeriodFrom);
        // LH-csúcs a 13. napon → a medián a 12–15. ciklusnap környékén
        var p50Day = p.OvulationP50.DayNumber - Start.DayNumber + 1;
        Assert.InRange(p50Day, 12, 15);
        Assert.Equal(14, p.CycleDay);
    }

    [Fact]
    public void Timing_reflects_logged_intercourse()
    {
        var p = PredictionEngine.Evaluate(Input())!;
        Assert.True(p.Chance > 0);
        Assert.NotEqual(TimingLabel.Weak, p.Timing);
    }

    [Fact]
    public void Categorize_maps_the_whole_cycle()
    {
        var p = PredictionEngine.Evaluate(Input())!;
        Assert.Equal(DayCategory.Menstruation, p.Categorize(Start));            // 1. nap, flow
        Assert.Equal(DayCategory.Ovulation, p.Categorize(p.OvulationP50));
        Assert.Equal(DayCategory.PredictedPeriod, p.Categorize(p.PeriodP50));
        Assert.Equal(DayCategory.Luteal, p.Categorize(p.OvulationTo.AddDays(1)));
        Assert.Equal(5, p.MenstruationEndDay);
    }

    [Fact]
    public void Headline_and_phase_follow_todays_category()
    {
        var p = PredictionEngine.Evaluate(Input())!;
        Assert.False(string.IsNullOrWhiteSpace(p.Headline));
        Assert.Equal(p.Categorize(Start.AddDays(13)), p.Phase.Key);
        Assert.True(p.Phase.TotalDays >= p.Phase.ElapsedDays);
    }

    [Fact]
    public void No_pregnancy_hint_mid_cycle() =>
        Assert.Null(PredictionEngine.Evaluate(Input())!.PregnancyHint);

    [Fact]
    public void Pregnancy_hint_when_period_is_late_and_bbt_stays_high()
    {
        // nyitott ciklus 34. napja: 15. naptól magas BBT (megerősített shift), nincs vérzés
        var logs = new List<DailyLogSnapshot>();
        for (var d = 1; d <= 34; d++)
        {
            logs.Add(new DailyLogSnapshot(
                Start.AddDays(d - 1),
                Bbt: d <= 14 ? 36.35m : 36.70m,
                Mucus: null, Lh: null, CrampType: null, CrampSeverity: null,
                Flow: d <= 5 ? FlowIntensity.Medium : null,
                PeriodStart: d == 1, IntercourseCount: 0, UnprotectedCount: 0));
        }
        var input = new EngineInput(
            [Closed(-84, 28, 13), Closed(-56, 27, 14), Closed(-29, 29, 13)],
            Start, logs, Start.AddDays(33));
        var p = PredictionEngine.Evaluate(input)!;
        Assert.NotNull(p.PregnancyHint);
    }

    [Fact]
    public void Closed_cycle_categorizer_uses_flow_and_ovulation_day()
    {
        var flow = new HashSet<DateOnly> { Start, Start.AddDays(1), Start.AddDays(2) };
        Assert.Equal(DayCategory.Menstruation,
            DayCategorizer.CategorizeClosed(Start, Start, 28, 14, flow));
        Assert.Equal(DayCategory.Ovulation,
            DayCategorizer.CategorizeClosed(Start.AddDays(13), Start, 28, 14, flow)); // 14. nap
        Assert.Equal(DayCategory.Fertile,
            DayCategorizer.CategorizeClosed(Start.AddDays(9), Start, 28, 14, flow));  // 10. nap
        Assert.Equal(DayCategory.Luteal,
            DayCategorizer.CategorizeClosed(Start.AddDays(20), Start, 28, 14, flow));
        Assert.Equal(DayCategory.Follicular,
            DayCategorizer.CategorizeClosed(Start.AddDays(5), Start, 28, 14, flow));
    }
}
