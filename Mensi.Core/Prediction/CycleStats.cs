namespace Mensi.Core.Prediction;

public sealed record ClosedCycleStat(
    DateOnly StartDate, int LengthDays, int? LutealLength, bool Anovulatory, int? PredictedLengthDays);

public sealed record CycleStatsResult(
    int ClosedCount, double EwmaLength, double MeanLength, double StdDevLength,
    double MedianLength, int MinLength, int MaxLength,
    double? MeanLuteal, double? StdDevLuteal, int ConfirmedLutealCount,
    (int P10, int P50, int P90)? Delay);

public static class CycleStats
{
    public const double Alpha = 0.27;

    public static CycleStatsResult? Compute(IReadOnlyList<ClosedCycleStat> cycles)
    {
        if (cycles.Count == 0) return null;
        var ordered = cycles.OrderBy(c => c.StartDate).ToList();
        var lengths = ordered.Select(c => (double)c.LengthDays).ToList();

        // EWMA: az anovulatorikus ciklus fél súllyal frissít (kilógó, de nem eldobható adat).
        var ewma = lengths[0];
        for (var i = 1; i < ordered.Count; i++)
        {
            var a = Alpha * (ordered[i].Anovulatory ? 0.5 : 1.0);
            ewma = a * lengths[i] + (1 - a) * ewma;
        }

        var mean = lengths.Average();
        var std = lengths.Count < 2
            ? 0
            : Math.Sqrt(lengths.Sum(l => (l - mean) * (l - mean)) / (lengths.Count - 1));

        var sortedLen = lengths.OrderBy(l => l).ToList();
        var median = sortedLen.Count % 2 == 1
            ? sortedLen[sortedLen.Count / 2]
            : (sortedLen[sortedLen.Count / 2 - 1] + sortedLen[sortedLen.Count / 2]) / 2;

        var luteals = ordered.Where(c => c.LutealLength is not null)
            .Select(c => (double)c.LutealLength!.Value).ToList();
        double? meanLut = luteals.Count > 0 ? luteals.Average() : null;
        double? stdLut = luteals.Count switch
        {
            0 => null,
            1 => 0,
            _ => Math.Sqrt(luteals.Sum(l => (l - meanLut!.Value) * (l - meanLut.Value)) / (luteals.Count - 1)),
        };

        var delays = ordered.Where(c => c.PredictedLengthDays is not null)
            .Select(c => c.LengthDays - c.PredictedLengthDays!.Value)
            .OrderBy(d => d).ToList();
        (int, int, int)? delay = delays.Count > 0
            ? (NearestRank(delays, 0.10), NearestRank(delays, 0.50), NearestRank(delays, 0.90))
            : null;

        return new CycleStatsResult(
            ordered.Count, ewma, mean, std, median,
            (int)sortedLen[0], (int)sortedLen[^1],
            meanLut, stdLut, luteals.Count, delay);
    }

    private static int NearestRank(IReadOnlyList<int> sorted, double p)
    {
        var rank = (int)Math.Ceiling(p * sorted.Count);
        return sorted[Math.Clamp(rank, 1, sorted.Count) - 1];
    }
}
