using Mensi.Core.Prediction;

namespace Mensi.Tests.Prediction;

public class CycleStatsTests
{
    private static ClosedCycleStat C(int startOffset, int len, int? luteal = null,
        bool anov = false, int? predicted = null) =>
        new(new DateOnly(2026, 1, 1).AddDays(startOffset), len, luteal, anov, predicted);

    [Fact]
    public void Empty_returns_null() => Assert.Null(CycleStats.Compute([]));

    [Fact]
    public void Single_cycle_seeds_everything()
    {
        var s = CycleStats.Compute([C(0, 28)])!;
        Assert.Equal(1, s.ClosedCount);
        Assert.Equal(28, s.EwmaLength);
        Assert.Equal(28, s.MeanLength);
        Assert.Equal(0, s.StdDevLength); // n<2: definíció szerint 0
        Assert.Equal(28, s.MedianLength);
    }

    [Fact]
    public void Ewma_weights_the_latest_cycle_by_alpha()
    {
        // 0.27·30 + 0.73·28 = 28.54
        var s = CycleStats.Compute([C(0, 28), C(28, 30)])!;
        Assert.Equal(28.54, s.EwmaLength, 3);
        Assert.Equal(29, s.MeanLength);
        Assert.Equal(Math.Sqrt(2), s.StdDevLength, 6);
    }

    [Fact]
    public void Anovulatory_cycle_updates_ewma_with_half_weight()
    {
        // effektív alfa 0.135: 28 + 0.135·(30−28) = 28.27
        var s = CycleStats.Compute([C(0, 28), C(28, 30, anov: true)])!;
        Assert.Equal(28.27, s.EwmaLength, 3);
    }

    [Fact]
    public void Luteal_stats_use_only_confirmed_cycles()
    {
        var s = CycleStats.Compute([C(0, 28, 13), C(28, 30, 14), C(58, 27)])!;
        Assert.Equal(13.5, s.MeanLuteal!.Value, 3);
        Assert.Equal(2, s.ConfirmedLutealCount);
    }

    [Fact]
    public void Delay_percentiles_use_nearest_rank()
    {
        // delayek: 28−27=1, 28−28=0, 27−30=−3 → rendezve [−3, 0, 1]
        var s = CycleStats.Compute([C(0, 28, predicted: 27), C(28, 28, predicted: 28), C(56, 27, predicted: 30)])!;
        Assert.Equal((-3, 0, 1), s.Delay);
    }

    [Fact]
    public void Delay_is_null_without_predictions()
    {
        Assert.Null(CycleStats.Compute([C(0, 28)])!.Delay);
    }
}
