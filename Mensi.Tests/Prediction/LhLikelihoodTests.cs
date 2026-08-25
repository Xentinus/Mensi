using Mensi.Core.Domain;
using Mensi.Core.Prediction;

namespace Mensi.Tests.Prediction;

public class LhLikelihoodTests
{
    private static readonly BbtAnalysis NoBbt =
        new(null, null, new HashSet<int>(), new HashSet<int>(), new HashSet<int>(), 0);

    private static ObservedDay Lh(int day, decimal value) =>
        new(day, null, null, null, null, value);

    [Theory]
    [InlineData(0.10, LhTest.Negative)]
    [InlineData(0.60, LhTest.Negative)]
    [InlineData(0.80, LhTest.Positive)]
    [InlineData(1.00, LhTest.Peak)]
    public void Value_maps_back_to_the_three_way_classification(double value, LhTest expected) =>
        Assert.Equal(expected, LhScale.ToTest((decimal)value));

    [Fact]
    public void Enum_only_days_fall_back_to_the_canonical_ratio()
    {
        Assert.Equal(LhScale.PeakValue, LhScale.Resolve(null, LhTest.Peak));
        Assert.Equal(0.42m, LhScale.Resolve(0.42m, LhTest.Negative)); // az arány a vezető adat
        Assert.Null(LhScale.Resolve(null, null));
    }

    [Fact]
    public void A_flat_series_and_a_rising_series_are_no_longer_the_same_evidence()
    {
        // Mindkét sorozat minden napja „negatív” a háromértékű skálán — korábban ezért
        // egyikük sem mozdította a becslést. A második sorozatban viszont ott a lökés alakja.
        var flat = new[] { Lh(12, 0.10m), Lh(14, 0.10m), Lh(16, 0.10m), Lh(18, 0.10m), Lh(20, 0.10m) };
        var rising = new[] { Lh(12, 0.10m), Lh(14, 0.20m), Lh(16, 0.45m), Lh(18, 0.15m), Lh(20, 0.10m) };

        var flatPost = OvulationPosterior.Compute(16, 25, flat, NoBbt);
        var risingPost = OvulationPosterior.Compute(16, 25, rising, NoBbt);

        var flatWidth = flatPost.Quantile(0.85) - flatPost.Quantile(0.15);
        var risingWidth = risingPost.Quantile(0.85) - risingPost.Quantile(0.15);

        Assert.True(risingWidth < flatWidth,
            $"a felfutó sorozat nem szűkített: {risingWidth} vs {flatWidth}");
        // A 16. napi maximum a lökés napja → az ovuláció a rá következő nap környékén.
        Assert.InRange(risingPost.Quantile(0.5), 16, 18);
    }

    [Fact]
    public void The_curve_is_measured_against_the_cycles_own_maximum()
    {
        // Akinek a csíkja sosem megy 0,45 fölé, annál a 0,45 a csúcs — a fix 0,8-as
        // „pozitív” küszöb az ilyen tesztmárkánál soha nem teljesülne.
        var observations = new[] { Lh(10, 0.12m), Lh(13, 0.45m), Lh(16, 0.12m) };
        var amplitude = LhLikelihood.Amplitude(observations);
        Assert.Equal(0.45, amplitude, 3);

        var post = OvulationPosterior.Compute(15, 25, observations, NoBbt);
        Assert.InRange(post.Quantile(0.5), 13, 15);
    }

    [Fact]
    public void Amplitude_is_clamped_so_a_single_faint_strip_is_not_a_peak()
    {
        Assert.Equal(LhLikelihood.MinAmplitude, LhLikelihood.Amplitude([Lh(10, 0.05m)]), 3);
        Assert.Equal(LhLikelihood.MinAmplitude, LhLikelihood.Amplitude([]), 3);
        Assert.Equal(1.0, LhLikelihood.Amplitude([Lh(10, 1.00m)]), 3);
        Assert.Equal(LhLikelihood.MaxAmplitude, LhLikelihood.Amplitude([Lh(10, 1.50m)]), 3);
    }

    [Fact]
    public void A_single_misread_strip_cannot_zero_out_a_day()
    {
        // Az alsó korlát nélkül egy elgépelt érték kioltana egy egyébként lehetséges napot.
        var factor = LhLikelihood.Factor(1.0, rel: -12, amplitude: 1.0);
        Assert.True(factor > 0, "a likelihood nem eshet nullára");
        Assert.True(factor < 0.2, "a nyilvánvalóan rossz illeszkedés így is erősen bünteti a napot");
    }

    [Fact]
    public void The_peak_is_the_day_before_ovulation()
    {
        // A lökés után 24–36 órával jön az ovuláció: a teljes erősségű csík a −1. naphoz
        // illik a legjobban, minden más eltolás rosszabb.
        const double amp = 1.0;
        var best = LhLikelihood.Factor(amp, rel: -1, amplitude: amp);
        for (var rel = -6; rel <= 3; rel++)
        {
            if (rel == -1) continue;
            Assert.True(LhLikelihood.Factor(amp, rel, amp) < best,
                $"a {rel}. eltolás nem lehet jobb illeszkedés, mint a −1.");
        }
        // Az alapszint felőli oldalon fordítva: a halvány csík az ovulációtól távol illik.
        Assert.True(LhLikelihood.Factor(LhLikelihood.Baseline, rel: -6, amplitude: amp)
            > LhLikelihood.Factor(LhLikelihood.Baseline, rel: -1, amplitude: amp));
    }
}
