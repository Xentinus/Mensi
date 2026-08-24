using Mensi.Core.Domain;

namespace Mensi.Core.Prediction;

public sealed record DailyLogSnapshot(
    DateOnly Date, decimal? Bbt, CervicalMucus? Mucus, LhTest? Lh,
    CrampType? CrampType, short? CrampSeverity, FlowIntensity? Flow,
    bool PeriodStart, int IntercourseCount, int UnprotectedCount);

public sealed record EngineInput(
    IReadOnlyList<ClosedCycleStat> ClosedCycles,
    DateOnly CurrentCycleStart,
    IReadOnlyList<DailyLogSnapshot> CurrentCycleLogs,
    DateOnly Today);

public sealed record PhaseInfo(DayCategory Key, string Label, int TotalDays, int ElapsedDays, int RemainingDays);

public sealed record CyclePrediction(
    DateOnly CycleStart, int CycleDay,
    DateOnly OvulationFrom, DateOnly OvulationP50, DateOnly OvulationTo,
    DateOnly PeriodFrom, DateOnly PeriodP50, DateOnly PeriodTo,
    DateOnly FertileFrom, DateOnly FertileTo,
    ConfidenceLevel Confidence, double Chance, TimingLabel Timing,
    string? WhatIfHint, string? PregnancyHint, string Headline,
    PhaseInfo Phase, BbtAnalysis Bbt, Posterior OvulationPosterior, int MenstruationEndDay)
{
    public DayCategory Categorize(DateOnly date)
    {
        var day = date.DayNumber - CycleStart.DayNumber + 1;
        if (day < 1) return DayCategory.Unknown;
        if (day <= MenstruationEndDay) return DayCategory.Menstruation;
        if (date >= OvulationFrom && date <= OvulationTo) return DayCategory.Ovulation;
        if (date >= FertileFrom && date < OvulationFrom) return DayCategory.Fertile;
        if (date >= PeriodFrom && date <= PeriodTo) return DayCategory.PredictedPeriod;
        if (date > OvulationTo && date < PeriodFrom) return DayCategory.Luteal;
        if (date > PeriodTo) return DayCategory.Unknown;
        return DayCategory.Follicular;
    }
}

public static class PredictionEngine
{
    public static CyclePrediction? Evaluate(EngineInput input)
    {
        var stats = CycleStats.Compute(input.ClosedCycles);
        if (stats is null) return null;

        var start = input.CurrentCycleStart;
        var today = input.Today;
        var cycleDay = today.DayNumber - start.DayNumber + 1;

        // Személyes paraméterek shrinkage-dzsel (spec 4.2–4.3).
        var (cycleMean, cycleVar) = Shrinkage.Apply(
            PopulationPriors.CycleMean, PopulationPriors.CycleVar,
            stats.ClosedCount, stats.EwmaLength, stats.StdDevLength * stats.StdDevLength);
        var (lutealMean, lutealVar) = Shrinkage.Apply(
            PopulationPriors.LutealMean, PopulationPriors.LutealVar,
            stats.ConfirmedLutealCount, stats.MeanLuteal ?? PopulationPriors.LutealMean,
            (stats.StdDevLuteal ?? 0) * (stats.StdDevLuteal ?? 0));

        var bbt = BbtAnalyzer.Analyze(BuildBbtDays(input.CurrentCycleLogs, start, cycleDay));
        var observations = input.CurrentCycleLogs
            .Select(l => new ObservedDay(
                l.Date.DayNumber - start.DayNumber + 1, l.Mucus, l.Lh, l.CrampType, l.CrampSeverity))
            .ToList();

        var posterior = OvulationPosterior.Compute(
            cycleMean - lutealMean, cycleVar + lutealVar, observations, bbt);

        // Feltétel NÉLKÜLI menstruáció-eloszlás: a terhesség-jelzés „késik-e" kérdéséhez
        // az kell, hova esett volna a menstruáció a naptári modell szerint.
        var (_, _, unconditionedPerToDay) =
            PeriodDistribution.NextPeriod(posterior, lutealMean, lutealVar);

        // „Még nincs menstruáció" evidencia: a mai napig nem kezdődött el, tehát
        // o + luteális ≥ mai ciklusnap — a korai ovulációs napok tömege ennyivel csökken.
        posterior = posterior.Reweighted(o =>
            PeriodDistribution.LutealSurvival(cycleDay - o, lutealMean, lutealVar));

        var ovuFromDay = posterior.Quantile(0.15);
        var ovuP50Day = posterior.Quantile(0.50);
        var ovuToDay = posterior.Quantile(0.85);
        var (perFromDay, perP50Day, perToDay) =
            PeriodDistribution.NextPeriod(posterior, lutealMean, lutealVar, minPeriodDay: cycleDay);

        var fertileFromDay = Math.Min(ovuP50Day - 5, ovuFromDay);

        DateOnly D(int day) => start.AddDays(day - 1);

        var unprotectedDays = input.CurrentCycleLogs
            .Where(l => l.UnprotectedCount > 0)
            .Select(l => l.Date.DayNumber - start.DayNumber + 1).ToList();
        var chance = WilcoxKernel.CycleChance(posterior, unprotectedDays);
        var whatIf = WilcoxKernel.WhatIfHint(posterior, unprotectedDays, cycleDay, ovuToDay);

        var mensEnd = MenstruationEnd(input.CurrentCycleLogs, start);
        var confidence = ConfidenceRule.From(ovuToDay - ovuFromDay, stats.ClosedCount);
        var pregnancy = PregnancyHint(input, bbt, D(unconditionedPerToDay), lutealMean, start, today);

        var prediction = new CyclePrediction(
            start, cycleDay,
            D(ovuFromDay), D(ovuP50Day), D(ovuToDay),
            D(perFromDay), D(perP50Day), D(perToDay),
            D(fertileFromDay), D(ovuToDay),
            confidence, chance, WilcoxKernel.Label(chance),
            whatIf, pregnancy, "", PhaseOf(DayCategory.Unknown, 1, 1, 0), bbt, posterior, mensEnd);

        var phase = BuildPhase(prediction, today);
        return prediction with { Phase = phase, Headline = Headline(prediction, phase, today) };
    }

    private static IReadOnlyList<BbtDay> BuildBbtDays(
        IReadOnlyList<DailyLogSnapshot> logs, DateOnly start, int cycleDay)
    {
        var byDay = logs.ToDictionary(l => l.Date.DayNumber - start.DayNumber + 1);
        return Enumerable.Range(1, Math.Max(cycleDay, 1))
            .Select(d => new BbtDay(d, byDay.TryGetValue(d, out var l) ? l.Bbt : null))
            .ToList();
    }

    private static int MenstruationEnd(IReadOnlyList<DailyLogSnapshot> logs, DateOnly start)
    {
        var byDay = logs.ToDictionary(l => l.Date.DayNumber - start.DayNumber + 1);
        var end = 1; // az 1. nap definíció szerint menstruáció (period_start jelölte ki)
        for (var d = 1; byDay.TryGetValue(d, out var l) && l.Flow >= FlowIntensity.Light; d++)
            end = d;
        return end;
    }

    private static PhaseInfo BuildPhase(CyclePrediction p, DateOnly today)
    {
        var category = p.Categorize(today);
        var (from, to) = category switch
        {
            DayCategory.Menstruation => (p.CycleStart, p.CycleStart.AddDays(p.MenstruationEndDay - 1)),
            DayCategory.Follicular => (p.CycleStart.AddDays(p.MenstruationEndDay), p.FertileFrom.AddDays(-1)),
            DayCategory.Fertile => (p.FertileFrom, p.OvulationFrom.AddDays(-1)),
            DayCategory.Ovulation => (p.OvulationFrom, p.OvulationTo),
            DayCategory.Luteal => (p.OvulationTo.AddDays(1), p.PeriodFrom.AddDays(-1)),
            DayCategory.PredictedPeriod => (p.PeriodFrom, p.PeriodTo),
            _ => (today, today),
        };
        var total = Math.Max(to.DayNumber - from.DayNumber + 1, 1);
        var elapsed = Math.Clamp(today.DayNumber - from.DayNumber + 1, 0, total);
        return PhaseOf(category, total, elapsed, total - elapsed);
    }

    private static PhaseInfo PhaseOf(DayCategory key, int total, int elapsed, int remaining) =>
        new(key, key switch
        {
            DayCategory.Menstruation => "Menstruáció",
            DayCategory.Follicular => "Follikuláris szakasz",
            DayCategory.Fertile => "Termékeny ablak",
            DayCategory.Ovulation => "Ovulációs ablak",
            DayCategory.Luteal => "Luteális fázis",
            DayCategory.PredictedPeriod => "Becsült menstruáció",
            _ => "Cikluson túl",
        }, total, elapsed, remaining);

    private static string Headline(CyclePrediction p, PhaseInfo phase, DateOnly today) =>
        phase.Key switch
        {
            DayCategory.Menstruation => $"Menstruáció — a ciklus {p.CycleDay}. napja.",
            DayCategory.Follicular =>
                $"Follikuláris szakasz — a termékeny ablak {p.FertileFrom.DayNumber - today.DayNumber} nap múlva kezdődik.",
            DayCategory.Fertile =>
                $"Termékeny ablakban vagy — az ovuláció {p.OvulationTo.DayNumber - today.DayNumber} napon belül várható.",
            DayCategory.Ovulation => "Ovulációs ablakban vagy — most a legnagyobb az esély.",
            DayCategory.Luteal =>
                $"Luteális fázis — a következő menstruáció {p.PeriodTo.DayNumber - today.DayNumber} napon belül várható.",
            DayCategory.PredictedPeriod => "A menstruáció ezekben a napokban várható.",
            _ => "A ciklus a becsült hossznál hosszabb — ha nincs vérzés, érdemes tesztet fontolóra venni.",
        };

    private static string? PregnancyHint(
        EngineInput input, BbtAnalysis bbt, DateOnly periodTo,
        double lutealMean, DateOnly start, DateOnly today)
    {
        var noFlowSincePredicted = !input.CurrentCycleLogs.Any(l =>
            l.Date >= periodTo.AddDays(-(int)lutealMean) && l.Flow >= FlowIntensity.Light
            && l.Date.DayNumber - start.DayNumber + 1 > 6);

        // 1. szabály: a predikció felső határa elmúlt, a BBT az utolsó 3 mérésben coverline fölött.
        if (today > periodTo && bbt.Coverline is not null && noFlowSincePredicted)
        {
            var lastThree = input.CurrentCycleLogs
                .Where(l => l.Bbt is not null).OrderBy(l => l.Date).TakeLast(3).ToList();
            if (lastThree.Count == 3 && lastThree.All(l => l.Bbt > bbt.Coverline))
                return "A menstruáció a becsült ablakhoz képest késik, és a testhő emelkedett maradt "
                     + "— érdemes hCG-tesztet végezni.";
        }

        // 2. szabály: megerősített ovuláció után a luteális + 3 napnál tovább magas a BBT.
        if (bbt.ConfirmedOvulationDay is int o)
        {
            var daysSinceOvu = today.DayNumber - start.AddDays(o - 1).DayNumber;
            if (daysSinceOvu > lutealMean + 3 && noFlowSincePredicted
                && bbt.AboveCoverlineDays.Count >= 3)
                return "A luteális fázis a szokásosnál hosszabb, a testhő emelkedett és nincs vérzés "
                     + "— érdemes hCG-tesztet végezni.";
        }
        return null;
    }
}
