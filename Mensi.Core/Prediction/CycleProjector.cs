using Mensi.Core.Domain;

namespace Mensi.Core.Prediction;

/// <summary>Egy előrevetített jövőbeli ciklus. Nem posterior-sáv, hanem pontbecslés: a nyitott
/// ciklus utáni ciklusokra a naptár-modell szórása annyira nagy (nálunk ±9 nap is lehet), hogy
/// a becsületesen kirajzolt sáv az egész hónapot befestené. Helyette a várható napokat mutatjuk,
/// és a nézet jelzi, hogy ezek előrevetítettek.</summary>
public sealed record ProjectedCycle(
    int Index, DateOnly Start, DateOnly MenstruationTo,
    DateOnly FertileFrom, DateOnly OvulationFrom, DateOnly OvulationTo, DateOnly End)
{
    public DayCategory Categorize(DateOnly date)
    {
        if (date < Start || date > End) return DayCategory.Unknown;
        if (date <= MenstruationTo) return DayCategory.PredictedPeriod;
        if (date >= OvulationFrom && date <= OvulationTo) return DayCategory.Ovulation;
        if (date >= FertileFrom && date < OvulationFrom) return DayCategory.Fertile;
        if (date > OvulationTo) return DayCategory.Luteal;
        return DayCategory.Follicular;
    }
}

public static class CycleProjector
{
    /// <summary>Hány ciklust vetítünk előre. 14 ciklus a leghosszabb reális ciklushossz mellett
    /// is átfedi a naptár egyéves előre-navigálását.</summary>
    public const int Cycles = 14;

    /// <summary>Az ovuláció körüli ablak fél szélessége az előrevetített ciklusokban.</summary>
    public const int OvulationHalfWidth = 1;

    /// <summary>A termékeny napok száma az ovulációs ablak előtt (spermium-túlélés).</summary>
    public const int FertileLeadDays = 5;

    /// <summary>Ennyi vérzésnappal számolunk, ha a nyitott ciklusból nem derül ki több.</summary>
    public const int DefaultMenstruationDays = 4;

    /// <param name="firstStart">Az első előrevetített ciklus kezdete — a nyitott ciklus
    /// menstruáció-becslésének mediánja.</param>
    public static IReadOnlyList<ProjectedCycle> Project(
        DateOnly firstStart, double cycleMean, double lutealMean, int menstruationDays)
    {
        var length = Math.Max((int)Math.Round(cycleMean, MidpointRounding.AwayFromZero), 15);
        var ovulationDay = Math.Clamp(
            (int)Math.Round(cycleMean - lutealMean, MidpointRounding.AwayFromZero), 2, length - 1);
        var mens = Math.Clamp(menstruationDays, 1, length - 1);

        var result = new List<ProjectedCycle>(Cycles);
        var start = firstStart;
        for (var i = 1; i <= Cycles; i++)
        {
            var ovuFrom = start.AddDays(ovulationDay - 1 - OvulationHalfWidth);
            var ovuTo = start.AddDays(ovulationDay - 1 + OvulationHalfWidth);
            result.Add(new ProjectedCycle(
                i, start, start.AddDays(mens - 1),
                ovuFrom.AddDays(-FertileLeadDays), ovuFrom, ovuTo,
                start.AddDays(length - 1)));
            start = start.AddDays(length);
        }
        return result;
    }
}
