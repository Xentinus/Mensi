using Mensi.Core.Domain;
using Mensi.Core.Prediction;

namespace Mensi.Tests.Prediction;

public class WilcoxKernelTests
{
    [Theory]
    [InlineData(-5, 0.10)]
    [InlineData(-1, 0.31)]
    [InlineData(0, 0.33)]
    [InlineData(1, 0.0)]
    [InlineData(-6, 0.0)]
    public void Kernel_matches_published_values(int rel, double expected) =>
        Assert.Equal(expected, WilcoxKernel.DayProbability(rel), 9);

    [Fact]
    public void Single_intercourse_day_before_point_mass_ovulation()
    {
        var chance = WilcoxKernel.CycleChance(Posterior.FromPointMass(14), [13]);
        Assert.Equal(0.31, chance, 9);
        Assert.Equal(TimingLabel.Good, WilcoxKernel.Label(chance));
    }

    [Fact]
    public void Multiple_days_combine_with_complement_product()
    {
        // 1 − (1−0.31)(1−0.33) = 0.5377
        var chance = WilcoxKernel.CycleChance(Posterior.FromPointMass(14), [13, 14]);
        Assert.Equal(0.5377, chance, 4);
    }

    [Fact]
    public void No_intercourse_is_zero_and_weak()
    {
        Assert.Equal(0, WilcoxKernel.CycleChance(Posterior.FromPointMass(14), []));
        Assert.Equal(TimingLabel.Weak, WilcoxKernel.Label(0));
    }

    [Theory]
    [InlineData(0.079, TimingLabel.Weak)]
    [InlineData(0.08, TimingLabel.Medium)]
    [InlineData(0.16, TimingLabel.Medium)]
    [InlineData(0.161, TimingLabel.Good)]
    public void Label_thresholds(double chance, TimingLabel expected) =>
        Assert.Equal(expected, WilcoxKernel.Label(chance));

    [Fact]
    public void What_if_improving_today_and_tomorrow_names_both()
    {
        var hint = WilcoxKernel.WhatIfHint(Posterior.FromPointMass(14), [], 13, 15);
        Assert.Equal("Ha ma vagy holnap van együttlét, a minősítés Jó lesz.", hint);
    }

    [Fact]
    public void What_if_after_fertile_window_is_null()
    {
        Assert.Null(WilcoxKernel.WhatIfHint(Posterior.FromPointMass(14), [], 16, 15));
    }

    [Fact]
    public void Retro_chance_spreads_around_confirmed_day()
    {
        var exact = WilcoxKernel.CycleChance(Posterior.FromPointMass(14), [13]);
        var retro = WilcoxKernel.RetroChance(14, [13]);
        Assert.InRange(retro, exact * 0.5, exact); // szórt posterior kicsit kisebb esélyt ad
        Assert.True(retro > 0.15);
    }
}
