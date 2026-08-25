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

    /// <summary>A bejelentett eset: 26–42 nap között szóró ciklusok, a 34. napon, vérzés nélkül.
    /// A naptár-modell itt jogosan széles — a szűkítést az LH-arányoknak kell hozniuk.</summary>
    private static EngineInput Irregular(IReadOnlyList<(int Day, decimal Lh)> lhReadings)
    {
        int[] lengths = [28, 30, 35, 26, 38, 40, 29, 42];
        var closed = new List<ClosedCycleStat>();
        var cursor = Start.AddDays(-lengths.Sum());
        foreach (var len in lengths)
        {
            closed.Add(new ClosedCycleStat(cursor, len, null, false, null));
            cursor = cursor.AddDays(len);
        }

        var byDay = lhReadings.ToDictionary(r => r.Day, r => r.Lh);
        var logs = Enumerable.Range(1, 34).Select(d => new DailyLogSnapshot(
            Start.AddDays(d - 1), Bbt: null, Mucus: null, Lh: null,
            CrampType: null, CrampSeverity: null,
            Flow: d <= 4 ? FlowIntensity.Medium : null,
            PeriodStart: d == 1, IntercourseCount: 0, UnprotectedCount: 0,
            LhValue: byDay.TryGetValue(d, out var v) ? v : null)).ToList();

        return new EngineInput(closed, Start, logs, Start.AddDays(33));
    }

    private static int Width(DateOnly from, DateOnly to) => to.DayNumber - from.DayNumber + 1;

    [Fact]
    public void Irregular_cycles_stay_honest_but_lh_readings_narrow_the_bands()
    {
        var bare = PredictionEngine.Evaluate(Irregular([]))!;
        // A luteális szórás korábban kétszer került bele (prior + konvolúció) — a sáv
        // ettől szisztematikusan szélesebb volt, mint amit a ciklushossz-szórás indokol.
        Assert.InRange(Width(bare.PeriodFrom, bare.PeriodTo), 1, 10);
        Assert.Equal(ConfidenceLevel.Low, bare.Confidence);
        Assert.NotNull(bare.MeasurementHint);

        // Ugyanaz a ciklus, csak felvitt csíkarányokkal: a 22. napi maximum köré húzódik minden.
        var measured = PredictionEngine.Evaluate(Irregular(
            [(16, 0.10m), (18, 0.12m), (20, 0.18m), (22, 0.45m), (23, 0.30m),
             (25, 0.12m), (27, 0.10m), (30, 0.10m), (33, 0.10m)]))!;

        Assert.True(Width(measured.PeriodFrom, measured.PeriodTo)
            < Width(bare.PeriodFrom, bare.PeriodTo));
        Assert.True(Width(measured.OvulationFrom, measured.OvulationTo)
            < Width(bare.OvulationFrom, bare.OvulationTo));
        Assert.InRange(measured.OvulationP50.DayNumber - Start.DayNumber + 1, 21, 25);
        Assert.Null(measured.MeasurementHint); // van biomarker, nem a mérést kell sürgetni
    }

    [Fact]
    public void Future_cycles_are_projected_beyond_the_predicted_period()
    {
        var p = PredictionEngine.Evaluate(Input())!;
        Assert.NotEmpty(p.Future);
        Assert.Equal(p.PeriodP50, p.Future[0].Start);

        var wellAhead = p.PeriodTo.AddDays(120);
        Assert.NotEqual(DayCategory.Unknown, p.Categorize(wellAhead));
        Assert.NotNull(p.ProjectedCycleDay(wellAhead));
        // A nyitott ciklus sávján belül még a posterior dönt, nem az előrevetítés.
        Assert.Null(p.ProjectedFor(p.OvulationP50));
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
    public void Overdue_cycle_conditions_on_no_period_yet()
    {
        // 33. nap, semmilyen biomarker: a "még nincs menstruáció" evidencia miatt a
        // menstruáció-sáv nem kezdődhet a múltban, és az ovuláció-posterior későbbre tolódik.
        var logs = new List<DailyLogSnapshot>();
        for (var d = 1; d <= 33; d++)
        {
            logs.Add(new DailyLogSnapshot(
                Start.AddDays(d - 1), null, null, null, null, null,
                d <= 5 ? FlowIntensity.Medium : null, d == 1, 0, 0));
        }
        var today = Start.AddDays(32); // 33. ciklusnap
        var input = new EngineInput(
            [Closed(-84, 28, 13), Closed(-56, 27, 14), Closed(-29, 29, 13)],
            Start, logs, today);
        var p = PredictionEngine.Evaluate(input)!;

        Assert.True(p.PeriodFrom >= today);
        // ~28 napos történet mellett a feltétel nélküli ovuláció-medián ~14-15 lenne;
        // a survival-súlyozás után legalább 33−18 = 15 fölé kell tolódnia.
        var p50Day = p.OvulationP50.DayNumber - Start.DayNumber + 1;
        Assert.True(p50Day >= 15);
        Assert.Null(p.PregnancyHint); // nincs BBT-adat → egyik szabály sem sülhet el
    }

    [Fact]
    public void Measurement_hint_appears_on_low_confidence_without_biomarkers()
    {
        // szabálytalan történet (26/40/33) + nulla biomarker → széles sáv → mérési javaslat
        var logs = new List<DailyLogSnapshot>
        {
            new(Start, null, null, null, null, null, FlowIntensity.Medium, true, 0, 0),
        };
        var input = new EngineInput(
            [Closed(-99, 26, null), Closed(-73, 40, null), Closed(-33, 33, null)],
            Start, logs, Start.AddDays(9));
        var p = PredictionEngine.Evaluate(input)!;
        Assert.Equal(ConfidenceLevel.Low, p.Confidence);
        Assert.NotNull(p.MeasurementHint);
    }

    [Fact]
    public void Measurement_hint_is_absent_when_biomarkers_exist()
    {
        var p = PredictionEngine.Evaluate(Input())!; // a fixture LH-csúcsot és BBT-t is tartalmaz
        Assert.Null(p.MeasurementHint);
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
