using Mensi.Core.Domain;
using Mensi.Core.Prediction;

namespace Mensi.Tests.Prediction;

public class CycleDeriverTests
{
    private static DailyLogSnapshot Day(DateOnly date, bool periodStart = false,
        decimal? bbt = null, FlowIntensity? flow = null) =>
        new(date, bbt, null, null, null, null, flow, periodStart, 0, 0);

    private static readonly DateOnly C1 = new(2026, 6, 1);

    [Fact]
    public void Period_start_days_split_cycles_and_last_stays_open()
    {
        var logs = new List<DailyLogSnapshot>
        {
            Day(C1, periodStart: true, flow: FlowIntensity.Heavy),
            Day(C1.AddDays(28), periodStart: true, flow: FlowIntensity.Medium),
            Day(C1.AddDays(28 + 27), periodStart: true, flow: FlowIntensity.Medium),
        };
        var cycles = CycleDeriver.Derive(logs);
        Assert.Equal(3, cycles.Count);
        Assert.Equal(28, cycles[0].LengthDays);
        Assert.Equal(27, cycles[1].LengthDays);
        Assert.Null(cycles[2].LengthDays); // nyitott
    }

    [Fact]
    public void Confirmed_bbt_shift_sets_luteal_and_estimate()
    {
        var logs = new List<DailyLogSnapshot> { Day(C1, periodStart: true, flow: FlowIntensity.Heavy) };
        // 2..13. nap alacsony, 14..16. nap magas → ovuláció a 13. napon
        for (var d = 2; d <= 13; d++) logs.Add(Day(C1.AddDays(d - 1), bbt: 36.30m + (d % 4) * 0.03m));
        for (var d = 14; d <= 27; d++) logs.Add(Day(C1.AddDays(d - 1), bbt: 36.70m));
        logs.Add(Day(C1.AddDays(28), periodStart: true, flow: FlowIntensity.Medium)); // 28 napos ciklus

        var cycles = CycleDeriver.Derive(logs);
        Assert.Equal(13, cycles[0].OvulationConfirmed);
        Assert.Equal(13, cycles[0].OvulationEstimated);
        Assert.Equal(28 - 13, cycles[0].LutealLength);
        Assert.False(cycles[0].Anovulatory);
    }

    [Fact]
    public void Enough_bbt_without_shift_marks_anovulatory()
    {
        var logs = new List<DailyLogSnapshot> { Day(C1, periodStart: true, flow: FlowIntensity.Heavy) };
        for (var d = 2; d <= 27; d++) logs.Add(Day(C1.AddDays(d - 1), bbt: 36.35m)); // sima, nincs shift
        logs.Add(Day(C1.AddDays(28), periodStart: true, flow: FlowIntensity.Medium));

        var c = CycleDeriver.Derive(logs)[0];
        Assert.True(c.Anovulatory);
        Assert.Null(c.LutealLength);
        Assert.Equal(28 - 14, c.OvulationEstimated); // fallback: hossz − 14
    }

    [Fact]
    public void Sparse_bbt_is_not_judged_anovulatory()
    {
        var logs = new List<DailyLogSnapshot>
        {
            Day(C1, periodStart: true, flow: FlowIntensity.Heavy),
            Day(C1.AddDays(5), bbt: 36.40m),
            Day(C1.AddDays(28), periodStart: true, flow: FlowIntensity.Medium),
        };
        Assert.False(CycleDeriver.Derive(logs)[0].Anovulatory);
    }

    [Fact]
    public void Length_predictor_rounds_the_shrunken_ewma()
    {
        // EWMA(28, 30) = 28.54; shrink(pop 28/16, n=2, s²=2) ≈ 28.51 → 29
        var predicted = LengthPredictor.Predict([
            new ClosedCycleStat(C1, 28, null, false, null),
            new ClosedCycleStat(C1.AddDays(28), 30, null, false, null)]);
        Assert.Equal(29, predicted);
        Assert.Null(LengthPredictor.Predict([]));
    }
}
