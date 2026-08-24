using Mensi.Core.Domain;
using Mensi.Core.Prediction;

namespace Mensi.Tests.Prediction;

public class OvulationPosteriorTests
{
    private static readonly BbtAnalysis NoBbt =
        new(null, null, new HashSet<int>(), new HashSet<int>(), new HashSet<int>(), 0);

    [Fact]
    public void Prior_without_observations_centers_on_prior_mean()
    {
        var p = OvulationPosterior.Compute(14, 9, [], NoBbt);
        Assert.InRange(p.Quantile(0.5), 13, 15);
        Assert.Equal(1.0, p.Sum, 9);
    }

    [Fact]
    public void Lh_peak_narrows_and_pulls_the_posterior()
    {
        var prior = Posterior.FromNormal(16, 9);
        var priorWidth = prior.Quantile(0.85) - prior.Quantile(0.15);

        var p = OvulationPosterior.Compute(16, 9,
            [new ObservedDay(13, null, LhTest.Peak, null, null)], NoBbt);
        var width = p.Quantile(0.85) - p.Quantile(0.15);

        Assert.InRange(p.Quantile(0.5), 12, 15); // a csúcs d−o ∈ [−1,0]-t preferál → o ∈ [13,14]
        Assert.True(width < priorWidth);
    }

    [Fact]
    public void Confirmed_bbt_shift_dominates()
    {
        var bbt = new BbtAnalysis(36.42m, 12, new HashSet<int>(), new HashSet<int>(),
            new HashSet<int> { 13, 14, 15 }, 12);
        var p = OvulationPosterior.Compute(17, 9, [], bbt);
        Assert.InRange(p.Quantile(0.5), 11, 13);
    }

    [Fact]
    public void Egg_white_mucus_shifts_mass_before_the_day()
    {
        var p = OvulationPosterior.Compute(14, 9,
            [new ObservedDay(12, CervicalMucus.EggWhite, null, null, null)], NoBbt);
        // nyúlós nyák d−o ∈ [−3..0] → o ∈ [12..15] súlyozott
        Assert.InRange(p.Quantile(0.5), 12, 15);
    }

    [Fact]
    public void Point_mass_factory_is_degenerate()
    {
        var p = Posterior.FromPointMass(14);
        Assert.Equal(14, p.Quantile(0.15));
        Assert.Equal(14, p.Quantile(0.85));
        Assert.Equal(1.0, p[14], 9);
    }

    [Fact]
    public void All_zero_reweight_falls_back_to_unweighted()
    {
        var p = Posterior.FromPointMass(14).Reweighted(_ => 0);
        Assert.Equal(14, p.Quantile(0.5));
    }
}
