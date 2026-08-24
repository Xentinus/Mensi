using Mensi.Core.Domain;

namespace Mensi.Core.Prediction;

public sealed record DerivedCycle(
    DateOnly Start, int? LengthDays, int? OvulationConfirmed, int? OvulationEstimated,
    int? LutealLength, bool Anovulatory);

public static class CycleDeriver
{
    /// <summary>Ennyi érvényes BBT-mérés alatt nem ítélünk anovulatorikusnak egy ciklust —
    /// az adat hiánya nem a bifázisos mintázat hiánya.</summary>
    public const int MinBbtForAnovulatory = 10;

    public static List<DerivedCycle> Derive(IReadOnlyList<DailyLogSnapshot> allLogs)
    {
        var ordered = allLogs.OrderBy(l => l.Date).ToList();
        var starts = ordered.Where(l => l.PeriodStart).Select(l => l.Date).ToList();
        var result = new List<DerivedCycle>();

        for (var i = 0; i < starts.Count; i++)
        {
            var start = starts[i];
            DateOnly? next = i + 1 < starts.Count ? starts[i + 1] : null;
            int? length = next is null ? null : next.Value.DayNumber - start.DayNumber;

            var cycleLogs = ordered
                .Where(l => l.Date >= start && (next is null || l.Date < next.Value))
                .ToList();
            var lastDay = cycleLogs.Count == 0
                ? 1
                : cycleLogs[^1].Date.DayNumber - start.DayNumber + 1;
            var byDay = cycleLogs.ToDictionary(l => l.Date.DayNumber - start.DayNumber + 1);
            var bbtDays = Enumerable.Range(1, Math.Max(length ?? lastDay, 1))
                .Select(d => new BbtDay(d, byDay.TryGetValue(d, out var l) ? l.Bbt : null))
                .ToList();

            var bbt = BbtAnalyzer.Analyze(bbtDays);
            int? confirmed = bbt.ConfirmedOvulationDay;
            int? luteal = length is int len && confirmed is int o ? len - o : null;
            var anovulatory = length is not null && confirmed is null
                && bbt.ValidCount >= MinBbtForAnovulatory;
            int? estimated = confirmed ?? (length is int l2 ? Math.Max(l2 - 14, 1) : null);

            result.Add(new DerivedCycle(start, length, confirmed, estimated, luteal, anovulatory));
        }
        return result;
    }
}

public static class LengthPredictor
{
    public static int? Predict(IReadOnlyList<ClosedCycleStat> closedCycles)
    {
        var stats = CycleStats.Compute(closedCycles);
        if (stats is null) return null;
        var (mean, _) = Shrinkage.Apply(
            PopulationPriors.CycleMean, PopulationPriors.CycleVar,
            stats.ClosedCount, stats.EwmaLength, stats.StdDevLength * stats.StdDevLength);
        return (int)Math.Round(mean, MidpointRounding.AwayFromZero);
    }
}
