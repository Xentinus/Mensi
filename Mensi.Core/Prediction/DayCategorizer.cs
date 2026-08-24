using Mensi.Core.Domain;

namespace Mensi.Core.Prediction;

public static class DayCategorizer
{
    /// <summary>Lezárt ciklus napjának kategóriája: vérzésnapok a logból, az ovuláció köré
    /// ±1 nap ablak, előtte 4 termékeny nap — visszatekintő nézetekhez.</summary>
    public static DayCategory CategorizeClosed(
        DateOnly date, DateOnly cycleStart, int lengthDays, int? ovulationDay,
        IReadOnlySet<DateOnly> flowDays)
    {
        var day = date.DayNumber - cycleStart.DayNumber + 1;
        if (day < 1 || day > lengthDays) return DayCategory.Unknown;
        if (flowDays.Contains(date)) return DayCategory.Menstruation;
        if (ovulationDay is not int o) return DayCategory.Follicular;
        return day switch
        {
            _ when day >= o - 1 && day <= o + 1 => DayCategory.Ovulation,
            _ when day >= o - 5 && day <= o - 2 => DayCategory.Fertile,
            _ when day > o + 1 => DayCategory.Luteal,
            _ => DayCategory.Follicular,
        };
    }
}
