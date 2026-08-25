using Mensi.Core.Domain;

namespace Mensi.Core.Prediction;

public sealed record ObservedDay(
    int CycleDay, CervicalMucus? Mucus, LhTest? Lh, CrampType? CrampType, short? CrampSeverity,
    decimal? LhValue = null);

/// <summary>Az LH tesztcsík/kontrollcsík arányának kezelése. A háromértékű <see cref="LhTest"/>
/// enum ugyanezen a 0–1 skálán él: az importált Low/High/Peak eredmények kanonikus arányt kapnak,
/// így a modellnek egyetlen — folytonos — beviteli útja van.</summary>
public static class LhScale
{
    public const decimal NegativeValue = 0.15m;
    public const decimal PositiveValue = 0.80m;
    public const decimal PeakValue = 1.00m;

    /// <summary>Az enum kanonikus aránya — az importált és a régi, csík nélküli bejegyzésekhez.</summary>
    public static decimal ToValue(LhTest lh) => lh switch
    {
        LhTest.Peak => PeakValue,
        LhTest.Positive => PositiveValue,
        _ => NegativeValue,
    };

    /// <summary>A megjelenítéshez visszavezetett háromértékű besorolás.</summary>
    public static LhTest ToTest(decimal value) => value switch
    {
        >= 0.95m => LhTest.Peak,
        >= 0.65m => LhTest.Positive,
        _ => LhTest.Negative,
    };

    /// <summary>A tárolt arány, ha van; különben az enum kanonikus értéke.</summary>
    public static decimal? Resolve(decimal? value, LhTest? test) =>
        value ?? (test is LhTest t ? ToValue(t) : null);
}

/// <summary>Szekvenciális Bayes-frissítés: prior a naptár-statisztikából, likelihood a napi
/// jelekből (spec 4.3 táblázata). A szorzók a specifikáció részei — a tesztek ezekre épülnek.</summary>
public static class OvulationPosterior
{
    public static Posterior Compute(
        double priorMean, double priorVariance,
        IReadOnlyList<ObservedDay> observations, BbtAnalysis bbt)
    {
        var posterior = Posterior.FromNormal(priorMean, priorVariance);
        var amplitude = LhLikelihood.Amplitude(observations);

        foreach (var obs in observations)
        {
            if (LhScale.Resolve(obs.LhValue, obs.Lh) is decimal lh)
                posterior = posterior.Reweighted(o =>
                    LhLikelihood.Factor((double)lh, obs.CycleDay - o, amplitude));
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

    private static double MucusFactor(CervicalMucus mucus, int rel) => mucus switch
    {
        CervicalMucus.EggWhite => rel is >= -3 and <= 0 ? 3 : rel is -4 or 1 ? 1.5 : 0.5,
        CervicalMucus.Creamy => rel is >= -4 and <= -1 ? 1.8 : 0.8,
        _ => rel is >= -2 and <= 1 ? 0.55 : 1.15, // Dry, Sticky
    };
}

/// <summary>Az LH-arány folytonos likelihoodja. A háromértékű besorolás azért nem szűkített
/// semmit, mert a 0,10 és a 0,40 csík egyaránt „negatív" volt, holott a kettő között ott a
/// teljes emelkedő ág. Itt minden nap arra kap súlyt, mennyire illik a mért arány ahhoz a
/// tipikus LH-görbéhez, amit az adott ovulációs nap feltételezése megkövetelne.</summary>
public static class LhLikelihood
{
    /// <summary>A tipikus LH-görbe alakja az ovulációs naphoz képest (0 = alapszint, 1 = csúcs).
    /// A csúcs a −1. napon van: a lökés után 24–36 órával következik az ovuláció.</summary>
    private static double Shape(int rel) => rel switch
    {
        -4 => 0.08, -3 => 0.17, -2 => 0.40, -1 => 1.00,
        0 => 0.70, 1 => 0.22, 2 => 0.08,
        _ => 0.0,
    };

    /// <summary>Az alapszint (nem termékeny napok tipikus aránya).</summary>
    public const double Baseline = 0.10;

    /// <summary>A csúcs-amplitúdó alsó és felső korlátja. Az abszolút csíkerősség
    /// tesztmárkánként és hígításonként más, ezért a ciklus saját maximumához mérünk —
    /// akinek a csíkja sosem megy 0,45 fölé, annál a 0,45 a csúcs.</summary>
    public const double MinAmplitude = 0.35, MaxAmplitude = 1.20;

    /// <summary>A gyenge illeszkedés alsó korlátja: egyetlen félreolvasott csík ne olthasson ki
    /// egy egyébként lehetséges ovulációs napot.</summary>
    private const double FloorFactor = 0.04;

    /// <summary>A ciklusban látott legnagyobb arány — ehhez skálázódik a görbe csúcsa.</summary>
    public static double Amplitude(IReadOnlyList<ObservedDay> observations)
    {
        double max = 0;
        foreach (var obs in observations)
            if (LhScale.Resolve(obs.LhValue, obs.Lh) is decimal v)
                max = Math.Max(max, (double)v);
        return Math.Clamp(max, MinAmplitude, MaxAmplitude);
    }

    /// <param name="rel">A mérés napja mínusz a feltételezett ovulációs nap.</param>
    public static double Factor(double value, int rel, double amplitude)
    {
        var shape = Shape(rel);
        var expected = Baseline + shape * (amplitude - Baseline);
        // A csúcs közelében nagyobb a tűrés: a lökés hossza és a mérés órája is ingadozik.
        var sigma = 0.13 + 0.18 * shape;
        var z = (value - expected) / sigma;
        return Math.Max(Math.Exp(-z * z / 2), FloorFactor);
    }
}
