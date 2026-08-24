using Mensi.Core.Domain;

namespace Mensi.Core.Prediction;

public sealed record ObservedDay(
    int CycleDay, CervicalMucus? Mucus, LhTest? Lh, CrampType? CrampType, short? CrampSeverity);

/// <summary>Szekvenciális Bayes-frissítés: prior a naptár-statisztikából, likelihood a napi
/// jelekből (spec 4.3 táblázata). A szorzók a specifikáció részei — a tesztek ezekre épülnek.</summary>
public static class OvulationPosterior
{
    public static Posterior Compute(
        double priorMean, double priorVariance,
        IReadOnlyList<ObservedDay> observations, BbtAnalysis bbt)
    {
        var posterior = Posterior.FromNormal(priorMean, priorVariance);

        foreach (var obs in observations)
        {
            if (obs.Lh is not null)
                posterior = posterior.Reweighted(o => LhFactor(obs.Lh.Value, obs.CycleDay - o));
            if (obs.Mucus is not null)
                posterior = posterior.Reweighted(o => MucusFactor(obs.Mucus.Value, obs.CycleDay - o));
            if (obs is { CrampType: CrampType.Abdomen, CrampSeverity: >= 1, CycleDay: > 8 })
                posterior = posterior.Reweighted(o =>
                    obs.CycleDay - o is >= -1 and <= 1 ? 1.6 : 0.95);
        }

        if (bbt.ConfirmedOvulationDay is int confirmed)
            posterior = posterior.Reweighted(o =>
                Math.Abs(o - confirmed) <= 1 ? 4.0 : 0.25);

        return posterior;
    }

    private static double LhFactor(LhTest lh, int rel) => lh switch
    {
        LhTest.Positive => rel is >= -2 and <= 0 ? 6 : rel is -3 or 1 ? 2 : 0.3,
        LhTest.Peak => rel is >= -1 and <= 0 ? 12 : rel is -2 or 1 ? 2 : 0.15,
        _ => rel is >= -1 and <= 1 ? 0.6 : 1.1, // Negative
    };

    private static double MucusFactor(CervicalMucus mucus, int rel) => mucus switch
    {
        CervicalMucus.EggWhite => rel is >= -3 and <= 0 ? 3 : rel is -4 or 1 ? 1.5 : 0.5,
        CervicalMucus.Creamy => rel is >= -4 and <= -1 ? 1.8 : 0.8,
        _ => rel is >= -2 and <= 1 ? 0.55 : 1.15, // Dry, Sticky
    };
}
