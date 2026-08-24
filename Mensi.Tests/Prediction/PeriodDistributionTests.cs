using Mensi.Core.Domain;
using Mensi.Core.Prediction;

namespace Mensi.Tests.Prediction;

public class PeriodDistributionTests
{
    [Fact]
    public void Point_mass_ovulation_and_tight_luteal_give_exact_period_day()
    {
        var (p15, p50, p85) = PeriodDistribution.NextPeriod(
            Posterior.FromPointMass(14), 14, 0.0001);
        Assert.Equal(28, p15);
        Assert.Equal(28, p50);
        Assert.Equal(28, p85);
    }

    [Fact]
    public void Wider_luteal_variance_widens_the_band()
    {
        var (p15, p50, p85) = PeriodDistribution.NextPeriod(
            Posterior.FromPointMass(14), 14, 4);
        Assert.True(p15 < p50 && p50 < p85);
        Assert.InRange(p50, 27, 29);
    }

    [Fact]
    public void Luteal_is_clamped_to_9_18()
    {
        // extrém átlag mellett is a [9,18] vágott tartomány érvényesül
        var (p15, _, p85) = PeriodDistribution.NextPeriod(Posterior.FromPointMass(14), 25, 1);
        Assert.InRange(p15, 14 + 9, 14 + 18);
        Assert.InRange(p85, 14 + 9, 14 + 18);
    }

    [Theory]
    [InlineData(3, 6, ConfidenceLevel.High)]
    [InlineData(3, 2, ConfidenceLevel.Medium)]  // kevés ciklus lehúzza
    [InlineData(6, 6, ConfidenceLevel.Medium)]
    [InlineData(9, 6, ConfidenceLevel.Low)]
    public void Confidence_rule(int width, int cycles, ConfidenceLevel expected) =>
        Assert.Equal(expected, ConfidenceRule.From(width, cycles));
}
