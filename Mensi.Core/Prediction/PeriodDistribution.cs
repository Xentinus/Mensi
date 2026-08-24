using Mensi.Core.Domain;

namespace Mensi.Core.Prediction;

public static class PeriodDistribution
{
    public const int LutealMin = 9, LutealMax = 18;

    /// <param name="minPeriodDay">
    /// A „még nincs menstruáció" evidencia: a mai ciklusnapnál korábbi kezdet lehetetlen,
    /// ezért az eloszlás erre a napra csonkolódik. 0 = nincs feltétel.
    /// </param>
    public static (int P15, int P50, int P85) NextPeriod(
        Posterior ovulation, double lutealMean, double lutealVariance, int minPeriodDay = 0)
    {
        var luteal = LutealWeights(lutealMean, lutealVariance);
        var lutealSum = luteal.Sum();

        var minDay = Posterior.GridMin + LutealMin;
        var maxDay = Posterior.GridMax + LutealMax;
        var period = new double[maxDay - minDay + 1];
        for (var o = Posterior.GridMin; o <= Posterior.GridMax; o++)
        {
            var po = ovulation[o];
            if (po <= 0) continue;
            for (var i = 0; i < luteal.Length; i++)
                period[o + LutealMin + i - minDay] += po * luteal[i] / lutealSum;
        }

        for (var day = minDay; day < Math.Min(minPeriodDay, maxDay + 1); day++)
            period[day - minDay] = 0;

        // Ha minden tömeg a csonkolás alá esett (a ciklus messze túlnyúlt a modellen),
        // a legkorábbi még lehetséges nap marad: "mostantól bármikor".
        if (period.Sum() <= 0)
        {
            var fallback = Math.Min(Math.Max(minPeriodDay, minDay), maxDay);
            return (fallback, fallback, fallback);
        }

        return (Quantile(0.15), Quantile(0.50), Quantile(0.85));

        int Quantile(double q)
        {
            var total = period.Sum();
            double cum = 0;
            for (var i = 0; i < period.Length; i++)
            {
                cum += period[i];
                if (cum >= q * total) return minDay + i;
            }
            return maxDay;
        }
    }

    /// <summary>P(luteális ≥ minLuteal) a [9,18]-ra vágott, diszkretizált eloszlás szerint —
    /// az ovuláció-posterior „még nincs menstruáció" súlyozásához.</summary>
    public static double LutealSurvival(int minLuteal, double lutealMean, double lutealVariance)
    {
        if (minLuteal <= LutealMin) return 1;
        if (minLuteal > LutealMax) return 0;
        var luteal = LutealWeights(lutealMean, lutealVariance);
        var total = luteal.Sum();
        double tail = 0;
        for (var l = minLuteal; l <= LutealMax; l++) tail += luteal[l - LutealMin];
        return tail / total;
    }

    private static double[] LutealWeights(double lutealMean, double lutealVariance)
    {
        // Diszkretizált, [9,18]-ra vágott luteális eloszlás.
        var v = Math.Max(lutealVariance, 0.0001);
        var luteal = new double[LutealMax - LutealMin + 1];
        for (var i = 0; i < luteal.Length; i++)
        {
            var x = LutealMin + i - lutealMean;
            luteal[i] = Math.Exp(-x * x / (2 * v));
        }
        return luteal;
    }
}

public static class ConfidenceRule
{
    public static ConfidenceLevel From(int widthDays, int closedCycleCount)
    {
        var level = widthDays <= 4 ? ConfidenceLevel.High
            : widthDays <= 7 ? ConfidenceLevel.Medium
            : ConfidenceLevel.Low;
        if (closedCycleCount < 3 && level == ConfidenceLevel.High) level = ConfidenceLevel.Medium;
        return level;
    }
}
