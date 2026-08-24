using Mensi.Core.Prediction;

namespace Mensi.Tests.Prediction;

public class ShrinkageTests
{
    [Fact]
    public void Zero_samples_returns_population_prior()
    {
        var (mean, var) = Shrinkage.Apply(28, 16, 0, 0, 0);
        Assert.Equal(28, mean);
        Assert.Equal(16, var);
    }

    [Fact]
    public void Many_samples_converge_to_sample_mean()
    {
        var (mean, _) = Shrinkage.Apply(28, 16, 100, 26, 1);
        Assert.True(Math.Abs(mean - 26) < 0.1);
    }

    [Fact]
    public void Few_samples_land_between_population_and_sample()
    {
        var (mean, _) = Shrinkage.Apply(28, 16, 3, 26, 4);
        Assert.InRange(mean, 26, 28);
    }

    [Fact]
    public void Small_n_uses_population_variance_as_within()
    {
        // n<2-nél a mintavariancia nem értelmezhető: a populációsat használjuk s²-ként.
        var one = Shrinkage.Apply(28, 16, 1, 26, 0);
        var oneExplicit = Shrinkage.Apply(28, 16, 1, 26, 16);
        Assert.Equal(oneExplicit, one);
    }
}
