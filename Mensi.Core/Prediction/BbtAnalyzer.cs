namespace Mensi.Core.Prediction;

public sealed record BbtDay(int CycleDay, decimal? Value);

public sealed record BbtAnalysis(
    decimal? Coverline,
    int? ConfirmedOvulationDay,
    IReadOnlySet<int> OutlierDays,
    IReadOnlySet<int> MissingDays,
    IReadOnlySet<int> AboveCoverlineDays,
    int ValidCount);

/// <summary>Coverline + change-point a spec 4.4 szerint. Hiányzó napot nem interpolál:
/// az "egymást követő" mindig az érvényes mérések sorrendjét jelenti.</summary>
public static class BbtAnalyzer
{
    public const decimal ShiftThreshold = 0.2m;
    public const decimal OutlierDelta = 0.3m;
    private const decimal TrendDelta = 0.15m;

    public static BbtAnalysis Analyze(IReadOnlyList<BbtDay> days)
    {
        var missing = days.Where(d => d.Value is null).Select(d => d.CycleDay).ToHashSet();
        var valid = days.Where(d => d.Value is not null)
            .OrderBy(d => d.CycleDay)
            .Select(d => (Day: d.CycleDay, Value: d.Value!.Value))
            .ToList();

        var outliers = FindOutliers(valid);
        var series = valid.Where(v => !outliers.Contains(v.Day)).ToList();

        decimal? coverline = null;
        int? confirmedDay = null;

        // Change-point: az első k pozíció, ahol az előtte lévő 6 érték maximuma fölött
        // legalább 0,2°C-kal van 3 egymást követő érvényes mérés.
        for (var k = 6; k + 2 < series.Count; k++)
        {
            var baseline = series.Skip(k - 6).Take(6).Max(v => v.Value);
            if (series[k].Value > baseline + ShiftThreshold
                && series[k + 1].Value > baseline + ShiftThreshold
                && series[k + 2].Value > baseline + ShiftThreshold)
            {
                coverline = baseline;
                confirmedDay = series[k - 1].Day;
                break;
            }
        }

        // Provizórikus coverline megerősítés előtt: az utolsó 6 érvényes, nem-kiugró érték maximuma.
        coverline ??= series.Count >= 6 ? series.TakeLast(6).Max(v => v.Value) : null;

        var above = coverline is null
            ? new HashSet<int>()
            : series.Where(v => v.Value > coverline.Value).Select(v => v.Day).ToHashSet();

        return new BbtAnalysis(coverline, confirmedDay, outliers, missing, above, valid.Count);
    }

    private static HashSet<int> FindOutliers(List<(int Day, decimal Value)> valid)
    {
        var outliers = new HashSet<int>();
        for (var i = 0; i < valid.Count; i++)
        {
            var (day, value) = valid[i];
            var neighbors = valid.Where(v => v.Day != day && Math.Abs(v.Day - day) <= 3)
                .OrderBy(v => Math.Abs(v.Day - day)).Take(5)
                .Select(v => v.Value).OrderBy(v => v).ToList();
            if (neighbors.Count < 2) continue;

            var median = neighbors[neighbors.Count / 2];
            var deviation = value - median;
            if (Math.Abs(deviation) < OutlierDelta) continue;

            // Trend része, ha a közvetlen előző vagy következő érvényes mérés is
            // ugyanabba az irányba tér el legalább 0,15°C-kal.
            var sameDirection = false;
            foreach (var j in new[] { i - 1, i + 1 })
            {
                if (j < 0 || j >= valid.Count) continue;
                var other = valid[j].Value - median;
                if (Math.Sign(other) == Math.Sign(deviation) && Math.Abs(other) >= TrendDelta)
                    sameDirection = true;
            }
            if (!sameDirection) outliers.Add(day);
        }
        return outliers;
    }
}
