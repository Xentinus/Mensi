using Mensi.Core.Domain;
using Mensi.Core.Prediction;

namespace Mensi.Tests.Prediction;

public class CycleProjectorTests
{
    private static readonly DateOnly FirstStart = new(2026, 9, 1);

    private static IReadOnlyList<ProjectedCycle> Project() =>
        CycleProjector.Project(FirstStart, cycleMean: 30, lutealMean: 14, menstruationDays: 5);

    [Fact]
    public void Cycles_chain_without_gap_or_overlap()
    {
        var cycles = Project();
        Assert.Equal(CycleProjector.Cycles, cycles.Count);
        Assert.Equal(FirstStart, cycles[0].Start);
        for (var i = 1; i < cycles.Count; i++)
            Assert.Equal(cycles[i - 1].End.AddDays(1), cycles[i].Start);
    }

    [Fact]
    public void Every_day_of_a_projected_cycle_has_a_category()
    {
        var c = Project()[0];
        for (var date = c.Start; date <= c.End; date = date.AddDays(1))
            Assert.NotEqual(DayCategory.Unknown, c.Categorize(date));
        Assert.Equal(DayCategory.Unknown, c.Categorize(c.Start.AddDays(-1)));
        Assert.Equal(DayCategory.Unknown, c.Categorize(c.End.AddDays(1)));
    }

    [Fact]
    public void Ovulation_sits_a_luteal_phase_before_the_next_start()
    {
        var c = Project()[0];
        // 30 napos ciklus, 14 napos luteális fázis → a 16. ciklusnap ±1.
        Assert.Equal(FirstStart.AddDays(14), c.OvulationFrom);
        Assert.Equal(FirstStart.AddDays(16), c.OvulationTo);
        Assert.Equal(DayCategory.Ovulation, c.Categorize(FirstStart.AddDays(15)));
        Assert.Equal(DayCategory.Fertile, c.Categorize(FirstStart.AddDays(11)));
        Assert.Equal(DayCategory.Luteal, c.Categorize(FirstStart.AddDays(20)));
        Assert.Equal(DayCategory.PredictedPeriod, c.Categorize(FirstStart.AddDays(4)));
        Assert.Equal(DayCategory.Follicular, c.Categorize(FirstStart.AddDays(5)));
    }

    [Fact]
    public void Degenerate_parameters_stay_inside_the_cycle()
    {
        // Rövidebb ciklus, mint a luteális fázis és hosszabb vérzés, mint a ciklus:
        // a becslés így sem eshet a ciklushatárokon kívülre.
        var c = CycleProjector.Project(FirstStart, cycleMean: 5, lutealMean: 14, menstruationDays: 40)[0];
        Assert.True(c.OvulationFrom <= c.End);
        Assert.True(c.MenstruationTo < c.Start.AddDays(15));
        Assert.Equal(FirstStart.AddDays(14), c.End);
    }
}
