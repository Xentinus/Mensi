using Mensi.Core.Domain;

namespace Mensi.Core.Prediction;

/// <summary>Wilcox et al. 1995 (NEJM) napi fogamzási valószínűségei a 6 napos termékeny
/// ablakra, az ovuláció-posterior fölött várható értékkel (spec 4.5).</summary>
public static class WilcoxKernel
{
    public static readonly double[] DailyP = [0.10, 0.16, 0.14, 0.27, 0.31, 0.33]; // d−o = −5…0

    public static double DayProbability(int rel) =>
        rel is >= -5 and <= 0 ? DailyP[rel + 5] : 0;

    public static double CycleChance(Posterior ovulation, IReadOnlyCollection<int> unprotectedDays)
    {
        if (unprotectedDays.Count == 0) return 0;
        double expected = 0;
        for (var o = Posterior.GridMin; o <= Posterior.GridMax; o++)
        {
            var po = ovulation[o];
            if (po <= 0) continue;
            var miss = unprotectedDays.Aggregate(1.0, (acc, d) => acc * (1 - DayProbability(d - o)));
            expected += po * (1 - miss);
        }
        return expected;
    }

    public static TimingLabel Label(double chance) =>
        chance < 0.08 ? TimingLabel.Weak : chance <= 0.16 ? TimingLabel.Medium : TimingLabel.Good;

    public static string LabelHu(TimingLabel label) => label switch
    {
        TimingLabel.Weak => "Gyenge",
        TimingLabel.Medium => "Közepes",
        _ => "Jó",
    };

    /// <summary>Lezárt ciklus visszamenőleges minősítése a megerősített/becsült nap köré
    /// húzott Normal(o, 1.5²) posteriorral.</summary>
    public static double RetroChance(int ovulationDay, IReadOnlyCollection<int> unprotectedDays) =>
        CycleChance(Posterior.FromNormal(ovulationDay, 2.25), unprotectedDays);

    public static string? WhatIfHint(Posterior ovulation, IReadOnlyCollection<int> unprotectedDays,
        int todayCycleDay, int fertileEndDay)
    {
        if (todayCycleDay > fertileEndDay) return null;
        var current = Label(CycleChance(ovulation, unprotectedDays));

        var withToday = Label(CycleChance(ovulation, [.. unprotectedDays, todayCycleDay]));
        var tomorrow = todayCycleDay + 1;
        var withTomorrow = tomorrow <= fertileEndDay
            ? Label(CycleChance(ovulation, [.. unprotectedDays, tomorrow]))
            : current;

        if (withToday > current && withTomorrow > current && withToday == withTomorrow)
            return $"Ha ma vagy holnap van együttlét, a minősítés {LabelHu(withToday)} lesz.";
        if (withToday > current)
            return $"Ha ma van együttlét, a minősítés {LabelHu(withToday)} lesz.";
        if (withTomorrow > current)
            return $"Ha holnap van együttlét, a minősítés {LabelHu(withTomorrow)} lesz.";
        return null;
    }
}
